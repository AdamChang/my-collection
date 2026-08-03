using System.IO.Compression;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Transfer;

/// <summary>
/// 匯出核心。zip 內的 entry 名就是 storage 的相對路徑，三種尺寸原樣打包，
/// 因此匯入端只要把 entry 寫回同一個路徑就完成還原——不需要重壓圖片，
/// 也不需要知道任何語意。
///
/// 圖片一次一張串流讀寫，不會同時把多張圖片放進記憶體。另外為了繞過
/// <see cref="SyncSafeBufferedStream"/> 要處理的同步 I/O 限制，額外多墊了
/// 最多一個 entry 大小的緩衝。
/// </summary>
public sealed class ImageArchiveWriter(
    IImageArchiveRepository repository,
    IFileStorage storage,
    IUserContext userContext,
    TimeProvider timeProvider)
{
    public async Task WriteAsync(Stream destination, CancellationToken ct)
    {
        var items = await repository.ListItemsWithImagesAsync(ct);
        var missing = new List<MissingImageFile>();
        var fileCount = 0;

        // ZipArchiveEntry 的寫入串流（內部的 WrappedStream）沒有覆寫 DisposeAsync，
        // 就算全程用 OpenAsync／await using，關閉一個 entry 時還是會落回同步 Dispose，
        // 對底層 Stream 發出同步 Write/Flush（dotnet/runtime#107171、#1560，官方標
        // Future、不會修）。直接包 HttpResponse.Body 會在收尾時炸掉。
        // SyncSafeBufferedStream 把這些同步呼叫吃進記憶體緩衝，只有我們自己呼叫
        // FlushBufferedAsync 時才真的用非同步 I/O 送到 destination；緩衝在每個
        // entry 關閉後就會被沖掉，尖峰記憶體只跟單一 entry 大小成正比。
        // buffered 自己只持有一塊 MemoryStream，用 await using 讓它在方法結束時
        // 一併釋放；它的 Dispose 不會碰 inner（destination 的所有權還是呼叫端的），
        // 所以釋放順序跟下面的 FlushBufferedAsync 呼叫先後無關，兩者不會互相踩到。
        await using var buffered = new SyncSafeBufferedStream(destination);

        await using (var archive = new ZipArchive(buffered, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in items)
            {
                foreach (var path in item.Images.SelectMany(StoredPaths))
                {
                    await using var source = await storage.OpenReadAsync(path, ct);

                    if (source is null)
                    {
                        missing.Add(new MissingImageFile { ItemName = item.Name, Path = path });
                        continue;
                    }

                    await using (var entry = await archive.CreateEntry(path).OpenAsync(ct))
                    {
                        await source.CopyToAsync(entry, ct);
                    }

                    await buffered.FlushBufferedAsync(ct);
                    fileCount++;
                }
            }

            // manifest 刻意寫在最後：到這裡才知道實際帶走幾個檔、少了哪些。
            // zip 的 entry 順序是自由的，匯入端靠中央目錄定址，讀得到就好。
            var manifest = new ImageArchiveManifest
            {
                ExportedAt = timeProvider.GetUtcNow().UtcDateTime,
                OwnerId = userContext.UserId.ToString(),
                FileCount = fileCount,
                Missing = missing
            };

            await using (var entry = await archive.CreateEntry(ImageArchiveManifest.FileName).OpenAsync(ct))
            {
                await ImageArchiveManifestSerializer.WriteAsync(entry, manifest, ct);
            }

            await buffered.FlushBufferedAsync(ct);
        }

        // ZipArchive.DisposeAsync 本身有正確覆寫（負責寫中央目錄），但一樣是先
        // 寫進 buffered，離開上面的 await using 區塊後才把最後這批位元組送出去。
        // 這一定要在 buffered 被釋放之前執行完，否則中央目錄永遠出不去。
        await buffered.FlushBufferedAsync(ct);
    }

    private static IEnumerable<string> StoredPaths(ItemImage image) =>
        [image.Path, image.CardPath, image.ThumbPath];

    /// <summary>
    /// 見 <see cref="WriteAsync"/> 內的說明：只吸收 ZipArchiveEntry 內部在關閉
    /// entry 時發出的同步呼叫，不代表這個型別本身支援真正的隨機存取，所以
    /// Seek／Read／Length 一律不支援——archive 寫入路徑本來就不需要它們。
    /// </summary>
    private sealed class SyncSafeBufferedStream(Stream inner) : Stream
    {
        private readonly MemoryStream _buffer = new();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        // 刻意不轉呼叫 inner：讓它落地到 Kestrel 就是這整個類別要避免的事。
        // 真正送出資料只透過 FlushBufferedAsync。
        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            _buffer.Write(buffer, offset, count);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            _buffer.Write(buffer, offset, count);
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _buffer.Write(buffer.Span);
            return default;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public async Task FlushBufferedAsync(CancellationToken ct)
        {
            if (_buffer.Length == 0)
            {
                return;
            }

            _buffer.Position = 0;
            await _buffer.CopyToAsync(inner, ct);
            _buffer.SetLength(0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 只釋放自己的緩衝。inner 是呼叫端的（匯出端點的 HttpResponse.Body），
                // 它的生命週期不歸這裡管。
                _buffer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
