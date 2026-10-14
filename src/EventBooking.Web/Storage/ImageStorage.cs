using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using EventBooking.Core;

namespace EventBooking.Web.Storage;

public sealed record ImageUploadResult(ImageUploadError Error, string? BlobName = null);

/// <summary>Stores event images in an Azure Blob Storage container.</summary>
public sealed class ImageStorage(BlobContainerClient container, int maxImageCount)
{
    public string ContainerName => container.Name;
    public int MaxImageCount => maxImageCount;

    public async Task<ImageUploadResult> UploadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length > ImageUploadRules.MaxSizeBytes)
            return new(ImageUploadError.TooLarge);

        var header = new byte[ImageUploadRules.HeaderLength];
        int headerLength;
        using (var stream = file.OpenReadStream())
        {
            headerLength = await stream.ReadAtLeastAsync(
                header, header.Length, throwOnEndOfStream: false, cancellationToken);
        }

        var error = ImageUploadRules.ValidateFile(file.Length, header.AsSpan(0, headerLength), out var type);
        if (error != ImageUploadError.None)
            return new(error);

        // Counting blobs is a billable Azure call, so do it only for valid files.
        if (ImageUploadRules.IsStorageFull(await CountAsync(cancellationToken), maxImageCount))
            return new(ImageUploadError.StorageFull);

        // Never use the uploaded file name, so users can't overwrite other blobs.
        var blobName = $"{Guid.NewGuid():N}.{type!.Extension}";

        using var upload = file.OpenReadStream();
        await container.GetBlobClient(blobName).UploadAsync(upload, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = type.ContentType },
            // Fail rather than overwrite an existing blob.
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
        }, cancellationToken);

        return new(ImageUploadError.None, blobName);
    }

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
