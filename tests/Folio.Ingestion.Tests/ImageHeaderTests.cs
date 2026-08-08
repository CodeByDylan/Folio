using System.Text;
using Folio.Domain.Model;
using Folio.Ingestion.Media;

namespace Folio.Ingestion.Tests;

public sealed class ImageHeaderTests
{
    [Test]
    public async Task Png_Dimensions_Come_From_The_Ihdr_Chunk()
    {
        bool read = ImageHeader.TryRead(Images.Png(1280, 720), out MediaSize size);

        await Assert.That(read).IsTrue();
        await Assert.That(size.Width).IsEqualTo(1280);
        await Assert.That(size.Height).IsEqualTo(720);
    }

    [Test]
    public async Task Gif_Dimensions_Are_Little_Endian()
    {
        bool read = ImageHeader.TryRead(Images.Gif(640, 480), out MediaSize size);

        await Assert.That(read).IsTrue();
        await Assert.That(size.Width).IsEqualTo(640);
        await Assert.That(size.Height).IsEqualTo(480);
    }

    [Test]
    public async Task Jpeg_Dimensions_Come_From_The_Start_Of_Frame()
    {
        bool read = ImageHeader.TryRead(Images.Jpeg(800, 600), out MediaSize size);

        await Assert.That(read).IsTrue();
        await Assert.That(size.Width).IsEqualTo(800);
        await Assert.That(size.Height).IsEqualTo(600);
    }

    [Test]
    public async Task Jpeg_Skips_Segments_Before_The_Start_Of_Frame()
    {
        // A JFIF APP0 segment sits between the signature and the frame in almost every real file.
        byte[] app0 = [0xFF, 0xE0, 0x00, 0x10, .. "JFIF\0"u8, 0x01, 0x01, 0, 0, 1, 0, 1, 0, 0];
        byte[] jpeg = [0xFF, 0xD8, .. app0, .. Images.Jpeg(1024, 768)[2..]];

        bool read = ImageHeader.TryRead(jpeg, out MediaSize size);

        await Assert.That(read).IsTrue();
        await Assert.That(size.Width).IsEqualTo(1024);
        await Assert.That(size.Height).IsEqualTo(768);
    }

    [Test]
    public async Task Lossy_Webp_Dimensions_Are_Fourteen_Bit()
    {
        bool read = ImageHeader.TryRead(Images.Webp(1920, 1080), out MediaSize size);

        await Assert.That(read).IsTrue();
        await Assert.That(size.Width).IsEqualTo(1920);
        await Assert.That(size.Height).IsEqualTo(1080);
    }

    [Test]
    public async Task A_Jpeg_With_Large_Metadata_Needs_A_Longer_Read()
    {
        byte[] padded = Images.Padded(1600, 2400, 1200);

        await Assert.That(ImageHeader.TryRead(padded.AsSpan(0, ImageHeader.ProbeLength), out _)).IsFalse();
        await Assert.That(ImageHeader.NeedsLongerRead(padded.AsSpan(0, ImageHeader.ProbeLength))).IsTrue();

        bool read = ImageHeader.TryRead(padded, out MediaSize size);

        await Assert.That(read).IsTrue();
        await Assert.That(size.Width).IsEqualTo(1600);
        await Assert.That(size.Height).IsEqualTo(2400);
    }

    [Test]
    public async Task Binary_That_Matches_No_Format_Is_Not_Read_As_Svg()
    {
        byte[] binary = [0x00, 0x01, 0x02, 0x3C, 0x73, 0x76, 0x67, 0x00];

        await Assert.That(ImageHeader.TryRead(binary, out _)).IsFalse();
        await Assert.That(ImageHeader.NeedsLongerRead(binary)).IsFalse();
    }

    [Test]
    public async Task Svg_Prefers_Explicit_Width_And_Height()
    {
        bool read = ImageHeader.TryRead(
            Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" width="120px" height="60px"></svg>"""),
            out MediaSize size);

        await Assert.That(read).IsTrue();
        await Assert.That(size.Width).IsEqualTo(120);
        await Assert.That(size.Height).IsEqualTo(60);
    }

    [Test]
    public async Task Svg_Falls_Back_To_The_View_Box()
    {
        bool read = ImageHeader.TryRead(
            Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 16"></svg>"""),
            out MediaSize size);

        await Assert.That(read).IsTrue();
        await Assert.That(size.Width).IsEqualTo(24);
        await Assert.That(size.Height).IsEqualTo(16);
    }

    [Test]
    [Arguments("not an image at all")]
    [Arguments("")]
    [Arguments("\x89PNG")]
    public async Task An_Unrecognized_Header_Is_Refused(string content)
    {
        await Assert.That(ImageHeader.TryRead(Encoding.UTF8.GetBytes(content), out _)).IsFalse();
    }

    [Test]
    public async Task A_Truncated_Png_Is_Refused_Rather_Than_Guessed()
    {
        await Assert.That(ImageHeader.TryRead(Images.Png(100, 100)[..12], out _)).IsFalse();
    }

}
