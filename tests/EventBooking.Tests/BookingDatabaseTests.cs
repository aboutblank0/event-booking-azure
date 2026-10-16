using EventBooking.Core;
using EventBooking.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EventBooking.Tests;

/// <summary>
/// Saves and loads bookings in a real SQL Server. The database is shared, so each test creates its
/// own users and events.
/// </summary>
[Collection("SqlServer")]
public class BookingDatabaseTests(SqlServerFixture database)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private async Task<string> CreateUserAsync()
    {
        await using var db = database.CreateContext();
        var user = new ApplicationUser { UserName = $"{Guid.NewGuid()}@example.com" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Event> CreatePublishedEventAsync()
    {
        var ev = Event.Create(await CreateUserAsync(), "Tech Meetup", "Talks and pizza.",
            Now.AddDays(7), Now.AddDays(7).AddHours(3), "Europe/Lisbon", "Lisbon Congress Centre",
            "Praça das Indústrias, Lisbon", 38.70, -9.20, 100, Now);
        ev.AddImage("cover.jpg");
        ev.Publish(Now);

        await using var db = database.CreateContext();
        db.Events.Add(ev);
        await db.SaveChangesAsync();
        return ev;
    }

    // Skips the "already booked" check, like two requests arriving at the same moment.
    private static Booking NewBooking(Event ev, string userId) =>
        Booking.Create(ev, userId, confirmedBookings: 0, hasConfirmedBooking: false, Now);

    private async Task<int> SaveAsync(Booking booking)
    {
        await using var db = database.CreateContext();
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    [Fact]
    public async Task Booking_SavesAndLoadsWithAllValues()
    {
        var ev = await CreatePublishedEventAsync();
        var userId = await CreateUserAsync();
        var id = await SaveAsync(NewBooking(ev, userId));

        await using var db = database.CreateContext();
        var loaded = await db.Bookings.SingleAsync(b => b.Id == id);

        Assert.Equal(ev.Id, loaded.EventId);
        Assert.Equal(userId, loaded.UserId);
        Assert.Equal(BookingStatus.Confirmed, loaded.Status);
        Assert.Equal(Now, loaded.BookedAt);
        Assert.Null(loaded.CancelledAt);
    }

    [Fact]
    public async Task Booking_SecondConfirmedBookingIsRejectedByTheDatabase()
    {
        var ev = await CreatePublishedEventAsync();
        var userId = await CreateUserAsync();
        await SaveAsync(NewBooking(ev, userId));

        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(NewBooking(ev, userId)));
    }

    [Fact]
    public async Task Booking_CancelledBookingDoesNotBlockANewOne()
    {
        var ev = await CreatePublishedEventAsync();
        var userId = await CreateUserAsync();
        var cancelled = NewBooking(ev, userId);
        cancelled.Cancel(ev, Now);
        await SaveAsync(cancelled);

        // Only Confirmed rows are in the unique index, so this saves.
        var id = await SaveAsync(NewBooking(ev, userId));

        Assert.NotEqual(0, id);
    }

    [Fact]
    public async Task DeletingTheUser_DeletesTheirBookings()
    {
        var ev = await CreatePublishedEventAsync();
        var userId = await CreateUserAsync();
        var id = await SaveAsync(NewBooking(ev, userId));

        await using (var db = database.CreateContext())
        {
            db.Users.Remove(await db.Users.SingleAsync(u => u.Id == userId));
            await db.SaveChangesAsync();
        }

        await using var check = database.CreateContext();
        Assert.False(await check.Bookings.AnyAsync(b => b.Id == id));
    }
}
