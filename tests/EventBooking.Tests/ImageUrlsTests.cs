using EventBooking.Web.Storage;

namespace EventBooking.Tests;

public class ImageUrlsTests
{
    [Theory]
    [InlineData("https://account.blob.core.windows.net/")]
    [InlineData("https://account.blob.core.windows.net")]
    public void For_JoinsEndpointContainerAndBlobName(string endpoint)
    {
        var urls = new ImageUrls(new Uri(endpoint), "event-images");

        Assert.Equal("https://account.blob.core.windows.net/event-images/abc123.jpg", urls.For("abc123.jpg"));
    }
}
