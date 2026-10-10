using EventBooking.Core;
using EventBooking.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EventBooking.Tests;

/// <summary>
/// Integration tests: save and load events in a real SQL Server (see SqlServerFixture).
/// They check what the mapping tests can't, e.g. that EF can load an Event through its
/// private constructor and that rowversion really blocks a stale save.
/// The database is shared, so each test creates its own organiser and events.
/// </summary>
[Collection("SqlServer")]
public class EventDatabaseTests(SqlServerFixture database)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private async Task<string> CreateOrganiserAsync()
    {
        await using var db = database.CreateContext();
        var user = new ApplicationUser { UserName = $"{Guid.NewGuid()}@example.com" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static Event NewEvent(string organiserId) =>
        Event.Create(organiserId, "Tech Meetup", "Talks and pizza.",
            // A non-UTC offset, to check that datetimeoffset keeps it.
            new DateTimeOffset(2026, 11, 1, 19, 0, 0, TimeSpan.FromHours(1)),
            new DateTimeOffset(2026, 11, 1, 22, 0, 0, TimeSpan.FromHours(1)),
            "Europe/Lisbon", "Lisbon Congress Centre", "Praça das Indústrias, Lisbon",
            38.70, -9.20, 100, Now);

    private async Task<int> SaveAsync(Event ev)
    {
        await using var db = database.CreateContext();
        db.Events.Add(ev);
        await db.SaveChangesAsync();
        return ev.Id;
    }

    [Fact]
    public async Task Event_SavesAndLoadsWithAllValuesAndImages()
    {
        var organiserId = await CreateOrganiserAsync();
        var ev = NewEvent(organiserId);
        ev.AddImage("cover.jpg");
        ev.AddImage("second.jpg");
        var id = await SaveAsync(ev);

        await using var db = database.CreateContext();
        // Include tells EF to load the images in the same query; they aren't loaded by default.
        var loaded = await db.Events.Include(e => e.Images).SingleAsync(e => e.Id == id);

        Assert.Equal(organiserId, loaded.OrganiserId);
        Assert.Equal("Tech Meetup", loaded.Name);
        Assert.Equal(EventStatus.Draft, loaded.Status);
        Assert.Equal(ev.StartsAt, loaded.StartsAt);
        Assert.Equal(TimeSpan.FromHours(1), loaded.StartsAt.Offset);
        Assert.Equal(38.70, loaded.Latitude);
        Assert.Equal(Now, loaded.CreatedAt);
        Assert.Null(loaded.CancelledAt);
        Assert.NotEmpty(loaded.RowVersion); // filled in by SQL Server

        Assert.Equal(["cover.jpg", "second.jpg"],
            loaded.Images.OrderBy(i => i.SortOrder).Select(i => i.BlobName));
        Assert.All(loaded.Images, i => Assert.Equal(id, i.EventId));
    }

    [Fact]
    public async Task Event_ChangesAreSaved()
    {
        var id = await SaveAsync(NewEvent(await CreateOrganiserAsync()));

        await using (var db = database.CreateContext())
        {
            var ev = await db.Events.Include(e => e.Images).SingleAsync(e => e.Id == id);
            ev.AddImage("cover.jpg");
            ev.Publish(Now);
            ev.Cancel(Now.AddDays(1));
            await db.SaveChangesAsync();
        }

        await using var check = database.CreateContext();
        var loaded = await check.Events.SingleAsync(e => e.Id == id);
        Assert.Equal(EventStatus.Cancelled, loaded.Status);
        Assert.Equal(Now.AddDays(1), loaded.CancelledAt);
    }

    [Fact]
    public async Task Event_SavingAStaleCopyFails()
    {
        var id = await SaveAsync(NewEvent(await CreateOrganiserAsync()));

        // Two users load the same event at the same time.
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstCopy = await first.Events.SingleAsync(e => e.Id == id);
        var secondCopy = await second.Events.SingleAsync(e => e.Id == id);

        // The first save changes the row, so SQL Server gives it a new RowVersion...
        firstCopy.ChangeCapacity(50, confirmedBookings: 0);
        await first.SaveChangesAsync();

        // ...and the second save, still holding the old RowVersion, is rejected.
        secondCopy.ChangeCapacity(200, confirmedBookings: 0);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var check = database.CreateContext();
        Assert.Equal(50, (await check.Events.SingleAsync(e => e.Id == id)).Capacity);
    }

    [Fact]
    public async Task Event_DeletedEventsAreLeftOutOfQueries()
    {
        var ev = NewEvent(await CreateOrganiserAsync());
        ev.Delete();
        var id = await SaveAsync(ev);

        await using var db = database.CreateContext();
        Assert.False(await db.Events.AnyAsync(e => e.Id == id));
        Assert.True(await db.Events.IgnoreQueryFilters().AnyAsync(e => e.Id == id)); // still stored
    }

    [Fact]
    public async Task Event_CannotBeSavedWithAnOrganiserThatDoesNotExist()
    {
        var ev = NewEvent("no-such-user");

        // The foreign key to AspNetUsers rejects it, whatever the code does.
        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(ev));
    }

    [Fact]
    public async Task Organiser_CannotBeDeletedWhileTheyHaveEvents()
    {
        var organiserId = await CreateOrganiserAsync();
        await SaveAsync(NewEvent(organiserId));

        await using var db = database.CreateContext();
        var organiser = await db.Users.SingleAsync(u => u.Id == organiserId);
        db.Users.Remove(organiser);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
