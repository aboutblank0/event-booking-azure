namespace EventBooking.Core;

public enum ImageUploadError
{
    None,
    Empty,
    TooLarge,
    NotAnImage,
    StorageFull,
}

public sealed record ImageType(string Extension, string ContentType);

/// <summary>
/// Rules for accepting an uploaded image. These are pure functions so they can be
/// unit tested without a web server or Azure.
/// </summary>
public static class ImageUploadRules
{
    public const long MaxSizeBytes = 2 * 1024 * 1024; // 2 MB

    /// <summary>How many bytes from the start of the file are needed to detect its type.</summary>
    public const int HeaderLength = 12;

    public static readonly ImageType Jpeg = new("jpg", "image/jpeg");
    public static readonly ImageType Png = new("png", "image/png");
    public static readonly ImageType WebP = new("webp", "image/webp");

    /// <summary>
    /// Detects the image type from the file's first bytes (its "magic number").
    /// The file name and the Content-Type sent by the browser are chosen by the client,
    /// so they can't be trusted; the bytes themselves are much harder to fake.
    /// SVG is deliberately not supported because it can contain scripts.
    /// </summary>
    public static ImageType? DetectType(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
            return Jpeg;

        if (header.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return Png;

        // WebP: "RIFF" + 4 bytes of file size + "WEBP"
        if (header.Length >= 12
            && header[..4].SequenceEqual("RIFF"u8)
            && header[8..12].SequenceEqual("WEBP"u8))
            return WebP;

        return null;
    }

    /// <summary>Checks the uploaded file itself: not empty, at most 2 MB, and a supported image.</summary>
    public static ImageUploadError ValidateFile(long sizeBytes, ReadOnlySpan<byte> header, out ImageType? type)
    {
        type = null;

        if (sizeBytes <= 0)
            return ImageUploadError.Empty;

        if (sizeBytes > MaxSizeBytes)
            return ImageUploadError.TooLarge;

        type = DetectType(header);
        return type is null ? ImageUploadError.NotAnImage : ImageUploadError.None;
    }

    /// <summary>
    /// Caps the total number of stored images. With a 2 MB file limit this puts a hard
    /// ceiling on how much storage (and money) the app can use.
    /// </summary>
    public static bool IsStorageFull(int existingImageCount, int maxImageCount) =>
        existingImageCount >= maxImageCount;
}
