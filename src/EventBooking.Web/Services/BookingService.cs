using EventBooking.Core;
using EventBooking.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EventBooking.Web.Services;

/// <summary>
/// What the event details page needs: places left, the user's booking, and why they can't book
/// (null if they can).
/// </summary>
public record BookingInfo(int PlacesLeft, Booking? MyBooking, string? ReasonCannotBook);

/// <summary>A booking with its event. Hidden events are shown without their details.</summary>
public record MyBooking(Booking Booking, Event Event, bool IsEventHidden);

public record MyBookings(List<MyBooking> Upcoming, List<MyBooking> Past);

public record Attendee(string? Email, DateTimeOffset BookedAt);

/// <summary>
/// Booking places at events and cancelling them. Broken rules throw <see cref="BusinessRuleException"/>.
/// Booking and cancelling also update the event row, so its RowVersion stops two requests from
/// taking the same last place.
/// </summary>
public class BookingService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Confirmed bookings for each event, by event id, in one query. Events without bookings are
    /// left out; use <c>GetValueOrDefault</c>.
    /// </summary>
    public Task<Dictionary<int, int>> GetConfirmedCountsAsync(IReadOnlyCollection<int> eventIds) =>
        db.Bookings
            .Where(b => eventIds.Contains(b.EventId) && b.Status == BookingStatus.Confirmed)
            .GroupBy(b => b.EventId)
            .Select(g => new { EventId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(c => c.EventId, c => c.Count);

    /// <summary>Booking details for an event's page. Pass a null user for visitors.</summary>
    public async Task<BookingInfo> GetBookingInfoAsync(Event ev, string? userId)
    {
        var confirmed = await CountConfirmedAsync(ev.Id);
        var myBooking = userId is null ? null : await FindConfirmedBookingAsync(ev.Id, userId);

        string? reason;
        if (await IsHiddenAsync(ev))
            reason = HiddenEventReason;
        else
            // Visitors get the reasons that apply to everyone, such as the event being full.
            reason = Booking.ReasonCannotBook(ev, userId ?? "", confirmed, myBooking is not null, Now);

        return new BookingInfo(ev.PlacesLeft(confirmed), myBooking, reason);
    }

    /// <summary>
    /// Books a place. If another request changed the event at the same time, checks again once with
    /// fresh data, so the user gets the real reason (e.g. the event is now full).
    /// </summary>
    public Task<Booking> BookAsync(int eventId, string userId) =>
        RetryOnceAsync(() => TryBookAsync(eventId, userId));

    /// <summary>Cancels the user's own booking, freeing the place.</summary>
    public Task CancelAsync(int bookingId, string userId) =>
        RetryOnceAsync(async () =>
        {
            var booking = await db.Bookings.SingleOrDefaultAsync(b => b.Id == bookingId);
            if (booking is null || !booking.IsBookedBy(userId))
                throw new BusinessRuleException("This booking doesn't exist.");

            var ev = await db.Events.SingleAsync(e => e.Id == booking.EventId);
            booking.Cancel(ev, Now);
            MarkEventChanged(ev);
            await db.SaveChangesAsync();
            return true;
        });

    /// <summary>The user's confirmed bookings: events that haven't ended, then past ones.</summary>
    public async Task<MyBookings> GetMyBookingsAsync(string userId)
    {
        var now = Now;
        var activeOrganiserIds = db.ActiveOrganiserIds(now);
        var bookings = await db.Bookings
            .Where(b => b.UserId == userId && b.Status == BookingStatus.Confirmed)
            .Join(db.Events, b => b.EventId, e => e.Id, (b, e) => new MyBooking(
                b, e, !activeOrganiserIds.Contains(e.OrganiserId)))
            .ToListAsync();

        return new MyBookings(
            bookings.Where(b => b.Event.EndsAt > now).OrderBy(b => b.Event.StartsAt).ToList(),
            bookings.Where(b => b.Event.EndsAt <= now).OrderByDescending(b => b.Event.StartsAt).ToList());
    }

    /// <summary>
    /// Who has a confirmed place, earliest booking first. For the event's organiser, or any admin.
    /// </summary>
    public async Task<List<Attendee>> GetAttendeesAsync(int eventId, string userId)
    {
        var ev = await db.Events.SingleOrDefaultAsync(e => e.Id == eventId)
            ?? throw new BusinessRuleException("This event no longer exists.");

        var canSee = await userManager.IsActiveInRoleAsync(userId, Roles.Admin, Now)
            || (ev.IsOrganisedBy(userId) && await userManager.IsActiveInRoleAsync(userId, Roles.Organiser, Now));
        if (!canSee)
            throw new BusinessRuleException("Only the event's organiser and admins can see who is attending.");

        return await db.Bookings
            .Where(b => b.EventId == eventId && b.Status == BookingStatus.Confirmed)
            .OrderBy(b => b.BookedAt)
            .Join(db.Users, b => b.UserId, u => u.Id, (b, u) => new Attendee(u.Email, b.BookedAt))
            .ToListAsync();
    }

    private async Task<Booking> TryBookAsync(int eventId, string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null || user.IsLockedOut(Now))
            throw new BusinessRuleException("Your account can't make bookings.");

        var ev = await db.Events.SingleOrDefaultAsync(e => e.Id == eventId)
            ?? throw new BusinessRuleException("This event no longer exists.");
        if (await IsHiddenAsync(ev))
            throw new BusinessRuleException(HiddenEventReason);

        var booking = Booking.Create(ev, userId, await CountConfirmedAsync(eventId),
            await FindConfirmedBookingAsync(eventId, userId) is not null, Now);
        db.Bookings.Add(booking);
        MarkEventChanged(ev);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 })
        {
            // 2601 is "duplicate key in a unique index": the same user booked in another request.
            throw new BusinessRuleException("You have already booked this event.");
        }
        return booking;
    }

    private async Task<T> RetryOnceAsync<T>(Func<Task<T>> attempt)
    {
        try
        {
            return await attempt();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Forget the stale copies, so the second attempt reads everything again.
            db.ChangeTracker.Clear();
        }

        try
        {
            return await attempt();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException("Lots of people are booking this event right now. Please try again.");
        }
    }

    private const string HiddenEventReason = "This event isn't available for booking.";

    private DateTimeOffset Now => timeProvider.GetUtcNow();

    private Task<int> CountConfirmedAsync(int eventId) =>
        db.Bookings.CountAsync(b => b.EventId == eventId && b.Status == BookingStatus.Confirmed);

    private Task<Booking?> FindConfirmedBookingAsync(int eventId, string userId) =>
        db.Bookings.SingleOrDefaultAsync(b =>
            b.EventId == eventId && b.UserId == userId && b.Status == BookingStatus.Confirmed);

    private async Task<bool> IsHiddenAsync(Event ev) =>
        !await db.ActiveOrganiserIds(Now).ContainsAsync(ev.OrganiserId);

    // The booking is a separate row, so without this the event's RowVersion wouldn't be checked.
    private void MarkEventChanged(Event ev) => db.Entry(ev).State = EntityState.Modified;
}
