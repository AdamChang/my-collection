using FluentAssertions;
using MyCollection.Application.Media;
using MyCollection.Infrastructure.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace MyCollection.Tests.Unit;

public class ImageSharpProcessorTests
{
    private readonly ImageSharpProcessor _sut = new();

    private static Stream PngStream(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task Produces_three_sizes_capped_by_longest_edge()
    {
        await using var source = PngStream(3000, 1500);

        var result = await _sut.ProcessAsync(source, CancellationToken.None);

        Size(result.Full).Should().Be(new Size(1600, 800));
        Size(result.Card).Should().Be(new Size(480, 240));
        Size(result.Thumb).Should().Be(new Size(160, 80));
        return;

        static Size Size(byte[] bytes) => Image.Identify(bytes).Size;
    }

    [Fact]
    public async Task Never_upscales_small_images()
    {
        await using var source = PngStream(100, 50);

        var result = await _sut.ProcessAsync(source, CancellationToken.None);

        Image.Identify(result.Full).Size.Should().Be(new Size(100, 50));
        Image.Identify(result.Card).Size.Should().Be(new Size(100, 50));
        Image.Identify(result.Thumb).Size.Should().Be(new Size(100, 50));
    }

    [Fact]
    public async Task Encodes_every_size_as_webp()
    {
        await using var source = PngStream(800, 600);

        var result = await _sut.ProcessAsync(source, CancellationToken.None);

        Image.DetectFormat(result.Full).Should().BeOfType<WebpFormat>();
        Image.DetectFormat(result.Card).Should().BeOfType<WebpFormat>();
        Image.DetectFormat(result.Thumb).Should().BeOfType<WebpFormat>();
    }

    [Fact]
    public async Task Rejects_non_image_content()
    {
        await using var source = new MemoryStream("not an image"u8.ToArray());

        var act = () => _sut.ProcessAsync(source, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidImageException>();
    }
}
