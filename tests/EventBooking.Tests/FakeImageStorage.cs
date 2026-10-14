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

    /// <summary>Runs after an upload is stored, to change the database mid-request.</summary>
    public Func<Task>? AfterUpload { get; set; }

    public async Task<ImageUploadResult> UploadAsync(Stream content, CancellationToken cancellationToken)
    {
        UploadCount++;
        if (FailWith is { } error)
            return new(error);

        var blobName = $"{Guid.NewGuid():N}.jpg";
        Blobs.Add(blobName);
        if (AfterUpload is not null)
            await AfterUpload();
        return new(ImageUploadError.None, blobName);
    }

    public Task DeleteAsync(string blobName, CancellationToken cancellationToken)
    {
        Blobs.Remove(blobName);
        return Task.CompletedTask;
    }
}
