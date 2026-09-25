using System.Text;
using OpenQuest.Api.Photos;
using SkiaSharp;

namespace OpenQuest.Api.Tests;

public class PhotoProcessorTests
{
    private static readonly IPhotoProcessor Processor = new SkiaPhotoProcessor();

    /// <summary>A JPEG with a marker pattern, plus optional EXIF orientation and a comment segment with "personal data".</summary>
    internal static byte[] MakeJpeg(int w, int h, int seed = 0, int? exifOrientation = null, string? comment = null)
    {
        using var bmp = new SKBitmap(w, h);
        var rnd = new Random(seed);
        var blocks = new SKColor[8, 9];
        for (var y = 0; y < 8; y++) for (var x = 0; x < 9; x++) blocks[y, x] = new SKColor((byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256));
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++) bmp.SetPixel(x, y, blocks[y * 8 / h, x * 9 / w]);
        using var img = SKImage.FromBitmap(bmp);
        var jpeg = img.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray();

        var extra = new List<byte>();
        if (exifOrientation is { } o)
        {
            byte[] tiff = [0x4D, 0x4D, 0x00, 0x2A, 0, 0, 0, 8, 0, 1, 0x01, 0x12, 0, 3, 0, 0, 0, 1, 0, (byte)o, 0, 0, 0, 0, 0, 0];
            var payload = Encoding.ASCII.GetBytes("Exif\0\0").Concat(tiff).ToArray();
            extra.AddRange([0xFF, 0xE1, (byte)((payload.Length + 2) >> 8), (byte)((payload.Length + 2) & 0xFF)]);
            extra.AddRange(payload);
        }
        if (comment is not null)
        {
            var c = Encoding.UTF8.GetBytes(comment);
            extra.AddRange([0xFF, 0xFE, (byte)((c.Length + 2) >> 8), (byte)((c.Length + 2) & 0xFF)]);
            extra.AddRange(c);
        }
        return [.. jpeg.Take(2), .. extra, .. jpeg.Skip(2)];
    }

    [Fact]
    public void Metadata_is_stripped()
    {
        var input = MakeJpeg(320, 240, exifOrientation: 1, comment: "SecretCamera GPS 51.96,7.62");
        Assert.Contains("SecretCamera", Encoding.Latin1.GetString(input));

        var output = Processor.Process(input).Jpeg;
        var text = Encoding.Latin1.GetString(output);
        Assert.DoesNotContain("SecretCamera", text);
        Assert.DoesNotContain("Exif", text);
        Assert.Equal(0xFF, output[0]);
        Assert.Equal(0xD8, output[1]);
    }

    [Theory]
    [InlineData(1, 320, 240)]
    [InlineData(6, 240, 320)] // rotate 90: portrait photo taken with the phone held upright
    [InlineData(8, 240, 320)]
    [InlineData(3, 320, 240)]
    public void Exif_orientation_is_applied_before_metadata_is_dropped(int orientation, int expectedW, int expectedH)
    {
        var p = Processor.Process(MakeJpeg(320, 240, exifOrientation: orientation));
        Assert.Equal((expectedW, expectedH), (p.Width, p.Height));
    }

    [Fact]
    public void Large_images_are_downscaled()
    {
        var p = Processor.Process(MakeJpeg(4000, 3000));
        Assert.Equal(2048, Math.Max(p.Width, p.Height));
    }

    [Fact]
    public void Perceptual_hash_is_stable_across_recompression_and_differs_between_images()
    {
        var a = Processor.Process(MakeJpeg(640, 480, seed: 1));
        var aSmaller = Processor.Process(MakeJpeg(320, 240, seed: 1));
        var b = Processor.Process(MakeJpeg(640, 480, seed: 2));
        Assert.True(PerceptualHash.HammingDistance(a.Hash, aSmaller.Hash) <= 4);
        Assert.True(PerceptualHash.HammingDistance(a.Hash, b.Hash) > 8);
    }

    [Fact]
    public void Garbage_is_rejected()
        => Assert.Throws<InvalidImageException>(() => Processor.Process(Encoding.UTF8.GetBytes("not an image")));
}
