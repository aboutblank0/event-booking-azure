namespace EventBooking.Web.Components.Bookings;

public static class PlacesText
{
    /// <summary>"1 place" or "3 places".</summary>
    public static string Places(int count) => count == 1 ? "1 place" : $"{count} places";

    /// <summary>"Full" or "3 places left", for event lists and details.</summary>
    public static string PlacesLeft(int placesLeft) => placesLeft == 0 ? "Full" : $"{Places(placesLeft)} left";
}
