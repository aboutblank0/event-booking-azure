using EventBooking.Web.Components.Bookings;

namespace EventBooking.Tests;

public class PlacesTextTests
{
    [Theory]
    [InlineData(0, "Full")]
    [InlineData(1, "1 place left")]
    [InlineData(12, "12 places left")]
    public void PlacesLeft_ReadsNaturally(int placesLeft, string expected)
    {
        Assert.Equal(expected, PlacesText.PlacesLeft(placesLeft));
    }
}
