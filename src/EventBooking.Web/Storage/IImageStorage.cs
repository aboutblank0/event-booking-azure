namespace EventBooking.Web.Storage;

/// <summary>Where event images are stored. An interface so tests can use an in-memory fake.</summary>
public interface IImageStorage
{
    /// <summary>Checks and stores the image under a new random name, returned in the result.</summary>
    Task<ImageUploadResult> UploadAsync(Stream content, CancellationToken cancellationToken);

    Task DeleteAsync(string blobName, CancellationToken cancellationToken);
}
