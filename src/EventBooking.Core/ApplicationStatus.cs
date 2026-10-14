namespace EventBooking.Core;

// Stored as ints in the database, so never renumber or reorder the values.
public enum ApplicationStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
}
