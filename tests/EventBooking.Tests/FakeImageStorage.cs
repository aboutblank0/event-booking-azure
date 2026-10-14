using EventBooking.Core;
using EventBooking.Web.Storage;

namespace EventBooking.Tests;

/// <summary>Keeps blob names in memory instead of uploading to Azure.</summary>
internal class FakeImageStorage : IImageStorage
{
    public HashSet<string> Blobs { get; } = [];
    public int UploadCount { get; private set; }

    /// <summary>When set, uploads fail with this error.</summary>
    public ImageUploadError? FailWith { get; set; }

    public Task<ImageUploadResult> UploadAsync(Stream content, CancellationToken cancellationToken)
    {
        UploadCount++;
        if (FailWith is { } error)
            return Task.FromResult(new ImageUploadResult(error));

        var blobName = $"{Guid.NewGuid():N}.jpg";
        Blobs.Add(blobName);
        return Task.FromResult(new ImageUploadResult(ImageUploadError.None, blobName));
    }

    public Task DeleteAsync(string blobName, CancellationToken cancellationToken)
    {
        Blobs.Remove(blobName);
        return Task.CompletedTask;
    }
}
