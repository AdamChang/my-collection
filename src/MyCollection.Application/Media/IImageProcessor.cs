namespace MyCollection.Application.Media;

public sealed record ProcessedImage(byte[] Full, byte[] Card, byte[] Thumb);

/// <summary>來源不是可解析的圖片，由 GlobalExceptionHandler 轉成 400。</summary>
public sealed class InvalidImageException(Exception? innerException = null)
    : Exception("The uploaded file is not a valid image.", innerException);

public interface IImageProcessor
{
    /// <summary>生成 full(1600) / card(480) / thumb(160) 三種尺寸，一律輸出 WebP，不放大原圖。</summary>
    Task<ProcessedImage> ProcessAsync(Stream source, CancellationToken ct);
}
