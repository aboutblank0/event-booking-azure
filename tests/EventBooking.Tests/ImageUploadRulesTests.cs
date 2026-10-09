using EventBooking.Core;

namespace EventBooking.Tests;

public class ImageUploadRulesTests
{
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1];
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];
    private static readonly byte[] WebPHeader = "RIFF\x24\0\0\0WEBP"u8.ToArray();

    [Fact]
    public void DetectType_RecognizesJpegPngAndWebP()
    {
        Assert.Equal(ImageUploadRules.Jpeg, ImageUploadRules.DetectType(JpegHeader));
        Assert.Equal(ImageUploadRules.Png, ImageUploadRules.DetectType(PngHeader));
        Assert.Equal(ImageUploadRules.WebP, ImageUploadRules.DetectType(WebPHeader));
    }

    [Theory]
    [InlineData("<svg xmlns=\"ht")]   // SVG can contain scripts, so it's not allowed
    [InlineData("GIF89a......")]
    [InlineData("%PDF-1.7....")]
    [InlineData("MZ..........")]      // Windows executable
    [InlineData("RIFF....WAVE")]      // RIFF audio, not WebP
    public void DetectType_RejectsOtherFiles(string content)
    {
        Assert.Null(ImageUploadRules.DetectType(System.Text.Encoding.ASCII.GetBytes(content)));
    }

    [Fact]
    public void DetectType_RejectsTooShortHeader()
    {
        Assert.Null(ImageUploadRules.DetectType([0xFF, 0xD8]));
        Assert.Null(ImageUploadRules.DetectType("RIFF"u8));
    }

    [Fact]
    public void ValidateFile_AcceptsImageUpToExactly2MB()
    {
        var error = ImageUploadRules.ValidateFile(2 * 1024 * 1024, PngHeader, out var type);

        Assert.Equal(ImageUploadError.None, error);
        Assert.Equal(ImageUploadRules.Png, type);
    }

    [Fact]
    public void ValidateFile_RejectsFileOver2MB()
    {
        var error = ImageUploadRules.ValidateFile(2 * 1024 * 1024 + 1, PngHeader, out var type);

        Assert.Equal(ImageUploadError.TooLarge, error);
        Assert.Null(type);
    }

    [Fact]
    public void ValidateFile_RejectsEmptyFile()
    {
        Assert.Equal(ImageUploadError.Empty, ImageUploadRules.ValidateFile(0, [], out _));
    }

    [Fact]
    public void ValidateFile_RejectsNonImageEvenWithImageSize()
    {
        var error = ImageUploadRules.ValidateFile(1000, "%PDF-1.7...."u8, out var type);

        Assert.Equal(ImageUploadError.NotAnImage, error);
        Assert.Null(type);
    }

    [Theory]
    [InlineData(0, 200, false)]
    [InlineData(199, 200, false)]
    [InlineData(200, 200, true)]
    [InlineData(0, 0, true)]          // a missing limit setting (0) blocks all uploads
    public void IsStorageFull_ComparesCountWithLimit(int existing, int max, bool expected)
    {
        Assert.Equal(expected, ImageUploadRules.IsStorageFull(existing, max));
    }
}
