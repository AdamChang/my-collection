using MyCollection.Application.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace MyCollection.Infrastructure.Imaging;

public sealed class ImageSharpProcessor : IImageProcessor
{
    private const int FullMaxEdge = 1600;
    private const int CardMaxEdge = 480;
    private const int ThumbMaxEdge = 160;

    private static readonly WebpEncoder Encoder = new() { Quality = 82 };

    public async Task<ProcessedImage> ProcessAsync(Stream source, CancellationToken ct)
    {
        Image image;
        try
        {
            image = await Image.LoadAsync(source, ct);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            throw new InvalidImageException(ex);
        }

        using (image)
        {
            StripIdentifyingMetadata(image);

            return new ProcessedImage(
                await ResizeAsync(image, FullMaxEdge, ct),
                await ResizeAsync(image, CardMaxEdge, ct),
                await ResizeAsync(image, ThumbMaxEdge, ct));
        }
    }

    /// <summary>
    /// 清除 EXIF／XMP／IPTC（含 GPS、拍攝時間、機身序號），避免經公開分享外洩（M2）。
    /// 必須先 AutoOrient：像素原本靠 EXIF Orientation 轉正，直接清掉標籤直拍照會躺平。
    /// ICC 刻意保留：只描述色彩空間、不含個資，清掉會讓 Display P3 照片變淡。
    /// </summary>
    private static void StripIdentifyingMetadata(Image image)
    {
        image.Mutate(context => context.AutoOrient());

        image.Metadata.ExifProfile = null;
        image.Metadata.XmpProfile = null;
        image.Metadata.IptcProfile = null;
    }

    private static async Task<byte[]> ResizeAsync(Image source, int maxEdge, CancellationToken ct)
    {
        using var clone = source.Clone(context =>
        {
            var longest = Math.Max(source.Width, source.Height);
            if (longest <= maxEdge)
            {
                return; // 不放大
            }

            context.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(maxEdge, maxEdge),
                Sampler = KnownResamplers.Lanczos3
            });
        });

        using var buffer = new MemoryStream();
        await clone.SaveAsync(buffer, Encoder, ct);

        return buffer.ToArray();
    }
}
