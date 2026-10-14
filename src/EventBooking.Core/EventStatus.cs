namespace EventBooking.Core;

// Stored as ints in the database, so never renumber or reorder the values.
public enum EventStatus
{
    Draft = 0,
    Published = 1,
    Cancelled = 2,
    Deleted = 3,
}
