using System.Buffers.Binary;
using System.Text;
using Server.Features.Business;
using SkiaSharp;
using Xunit;

namespace Server.Tests;

public sealed class LogoImageTests
{
    [Fact]
    public void PngMetadataAndTrailingPayloadAreRemovedByReencoding()
    {
        var input = Convert.FromBase64String(BusinessLogoTests.Image());
        var text = "comment\0synthetic-secret-metadata"u8.ToArray();
        var chunk = new byte[text.Length + 12]; BinaryPrimitives.WriteInt32BigEndian(chunk, text.Length);
        "tEXt"u8.CopyTo(chunk.AsSpan(4)); text.CopyTo(chunk.AsSpan(8));
        uint crc = uint.MaxValue;
        foreach (var value in chunk.AsSpan(4, text.Length + 4))
        { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0); }
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(text.Length + 8), ~crc);
        var withMetadata = input[..^12].Concat(chunk).Concat(input[^12..]).Concat("<script>synthetic</script>"u8.ToArray()).ToArray();
        var output = Assert.IsType<LogoImage.Normalized>(LogoImage.Normalize(Convert.ToBase64String(withMetadata)));
        var decoded = Encoding.Latin1.GetString(output.Png);
        Assert.DoesNotContain("synthetic-secret-metadata", decoded); Assert.DoesNotContain("<script>", decoded);
        using var data = SKData.CreateCopy(output.Png); using var codec = SKCodec.Create(data);
        Assert.Equal(SKEncodedImageFormat.Png, codec.EncodedFormat); Assert.Equal(40, output.Width); Assert.Equal(20, output.Height);
    }
    [Fact]
    public void BoundaryPixelsAndNarrowLogosStayWithinStorageLimit()
    {
        foreach (var (width, height) in new[] { (2048, 2048), (2048, 1), (1, 2048), (1, 1) })
        {
            var output = Assert.IsType<LogoImage.Normalized>(LogoImage.Normalize(BusinessLogoTests.Image(width: width, height: height)));
            Assert.InRange(output.Width, 1, 512); Assert.InRange(output.Height, 1, 512); Assert.InRange(output.Png.Length, 1, LogoImage.MaxStoredBytes);
        }
    }
    [Fact]
    public void CorruptPixelsCannotBecomeAnApparentlyValidLogo()
    {
        var png = Convert.FromBase64String(BusinessLogoTests.Image());
        // Geçerli başlık tek başına yeterli değildir; IDAT içeriği bozulur.
        var idat = Encoding.Latin1.GetString(png).IndexOf("IDAT", StringComparison.Ordinal);
        Assert.True(idat > 0); Array.Fill(png, (byte)0, idat + 4, 8);
        Assert.Null(LogoImage.Normalize(Convert.ToBase64String(png)));
    }
}
