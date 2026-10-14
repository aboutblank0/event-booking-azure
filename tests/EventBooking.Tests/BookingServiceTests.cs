using EventBooking.Core;
using EventBooking.Web.Data;
using EventBooking.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventBooking.Tests;

/// <summary>
/// Runs the service against a real SQL Server, each call in its own scope like a web request.
/// </summary>
[Collection("SqlServer")]
public class BookingServiceTests(SqlServerFixture database)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NextWeek = Now.AddDays(7);

    /// <summary>
    /// Runs <paramref name="action"/> with a service from a fresh scope. <paramref name="beforeSave"/>
    /// runs just before the service first saves, like another request saving first.
    /// </summary>
    private async Task<T> WithServiceAsync<T>(Func<BookingService, Task<T>> action, DateTimeOffset? now = null,
        Func<Task>? beforeSave = null)
    {
        await using var services = database.CreateIdentityServices();
        await using var scope = services.CreateAsyncScope();
        await using var db = TestDbContextFactory.Create(database.ConnectionString,
            new BeforeSaveInterceptor(beforeSave ?? (() => Task.CompletedTask)));
        var service = ActivatorUtilities.CreateInstance<BookingService>(
            scope.ServiceProvider, db, new FixedTimeProvider(now ?? Now));
        return await action(service);
    }

    private Task WithServiceAsync(Func<BookingService, Task> action, DateTimeOffset? now = null) =>
        WithServiceAsync(async s => { await action(s); return true; }, now);

    private Task<Booking> BookAsync(int eventId, string userId, Func<Task>? beforeSave = null) =>
        WithServiceAsync(s => s.BookAsync(eventId, userId), beforeSave: beforeSave);

    /// <summary>
    /// Saves a published event straight to the database. <paramref name="createdAt"/> lets a test
    /// make an event that has already ended.
    /// </summary>
    private async Task<Event> CreatePublishedEventAsync(string organiserId, int capacity = 10,
        DateTimeOffset? createdAt = null)
    {
        var created = createdAt ?? Now;
        var startsAt = created.AddDays(7);
        var ev = Event.Create(organiserId, "Lisbon Tech Meetup", "Talks and networking.", startsAt,
            startsAt.AddHours(3), "Europe/Lisbon", "Lisbon Congress Centre", "Praça das Indústrias, Lisbon",
            38.70, -9.20, capacity, created);
        ev.AddImage($"{Guid.NewGuid()}.jpg");
        ev.Publish(created);

        await using var db = database.CreateContext();
        db.Events.Add(ev);
        await db.SaveChangesAsync();
        return ev;
    }

    private async Task<Event> CreatePublishedEventAsync(int capacity = 10) =>
        await CreatePublishedEventAsync(await database.CreateUserAsync(Roles.Organiser), capacity);

    private async Task ChangeEventElsewhereAsync(int eventId, Action<Event> change)
    {
        await using var db = database.CreateContext();
        change(await db.Events.SingleAsync(e => e.Id == eventId));
        await db.SaveChangesAsync();
    }

    private async Task<int> CountConfirmedAsync(int eventId)
    {
        await using var db = database.CreateContext();
        return await db.Bookings.CountAsync(b => b.EventId == eventId && b.Status == BookingStatus.Confirmed);
    }

    private async Task ChangeUserAsync(string userId, Func<UserManager<ApplicationUser>, ApplicationUser, Task<IdentityResult>> change)
    {
        await using var services = database.CreateIdentityServices();
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByIdAsync(userId))!;
        Assert.True((await change(userManager, user)).Succeeded);
    }

    private Task LockAsync(string userId) =>
        ChangeUserAsync(userId, (m, u) => m.SetLockoutEndDateAsync(u, DateTimeOffset.MaxValue));

    private async Task<string> EmailOfAsync(string userId)
    {
        await using var db = database.CreateContext();
        return (await db.Users.SingleAsync(u => u.Id == userId)).Email!;
    }

    // ---------- Booking ----------

    [Fact]
    public async Task Book_SavesAConfirmedBooking()
    {
        var ev = await CreatePublishedEventAsync();
        var userId = await database.CreateUserAsync();

        var booking = await BookAsync(ev.Id, userId);

        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(Now, booking.BookedAt);
        Assert.Equal(1, await CountConfirmedAsync(ev.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Roles.Organiser)]
    [InlineData(Roles.Admin)]
    public async Task Book_WorksForEveryRole(string? role)
    {
        var ev = await CreatePublishedEventAsync();

        await BookAsync(ev.Id, await database.CreateUserAsync(role));

        Assert.Equal(1, await CountConfirmedAsync(ev.Id));
    }

    [Fact]
    public async Task Book_RejectsTheEventsOwnOrganiser()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreatePublishedEventAsync(organiserId);

        await Assert.ThrowsAsync<BusinessRuleException>(() => BookAsync(ev.Id, organiserId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Roles.Admin)]
    public async Task Book_RejectsAHiddenEvent_EvenForAdmins(string? role)
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreatePublishedEventAsync(organiserId);
        var userId = await database.CreateUserAsync(role);
        await LockAsync(organiserId);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => BookAsync(ev.Id, userId));
        Assert.Contains("isn't available", ex.Message);
    }

    [Fact]
    public async Task Book_RejectsALockedUser()
    {
        var ev = await CreatePublishedEventAsync();
        var userId = await database.CreateUserAsync();
        await LockAsync(userId);

        await Assert.ThrowsAsync<BusinessRuleException>(() => BookAsync(ev.Id, userId));
    }

    [Fact]
    public async Task Book_RejectsAFullEvent()
    {
        var ev = await CreatePublishedEventAsync(capacity: 1);
        await BookAsync(ev.Id, await database.CreateUserAsync());
        var userId = await database.CreateUserAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => BookAsync(ev.Id, userId));
        Assert.Contains("currently full", ex.Message);
    }

    [Fact]
    public async Task Book_RejectsASecondBookingBySameUser()
    {
        var ev = await CreatePublishedEventAsync();
        var userId = await database.CreateUserAsync();
        await BookAsync(ev.Id, userId);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => BookAsync(ev.Id, userId));
        Assert.Contains("already booked", ex.Message);
    }

    [Fact]
    public async Task Book_AgainAfterCancelling_Works()
    {
        var ev = await CreatePublishedEventAsync();
        var userId = await database.CreateUserAsync();
        var first = await BookAsync(ev.Id, userId);
        await WithServiceAsync(s => s.CancelAsync(first.Id, userId));

        await BookAsync(ev.Id, userId);

        Assert.Equal(1, await CountConfirmedAsync(ev.Id));
    }

    // ---------- Two requests at once ----------

    [Fact]
    public async Task Book_LastPlaceTakenByAnotherRequest_SaysTheEventIsFull()
    {
        var ev = await CreatePublishedEventAsync(capacity: 1);
        var firstUser = await database.CreateUserAsync();
        var secondUser = await database.CreateUserAsync();

        // Both requests see one free place; the other one saves first.
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            BookAsync(ev.Id, secondUser, beforeSave: () => BookAsync(ev.Id, firstUser)));

        Assert.Contains("currently full", ex.Message);
        Assert.Equal(1, await CountConfirmedAsync(ev.Id));
    }

    [Fact]
    public async Task Book_ClashWithAnotherBooking_RetriesWhenAPlaceIsStillFree()
    {
        var ev = await CreatePublishedEventAsync(capacity: 2);
        var firstUser = await database.CreateUserAsync();
        var secondUser = await database.CreateUserAsync();

        await BookAsync(ev.Id, secondUser, beforeSave: () => BookAsync(ev.Id, firstUser));

        Assert.Equal(2, await CountConfirmedAsync(ev.Id));
    }

    [Fact]
    public async Task Book_EventCancelledByAnotherRequest_IsRejected()
    {
        var ev = await CreatePublishedEventAsync();
        var userId = await database.CreateUserAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            BookAsync(ev.Id, userId, beforeSave: () => ChangeEventElsewhereAsync(ev.Id, e => e.Cancel(Now))));

        Assert.Contains("cancelled", ex.Message);
        Assert.Equal(0, await CountConfirmedAsync(ev.Id));
    }

    // ---------- Cancelling ----------

    [Fact]
    public async Task Cancel_FreesThePlace()
    {
        var ev = await CreatePublishedEventAsync(capacity: 1);
        var userId = await database.CreateUserAsync();
        var booking = await BookAsync(ev.Id, userId);

        await WithServiceAsync(s => s.CancelAsync(booking.Id, userId));

        Assert.Equal(0, await CountConfirmedAsync(ev.Id));
        await BookAsync(ev.Id, await database.CreateUserAsync());
    }

    [Fact]
    public async Task Cancel_RejectsSomeoneElsesBooking()
    {
        var ev = await CreatePublishedEventAsync();
        var booking = await BookAsync(ev.Id, await database.CreateUserAsync());
        var adminId = await database.CreateUserAsync(Roles.Admin);

        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.CancelAsync(booking.Id, adminId)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.CancelAsync(booking.Id, ev.OrganiserId)));
        Assert.Equal(1, await CountConfirmedAsync(ev.Id));
    }

    [Fact]
    public async Task Cancel_WorksOnAHiddenEvent()
    {
        var ev = await CreatePublishedEventAsync();
        var userId = await database.CreateUserAsync();
        var booking = await BookAsync(ev.Id, userId);
        await LockAsync(ev.OrganiserId);

        await WithServiceAsync(s => s.CancelAsync(booking.Id, userId));

        Assert.Equal(0, await CountConfirmedAsync(ev.Id));
    }

    [Fact]
    public async Task Cancel_RejectedOnceTheEventHasStarted()
    {
        var ev = await CreatePublishedEventAsync();
        var userId = await database.CreateUserAsync();
        var booking = await BookAsync(ev.Id, userId);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            WithServiceAsync(s => s.CancelAsync(booking.Id, userId), now: NextWeek));
    }

    // ---------- What people see ----------

    [Fact]
    public async Task GetBookingInfo_ShowsPlacesLeftAndTheUsersBooking()
    {
        var ev = await CreatePublishedEventAsync(capacity: 3);
        var userId = await database.CreateUserAsync();
        var booking = await BookAsync(ev.Id, userId);

        var info = await WithServiceAsync(s => s.GetBookingInfoAsync(ev, userId));

        Assert.Equal(2, info.PlacesLeft);
        Assert.Equal(booking.Id, info.MyBooking?.Id);
        Assert.Contains("already booked", info.ReasonCannotBook);
    }

    [Fact]
    public async Task GetBookingInfo_ForAVisitor_SaysWhenTheEventIsFull()
    {
        var ev = await CreatePublishedEventAsync(capacity: 1);

        var before = await WithServiceAsync(s => s.GetBookingInfoAsync(ev, null));
        await BookAsync(ev.Id, await database.CreateUserAsync());
        var after = await WithServiceAsync(s => s.GetBookingInfoAsync(ev, null));

        Assert.Null(before.ReasonCannotBook);
        Assert.Equal(0, after.PlacesLeft);
        Assert.Contains("currently full", after.ReasonCannotBook);
    }

    [Fact]
    public async Task GetConfirmedCounts_CountsOnlyConfirmedBookings()
    {
        var ev = await CreatePublishedEventAsync(capacity: 5);
        var other = await CreatePublishedEventAsync(capacity: 8);
        var userId = await database.CreateUserAsync();
        var cancelled = await BookAsync(ev.Id, userId);
        await WithServiceAsync(s => s.CancelAsync(cancelled.Id, userId));
        await BookAsync(ev.Id, await database.CreateUserAsync());

        var counts = await WithServiceAsync(s => s.GetConfirmedCountsAsync([ev.Id, other.Id]));

        Assert.Equal(1, counts[ev.Id]);
        Assert.False(counts.ContainsKey(other.Id));
    }

    [Fact]
    public async Task GetMyBookings_SplitsUpcomingAndPast_AndMarksHiddenEvents()
    {
        var userId = await database.CreateUserAsync();
        var hiddenOrganiser = await database.CreateUserAsync(Roles.Organiser);
        var upcoming = await CreatePublishedEventAsync();
        var hidden = await CreatePublishedEventAsync(hiddenOrganiser);
        var past = await CreatePublishedEventAsync(await database.CreateUserAsync(Roles.Organiser),
            createdAt: Now.AddDays(-30));
        var cancelledBooking = await CreatePublishedEventAsync();

        await BookAsync(upcoming.Id, userId);
        await BookAsync(hidden.Id, userId);
        await WithServiceAsync(s => s.BookAsync(past.Id, userId), now: Now.AddDays(-30));
        var toCancel = await BookAsync(cancelledBooking.Id, userId);
        await WithServiceAsync(s => s.CancelAsync(toCancel.Id, userId));
        await LockAsync(hiddenOrganiser);

        var mine = await WithServiceAsync(s => s.GetMyBookingsAsync(userId));

        Assert.Equal([upcoming.Id, hidden.Id], mine.Upcoming.Select(b => b.Event.Id).Order());
        Assert.True(mine.Upcoming.Single(b => b.Event.Id == hidden.Id).IsEventHidden);
        Assert.False(mine.Upcoming.Single(b => b.Event.Id == upcoming.Id).IsEventHidden);
        Assert.Equal(past.Id, Assert.Single(mine.Past).Event.Id);
    }

    [Fact]
    public async Task GetMyBookings_KeepsBookingsForCancelledEvents()
    {
        var ev = await CreatePublishedEventAsync();
        var userId = await database.CreateUserAsync();
        await BookAsync(ev.Id, userId);
        await ChangeEventElsewhereAsync(ev.Id, e => e.Cancel(Now));

        var mine = await WithServiceAsync(s => s.GetMyBookingsAsync(userId));

        Assert.Equal(EventStatus.Cancelled, Assert.Single(mine.Upcoming).Event.Status);
    }

    [Fact]
    public async Task GetAttendees_ShowsEmailsToTheOrganiser()
    {
        var ev = await CreatePublishedEventAsync();
        var first = await database.CreateUserAsync();
        var second = await database.CreateUserAsync();
        await BookAsync(ev.Id, first);
        await WithServiceAsync(s => s.BookAsync(ev.Id, second), now: Now.AddHours(1));

        var attendees = await WithServiceAsync(s => s.GetAttendeesAsync(ev.Id, ev.OrganiserId));

        Assert.Equal([await EmailOfAsync(first), await EmailOfAsync(second)], attendees.Select(a => a.Email));
        Assert.Equal(Now, attendees[0].BookedAt);
    }

    [Fact]
    public async Task GetAttendees_AdminsCanSeeHiddenEvents()
    {
        var ev = await CreatePublishedEventAsync();
        await BookAsync(ev.Id, await database.CreateUserAsync());
        await LockAsync(ev.OrganiserId);
        var adminId = await database.CreateUserAsync(Roles.Admin);

        var attendees = await WithServiceAsync(s => s.GetAttendeesAsync(ev.Id, adminId));

        Assert.Single(attendees);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Roles.Organiser)]
    public async Task GetAttendees_RejectsOtherUsers(string? role)
    {
        var ev = await CreatePublishedEventAsync();
        var userId = await database.CreateUserAsync(role);

        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.GetAttendeesAsync(ev.Id, userId)));
    }

    [Fact]
    public async Task GetAttendees_RejectsAnOrganiserWhoLostTheRole()
    {
        var ev = await CreatePublishedEventAsync();
        await ChangeUserAsync(ev.OrganiserId, (m, u) => m.RemoveFromRoleAsync(u, Roles.Organiser));

        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.GetAttendeesAsync(ev.Id, ev.OrganiserId)));
    }
}
