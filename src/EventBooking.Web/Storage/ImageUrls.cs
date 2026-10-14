namespace EventBooking.Web.Storage;

/// <summary>
/// Builds the address browsers load an event image from. The images container allows anonymous
/// reads, so this is the blob's own URL.
/// </summary>
public sealed class ImageUrls(Uri blobEndpoint, string containerName)
{
    public string For(string blobName) =>
        new Uri(blobEndpoint, $"{Uri.EscapeDataString(containerName)}/{Uri.EscapeDataString(blobName)}").ToString();
}
