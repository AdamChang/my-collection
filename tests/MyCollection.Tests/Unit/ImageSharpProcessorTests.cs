using System.Buffers.Binary;
using FluentAssertions;
using MyCollection.Application.Media;
using MyCollection.Infrastructure.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.Metadata.Profiles.Iptc;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
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

    /// <summary>模擬手機照片：JPEG 帶 GPS 與方向的 EXIF、XMP、IPTC。</summary>
    private static Stream PhotoWithMetadata(int width, int height, ushort orientation)
    {
        using var image = new Image<Rgba32>(width, height);

        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Orientation, orientation);
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, [new Rational(25, 1), new Rational(2, 1), new Rational(0, 1)]);
        image.Metadata.ExifProfile = exif;
        image.Metadata.XmpProfile = new XmpProfile(
            "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"/></x:xmpmeta>"u8.ToArray());
        var iptc = new IptcProfile();
        iptc.SetValue(IptcTag.City, "Taipei");
        image.Metadata.IptcProfile = iptc;
        image.Metadata.IccProfile = new IccProfile(MinimalIccProfile());

        var stream = new MemoryStream();
        image.Save(stream, new JpegEncoder());
        stream.Position = 0;
        return stream;
    }

    /// <summary>只有 header、沒有 tag 的 ICC v4 profile；足以驗證 profile 有沒有被帶到輸出。</summary>
    private static byte[] MinimalIccProfile()
    {
        var bytes = new byte[132];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, 132);   // profile size
        bytes[8] = 0x04;                                     // version 4.x
        "mntr"u8.CopyTo(bytes.AsSpan(12));                   // device class
        "RGB "u8.CopyTo(bytes.AsSpan(16));                   // data colour space
        "XYZ "u8.CopyTo(bytes.AsSpan(20));                   // PCS
        "acsp"u8.CopyTo(bytes.AsSpan(36));                   // file signature
        return bytes;                                        // tag count（offset 128）為 0
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
    public async Task Strips_exif_xmp_and_iptc_from_every_size()
    {
        await using var source = PhotoWithMetadata(300, 200, orientation: 1);

        var result = await _sut.ProcessAsync(source, CancellationToken.None);

        foreach (var output in new[] { result.Full, result.Card, result.Thumb })
        {
            var metadata = Image.Identify(output).Metadata;
            metadata.ExifProfile.Should().BeNull();
            metadata.XmpProfile.Should().BeNull();
            metadata.IptcProfile.Should().BeNull();
        }
    }

    [Fact]
    public async Task Keeps_icc_profile_so_wide_gamut_colours_do_not_shift()
    {
        await using var source = PhotoWithMetadata(300, 200, orientation: 1);

        var result = await _sut.ProcessAsync(source, CancellationToken.None);

        Image.Identify(result.Full).Metadata.IccProfile.Should().NotBeNull();
    }

    [Fact]
    public async Task Applies_exif_orientation_to_pixels_before_stripping()
    {
        // Orientation=6：相機橫向存檔、需順時針轉 90° 顯示。清掉標籤前不先轉正，直拍照就會躺平。
        await using var source = PhotoWithMetadata(300, 200, orientation: 6);

        var result = await _sut.ProcessAsync(source, CancellationToken.None);

        Image.Identify(result.Full).Size.Should().Be(new Size(200, 300));
    }

    [Fact]
    public async Task Rejects_non_image_content()
    {
        await using var source = new MemoryStream("not an image"u8.ToArray());

        var act = () => _sut.ProcessAsync(source, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidImageException>();
    }
}
