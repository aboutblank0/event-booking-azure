namespace EventBooking.Core;

/// <summary>
/// The values are written out explicitly because they are stored in the database as ints.
/// Reordering or inserting a value would silently change what existing rows mean.
/// </summary>
public enum EventStatus
{
    Draft = 0,
    Published = 1,
    Cancelled = 2,
    Deleted = 3,
}
