namespace Amsterfam.Api.Services;

/// <summary>Identifies the raster image formats we accept from their magic bytes.</summary>
public static class ImageSniffer
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Returns the MIME type, or null if the data isn't a JPEG, PNG or WebP image.</summary>
    public static string? Detect(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(Jpeg))
            return "image/jpeg";
        if (data.StartsWith(Png))
            return "image/png";
        // RIFF....WEBP
        if (
            data.Length >= 12
            && data[..4].SequenceEqual("RIFF"u8)
            && data[8..12].SequenceEqual("WEBP"u8)
        )
            return "image/webp";
        return null;
    }
}
