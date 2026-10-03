using SkiaSharp;

namespace Server.Features.Business;

public static class LogoImage
{
    public const int MaxInputBytes = 1024 * 1024;
    public const int MaxInputSide = 2048;
    public const int MaxOutputSide = 512;
    public const int MaxStoredBytes = 1100000;
    public sealed record Normalized(byte[] Png, int Width, int Height);

    public static Normalized? Normalize(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64) || base64.Length > 4 * ((MaxInputBytes + 2) / 3)) return null;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch (FormatException) { return null; }
        if (bytes.Length == 0 || bytes.Length > MaxInputBytes) return null;
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg) ||
            codec.Info.Width is < 1 or > MaxInputSide || codec.Info.Height is < 1 or > MaxInputSide || codec.FrameCount > 1) return null;
        // Piksel sınırı bellek ayırmadan denetlenir; yüklenen dosya/metadata yayımlanmaz.
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success) return null;
        var ratio = Math.Min(1d, (double)MaxOutputSide / Math.Max(info.Width, info.Height));
        var width = Math.Max(1, (int)Math.Round(info.Width * ratio));
        var height = Math.Max(1, (int)Math.Round(info.Height * ratio));
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        if (surface is null) return null;
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawBitmap(bitmap, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear));
        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded is null || encoded.Size > MaxStoredBytes ? null : new(encoded.ToArray(), width, height);
    }
}
