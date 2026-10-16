namespace EventBooking.Core;

/// <summary>
/// One place at an event for one user. Cancelled bookings are kept, as a history.
/// Setters are private, so every change goes through a method that checks the rules.
/// </summary>
public class Booking
{
    // For EF Core when loading from the database.
    private Booking() { }

    public int Id { get; private set; }
    public int EventId { get; private set; }
    public string UserId { get; private set; } = "";
    public BookingStatus Status { get; private set; }
    public DateTimeOffset BookedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>
    /// Why the user can't book the event right now, or null if they can. Doesn't know whether the
    /// event is hidden; the caller checks that.
    /// </summary>
    public static string? ReasonCannotBook(
        Event ev, string userId, int confirmedBookings, bool hasConfirmedBooking, DateTimeOffset now)
    {
        if (ev.Status == EventStatus.Cancelled)
            return "This event has been cancelled.";
        if (ev.Status != EventStatus.Published)
            return "This event isn't open for booking.";
        if (ev.StartsAt <= now)
            return "Booking has closed: this event has already started.";
        if (ev.IsOrganisedBy(userId))
            return "You can't book your own event.";
        if (hasConfirmedBooking)
            return "You have already booked this event.";
        if (ev.PlacesLeft(confirmedBookings) == 0)
            return "This event is currently full. Places free up when people cancel, so check back later.";
        return null;
    }

    /// <summary>Creates a Confirmed booking, if <see cref="ReasonCannotBook"/> allows it.</summary>
    public static Booking Create(
        Event ev, string userId, int confirmedBookings, bool hasConfirmedBooking, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new BusinessRuleException("A booking must have a user.");

        var reason = ReasonCannotBook(ev, userId, confirmedBookings, hasConfirmedBooking, now);
        if (reason is not null)
            throw new BusinessRuleException(reason);

        return new Booking
        {
            EventId = ev.Id,
            UserId = userId,
            Status = BookingStatus.Confirmed,
            BookedAt = now,
        };
    }

    public bool IsBookedBy(string userId) => UserId == userId;

    /// <summary>Frees the place. Allowed until the event starts, unless the event was cancelled.</summary>
    public void Cancel(Event ev, DateTimeOffset now)
    {
        if (ev.Id != EventId)
            throw new ArgumentException("This isn't the booked event.", nameof(ev));

        if (Status != BookingStatus.Confirmed)
            throw new BusinessRuleException("This booking has already been cancelled.");
        if (ev.Status == EventStatus.Cancelled)
            throw new BusinessRuleException("This event has been cancelled, so there's nothing to cancel.");
        if (ev.StartsAt <= now)
            throw new BusinessRuleException("A booking can't be cancelled once the event has started.");

        Status = BookingStatus.Cancelled;
        CancelledAt = now;
    }
}
