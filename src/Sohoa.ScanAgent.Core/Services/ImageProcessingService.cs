using SkiaSharp;
using Sohoa.ScanAgent.Core.Models;

namespace Sohoa.ScanAgent.Core.Services;

/// <summary>
/// Applies rotate and crop transforms to a TIFF/image and returns processed JPEG bytes.
/// </summary>
public static class ImageProcessingService
{
    /// <summary>
    /// Returns JPEG bytes for a page, applying rotation and crop from metadata.
    /// </summary>
    public static byte[] GetPreviewJpeg(string tiffPath, PageMeta meta, int maxDimension = 600)
    {
        using var original = SKBitmap.Decode(tiffPath)
            ?? throw new InvalidOperationException($"Cannot decode image: {tiffPath}");

        var bitmap = ApplyTransforms(original, meta);

        // Resize keeping aspect ratio
        float scale = Math.Min((float)maxDimension / bitmap.Width, (float)maxDimension / bitmap.Height);
        if (scale < 1f)
        {
            var resized = bitmap.Resize(
                new SKImageInfo((int)(bitmap.Width * scale), (int)(bitmap.Height * scale)),
                SKFilterQuality.Medium);
            bitmap.Dispose();
            bitmap = resized;
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        bitmap.Dispose();
        return data.ToArray();
    }

    /// <summary>
    /// Returns full-resolution processed JPEG bytes (for PDF generation).
    /// </summary>
    public static byte[] GetProcessedJpeg(string tiffPath, PageMeta meta)
    {
        using var original = SKBitmap.Decode(tiffPath)
            ?? throw new InvalidOperationException($"Cannot decode image: {tiffPath}");

        using var bitmap = ApplyTransforms(original, meta);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        return data.ToArray();
    }

    private static SKBitmap ApplyTransforms(SKBitmap source, PageMeta meta)
    {
        SKBitmap working = source;

        // Crop first
        if (meta.Crop is { } crop)
        {
            var rect = new SKRectI(crop.X, crop.Y, crop.X + crop.Width, crop.Y + crop.Height);
            var cropped = new SKBitmap(rect.Width, rect.Height);
            using var canvas = new SKCanvas(cropped);
            canvas.DrawBitmap(working, -rect.Left, -rect.Top);
            working = cropped;
        }

        // Then rotate
        if (meta.Rotation != 0)
        {
            working = RotateBitmap(working, meta.Rotation);
        }

        return working;
    }

    private static SKBitmap RotateBitmap(SKBitmap source, int degrees)
    {
        bool swap = degrees == 90 || degrees == 270;
        int w = swap ? source.Height : source.Width;
        int h = swap ? source.Width : source.Height;

        var rotated = new SKBitmap(w, h);
        using var canvas = new SKCanvas(rotated);
        canvas.Translate(w / 2f, h / 2f);
        canvas.RotateDegrees(degrees);
        canvas.Translate(-source.Width / 2f, -source.Height / 2f);
        canvas.DrawBitmap(source, 0, 0);
        source.Dispose();
        return rotated;
    }
}
