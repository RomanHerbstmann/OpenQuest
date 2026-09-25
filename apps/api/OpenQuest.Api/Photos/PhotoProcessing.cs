using SkiaSharp;

namespace OpenQuest.Api.Photos;

public sealed record ProcessedPhoto(byte[] Jpeg, long Hash, int Width, int Height);

public class InvalidImageException(string message) : Exception(message);

/// <summary>Turns an uploaded image into the version we store, and fingerprints it.</summary>
public interface IPhotoProcessor
{
    /// <exception cref="InvalidImageException">The input is not a usable image.</exception>
    ProcessedPhoto Process(byte[] input);
}

public static class PerceptualHash
{
    public static int HammingDistance(long a, long b) => System.Numerics.BitOperations.PopCount((ulong)(a ^ b));
}

/// <summary>
/// Decodes an uploaded image, applies its EXIF orientation, and re-encodes it as JPEG.
/// Re-encoding drops all metadata (EXIF/GPS/maker notes), so nothing personal is stored.
/// Also computes a 64-bit difference hash (dHash) for duplicate detection.
/// </summary>
public sealed class SkiaPhotoProcessor : IPhotoProcessor
{
    private const int MaxDimension = 2048;
    private const long MaxPixels = 60_000_000;

    public ProcessedPhoto Process(byte[] input)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(input))
                          ?? throw new InvalidImageException("Unsupported or corrupt image.");
        if ((long)codec.Info.Width * codec.Info.Height > MaxPixels)
            throw new InvalidImageException("Image resolution too large.");

        using var decoded = SKBitmap.Decode(codec) ?? throw new InvalidImageException("Could not decode image.");
        using var oriented = ApplyOrigin(decoded, codec.EncodedOrigin);
        using var scaled = Downscale(oriented);

        using var image = SKImage.FromBitmap(scaled);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85)
                         ?? throw new InvalidImageException("Could not encode image.");
        return new ProcessedPhoto(data.ToArray(), DifferenceHash(scaled), scaled.Width, scaled.Height);
    }

    private static SKBitmap Downscale(SKBitmap src)
    {
        var longest = Math.Max(src.Width, src.Height);
        if (longest <= MaxDimension) return src.Copy();
        var f = (double)MaxDimension / longest;
        var info = new SKImageInfo(Math.Max(1, (int)(src.Width * f)), Math.Max(1, (int)(src.Height * f)));
        return src.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
               ?? throw new InvalidImageException("Could not resize image.");
    }

    private static long DifferenceHash(SKBitmap bmp)
    {
        var info = new SKImageInfo(9, 8, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var small = bmp.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
                          ?? throw new InvalidImageException("Could not hash image.");
        double Lum(int x, int y)
        {
            var c = small.GetPixel(x, y);
            return 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;
        }

        ulong hash = 0;
        var bit = 0;
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++, bit++)
            if (Lum(x, y) < Lum(x + 1, y)) hash |= 1UL << bit;
        return unchecked((long)hash);
    }

    private static SKBitmap ApplyOrigin(SKBitmap src, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return src.Copy();

        float w = src.Width, h = src.Height;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

        // Maps source pixel (x,y) to destination: x' = a*x + b*y + tx ; y' = c*x + d*y + ty
        var m = origin switch
        {
            SKEncodedOrigin.TopRight => Mat(-1, 0, w, 0, 1, 0),
            SKEncodedOrigin.BottomRight => Mat(-1, 0, w, 0, -1, h),
            SKEncodedOrigin.BottomLeft => Mat(1, 0, 0, 0, -1, h),
            SKEncodedOrigin.LeftTop => Mat(0, 1, 0, 1, 0, 0),
            SKEncodedOrigin.RightTop => Mat(0, -1, h, 1, 0, 0),
            SKEncodedOrigin.RightBottom => Mat(0, -1, h, -1, 0, w),
            SKEncodedOrigin.LeftBottom => Mat(0, 1, 0, -1, 0, w),
            _ => SKMatrix.Identity,
        };

        var dst = new SKBitmap(swap ? src.Height : src.Width, swap ? src.Width : src.Height);
        using var canvas = new SKCanvas(dst);
        canvas.SetMatrix(m);
        canvas.DrawBitmap(src, 0, 0);
        return dst;

        static SKMatrix Mat(float a, float b, float tx, float c, float d, float ty) => new(a, b, tx, c, d, ty, 0, 0, 1);
    }
}
