namespace EventBooking.Core;

// Stored as ints in the database, so never renumber or reorder the values.
public enum BookingStatus
{
    Confirmed = 0,
    Cancelled = 1,
}
