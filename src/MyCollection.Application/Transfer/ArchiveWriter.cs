using System.IO.Compression;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Transfer;

/// <summary>
/// 匯出核心。寫入任意 Stream，因此匯出端點（HttpResponse.Body）與
/// 匯入前的自動備份（備份檔）可以共用同一份邏輯。
///
/// 單趟串流，不落暫存檔也不整包進記憶體，所以耗用與收藏規模無關。
/// </summary>
public sealed class ArchiveWriter(
    ITransferRepository repository,
    IFileStorage storage,
    TimeProvider timeProvider)
{
    public async Task WriteAsync(Stream destination, CancellationToken ct)
    {
        // 這三次讀取沒有 transaction（單機 MongoDB 沒有），中間若有並發編輯
        // （例如另一個分頁刪掉品類），archive 可能出現「品項指向 archive 內
        // 不存在的品類」。自我觸發的匯出時間窗很短，這裡選擇記錄這個取捨，
        // 而不是為了這個邊角案例引入 transaction 或多讀一次校驗。
        var categories = await repository.ListOwnCategoriesAsync(ct);
        var items = await repository.ListExportableItemsAsync(ct);
        var shareLinks = await repository.ListOwnShareLinksAsync(ct);

        var manifest = new ArchiveManifest
        {
            ExportedAt = timeProvider.GetUtcNow().UtcDateTime,
            Categories = [.. categories.Select(ArchiveMapper.ToArchive)],
            Items = [.. items.Select(ArchiveMapper.ToArchive)],
            ShareLinks = [.. shareLinks.Select(ArchiveMapper.ToArchive)]
        };

        // ZipArchiveEntry 的寫入串流（內部的 WrappedStream）沒有覆寫 DisposeAsync，
        // 就算全程用 OpenAsync／await using，關閉一個 entry 時還是會落回同步 Dispose，
        // 對底層 Stream 發出同步 Write/Flush（dotnet/runtime#107171、#1560，官方標
        // Future、不會修）。直接包 HttpResponse.Body 會在收尾時炸掉。
        // SyncSafeBufferedStream 把這些同步呼叫吃進記憶體緩衝，只有我們自己呼叫
        // FlushBufferedAsync 時才真的用非同步 I/O 送到 destination；緩衝在每個
        // entry 關閉後就會被沖掉，尖峰記憶體只跟單一 entry 大小成正比。
        var buffered = new SyncSafeBufferedStream(destination);

        await using (var archive = new ZipArchive(buffered, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using (var manifestEntry = await archive.CreateEntry(ArchiveManifest.FileName).OpenAsync(ct))
            {
                await ArchiveManifestSerializer.WriteAsync(manifestEntry, manifest, ct);
            }
            await buffered.FlushBufferedAsync(ct);

            foreach (var item in items)
            {
                foreach (var image in item.Images)
                {
                    // 檔案遺失不由匯出端處理：manifest 照 DB 寫，zip 內少一個 entry，
                    // 由匯入端偵測並降級為 warning。這讓匯出維持單趟串流，
                    // 不必為了預檢而把每個檔案開兩次。
                    await using var source = await storage.OpenReadAsync(image.Path, ct);
                    if (source is null)
                    {
                        continue;
                    }

                    await using var entry = await archive.CreateEntry(ArchivePaths.Image(item.Id, image.Id)).OpenAsync(ct);
                    await source.CopyToAsync(entry, ct);
                    await buffered.FlushBufferedAsync(ct);
                }
            }
        }

        // ZipArchive.DisposeAsync 本身有正確覆寫（負責寫中央目錄），但一樣是先
        // 寫進 buffered，離開上面的 await using 區塊後才把最後這批位元組送出去。
        await buffered.FlushBufferedAsync(ct);
    }

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
    }
}
