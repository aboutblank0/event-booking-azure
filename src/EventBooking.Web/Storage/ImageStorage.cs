using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using EventBooking.Core;

namespace EventBooking.Web.Storage;

public sealed record ImageUploadResult(ImageUploadError Error, string? BlobName = null);

/// <summary>Stores event images in an Azure Blob Storage container.</summary>
public sealed class ImageStorage(BlobContainerClient container, int maxImageCount) : IImageStorage
{
    public string ContainerName => container.Name;
    public int MaxImageCount => maxImageCount;

    public async Task<ImageUploadResult> UploadAsync(Stream content, CancellationToken cancellationToken)
    {
        // Read into memory (at most 2 MB + 1 byte), because an upload stream can only be read once.
        // The extra byte shows whether the file is over the limit.
        var data = new byte[ImageUploadRules.MaxSizeBytes + 1];
        var length = await content.ReadAtLeastAsync(data, data.Length, throwOnEndOfStream: false, cancellationToken);

        var header = data.AsSpan(0, Math.Min(length, ImageUploadRules.HeaderLength));
        var error = ImageUploadRules.ValidateFile(length, header, out var type);
        if (error != ImageUploadError.None)
            return new(error);

        // Counting blobs is a billable Azure call, so do it only for valid files.
        if (ImageUploadRules.IsStorageFull(await CountAsync(cancellationToken), maxImageCount))
            return new(ImageUploadError.StorageFull);

        // Never use the uploaded file name, so users can't overwrite other blobs.
        var blobName = $"{Guid.NewGuid():N}.{type!.Extension}";

        using var upload = new MemoryStream(data, 0, length);
        await container.GetBlobClient(blobName).UploadAsync(upload, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = type.ContentType },
            // Fail rather than overwrite an existing blob.
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
        }, cancellationToken);

        return new(ImageUploadError.None, blobName);
    }

    public Task DeleteAsync(string blobName, CancellationToken cancellationToken) =>
        container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    /// <summary>Counts the stored images, stopping once the limit is reached.</summary>
    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        await foreach (var _ in container.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            if (++count >= maxImageCount)
                break;
        }
        return count;
    }
}
