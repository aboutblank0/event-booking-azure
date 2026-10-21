using EventBooking.Core;
using EventBooking.Web.Data;
using EventBooking.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventBooking.Tests;

/// <summary>
/// Runs the service against a real SQL Server, each call in its own scope like a web request.
/// Images go to an in-memory <see cref="FakeImageStorage"/>.
/// </summary>
[Collection("SqlServer")]
public class EventServiceTests(SqlServerFixture database)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NextWeek = Now.AddDays(7);

    private readonly FakeImageStorage _storage = new();
    private readonly FakeEmailSender _emails = new();

    private static EventDetails Details(string name = "Lisbon Tech Meetup", DateTimeOffset? startsAt = null) =>
        new(name, "Talks and networking.", startsAt ?? NextWeek, (startsAt ?? NextWeek).AddHours(3),
            "Europe/Lisbon", "Lisbon Congress Centre", "Praça das Indústrias, Lisbon", 38.70, -9.20, 100);

    /// <summary>
    /// Runs <paramref name="action"/> with a service from a fresh scope. <paramref name="beforeSave"/>
    /// runs just before the service saves, like another request saving first.
    /// </summary>
    private async Task<T> WithServiceAsync<T>(Func<EventService, Task<T>> action, DateTimeOffset? now = null,
        Func<Task>? beforeSave = null)
    {
        await using var services = database.CreateIdentityServices();
        await using var scope = services.CreateAsyncScope();
        await using var db = TestDbContextFactory.Create(database.ConnectionString,
            new BeforeSaveInterceptor(beforeSave ?? (() => Task.CompletedTask)));
        var service = ActivatorUtilities.CreateInstance<EventService>(
            scope.ServiceProvider, db, _storage, _emails.CreateAttendeeEmails(), new FixedTimeProvider(now ?? Now));
        return await action(service);
    }

    private Task WithServiceAsync(Func<EventService, Task> action, Func<Task>? beforeSave = null) =>
        WithServiceAsync(async s => { await action(s); return true; }, beforeSave: beforeSave);

    /// <summary>Changes the event from a separate context, as another request would.</summary>
    private async Task ChangeEventElsewhereAsync(int eventId, Action<Event> change)
    {
        await using var db = database.CreateContext();
        var ev = await db.Events.Include(e => e.Images).SingleAsync(e => e.Id == eventId);
        change(ev);
        await db.SaveChangesAsync();
    }

    private Task<Event> CreateEventAsync(string organiserId) =>
        WithServiceAsync(s => s.CreateAsync(organiserId, Details()));

    private Task<string> AddImageAsync(int eventId, string userId) =>
        WithServiceAsync(s => s.AddImageAsync(eventId, userId, new MemoryStream([1, 2, 3])));

    private async Task<Event> CreatePublishedEventAsync(string organiserId)
    {
        var ev = await CreateEventAsync(organiserId);
        await AddImageAsync(ev.Id, organiserId);
        await WithServiceAsync(s => s.PublishAsync(ev.Id, organiserId));
        return ev;
    }

    /// <summary>Books a place for a new user, and returns their email address.</summary>
    private async Task<string> AddBookingAsync(int eventId, bool cancelled = false)
    {
        var userId = await database.CreateUserAsync();
        await using var db = database.CreateContext();
        var ev = await db.Events.SingleAsync(e => e.Id == eventId);
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        var confirmed = await db.Bookings.CountAsync(b => b.EventId == eventId && b.Status == BookingStatus.Confirmed);
        var booking = Booking.Create(ev, user.Id, confirmed, false, Now);
        if (cancelled)
            booking.Cancel(ev, Now);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return user.Email!;
    }

    private async Task<Event> LoadAsync(int id)
    {
        await using var db = database.CreateContext();
        return await db.Events.IgnoreQueryFilters().Include(e => e.Images).SingleAsync(e => e.Id == id);
    }

    private async Task ChangeUserAsync(string userId, Func<UserManager<ApplicationUser>, ApplicationUser, Task<IdentityResult>> change)
    {
        await using var services = database.CreateIdentityServices();
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByIdAsync(userId))!;
        Assert.True((await change(userManager, user)).Succeeded);
    }

    // ---------- Who can create and change events ----------

    [Fact]
    public async Task Create_SavesADraftOrganisedByTheUser()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);

        var ev = await CreateEventAsync(organiserId);

        var loaded = await LoadAsync(ev.Id);
        Assert.Equal(EventStatus.Draft, loaded.Status);
        Assert.Equal(organiserId, loaded.OrganiserId);
        Assert.Equal(Now, loaded.CreatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Roles.Admin)]
    public async Task Create_RejectsUsersWithoutTheOrganiserRole(string? role)
    {
        var userId = await database.CreateUserAsync(role);

        await Assert.ThrowsAsync<BusinessRuleException>(() => CreateEventAsync(userId));
    }

    [Fact]
    public async Task Create_RejectsALockedOutOrganiser()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        await ChangeUserAsync(organiserId, (m, u) => m.SetLockoutEndDateAsync(u, DateTimeOffset.MaxValue));

        await Assert.ThrowsAsync<BusinessRuleException>(() => CreateEventAsync(organiserId));
    }

    [Fact]
    public async Task Changes_RejectAnotherOrganisersEvent()
    {
        var ev = await CreateEventAsync(await database.CreateUserAsync(Roles.Organiser));
        var otherId = await database.CreateUserAsync(Roles.Organiser);

        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.UpdateAsync(ev.Id, otherId, Details("Mine now"))));
        await Assert.ThrowsAsync<BusinessRuleException>(() => AddImageAsync(ev.Id, otherId));
        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.DeleteAsync(ev.Id, otherId)));
        Assert.Equal(0, _storage.UploadCount);
    }

    [Fact]
    public async Task Changes_RejectAnOrganiserWhoseRoleWasRemoved()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreateEventAsync(organiserId);
        await ChangeUserAsync(organiserId, (m, u) => m.RemoveFromRoleAsync(u, Roles.Organiser));

        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.UpdateAsync(ev.Id, organiserId, Details("Still mine"))));
    }

    [Fact]
    public async Task Changes_RejectAMissingEvent()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);

        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.PublishAsync(int.MaxValue, organiserId)));
    }

    // ---------- Editing ----------

    [Fact]
    public async Task Update_SavesTheNewValues()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreateEventAsync(organiserId);

        await WithServiceAsync(s => s.UpdateAsync(ev.Id, organiserId, Details("Porto Tech Meetup", NextWeek.AddDays(1))));

        var loaded = await LoadAsync(ev.Id);
        Assert.Equal("Porto Tech Meetup", loaded.Name);
        Assert.Equal(NextWeek.AddDays(1), loaded.StartsAt);
    }

    [Fact]
    public async Task Update_PublishedEvent_AllowsOtherChangesButNotNewDates()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreatePublishedEventAsync(organiserId);

        await WithServiceAsync(s => s.UpdateAsync(ev.Id, organiserId, Details("Renamed")));
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => WithServiceAsync(s => s.UpdateAsync(ev.Id, organiserId, Details("Renamed", NextWeek.AddDays(1)))));

        Assert.Equal("Renamed", (await LoadAsync(ev.Id)).Name);
    }

    [Fact]
    public async Task Update_CapacityCannotGoBelowConfirmedBookings()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreatePublishedEventAsync(organiserId);
        await using (var db = database.CreateContext())
        {
            var loaded = await db.Events.SingleAsync(e => e.Id == ev.Id);
            db.Bookings.Add(Booking.Create(loaded, await database.CreateUserAsync(), 0, false, Now));
            db.Bookings.Add(Booking.Create(loaded, await database.CreateUserAsync(), 1, false, Now));
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => WithServiceAsync(s => s.UpdateAsync(ev.Id, organiserId, Details() with { Capacity = 1 })));
        await WithServiceAsync(s => s.UpdateAsync(ev.Id, organiserId, Details() with { Capacity = 2 }));

        Assert.Equal(2, (await LoadAsync(ev.Id)).Capacity);
    }

    [Fact]
    public async Task Update_NewVenue_EmailsConfirmedAttendees()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreatePublishedEventAsync(organiserId);
        var attendeeEmail = await AddBookingAsync(ev.Id);
        await AddBookingAsync(ev.Id, cancelled: true);

        await WithServiceAsync(s => s.UpdateAsync(ev.Id, organiserId, Details() with { VenueName = "Porto Hall" }));

        var sent = Assert.Single(_emails.Sent);
        Assert.Equal(attendeeEmail, sent.To);
        Assert.Contains("Porto Hall", sent.HtmlMessage);
    }

    [Fact]
    public async Task Update_SameVenue_SendsNoEmail()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreatePublishedEventAsync(organiserId);
        await AddBookingAsync(ev.Id);

        await WithServiceAsync(s => s.UpdateAsync(ev.Id, organiserId, Details("Renamed")));

        Assert.Empty(_emails.Sent);
    }

    // ---------- Images ----------

    [Fact]
    public async Task AddImage_StoresTheFileAndAttachesIt()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreateEventAsync(organiserId);

        var blobName = await AddImageAsync(ev.Id, organiserId);

        Assert.Contains(blobName, _storage.Blobs);
        Assert.Equal(blobName, Assert.Single((await LoadAsync(ev.Id)).Images).BlobName);
    }

    [Fact]
    public async Task AddImage_FullEvent_FailsWithoutUploading()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreateEventAsync(organiserId);
        for (var i = 0; i < Event.MaxImages; i++)
            await AddImageAsync(ev.Id, organiserId);

        await Assert.ThrowsAsync<BusinessRuleException>(() => AddImageAsync(ev.Id, organiserId));

        Assert.Equal(Event.MaxImages, _storage.UploadCount);
    }

    [Fact]
    public async Task AddImage_RejectedFile_ExplainsWhy()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreateEventAsync(organiserId);
        _storage.FailWith = ImageUploadError.NotAnImage;

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => AddImageAsync(ev.Id, organiserId));

        Assert.Contains("JPEG", ex.Message);
        Assert.Empty((await LoadAsync(ev.Id)).Images);
    }

    [Fact]
    public async Task RemoveImage_DeletesTheRowAndTheFile()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreateEventAsync(organiserId);
        var blobName = await AddImageAsync(ev.Id, organiserId);

        await WithServiceAsync(s => s.RemoveImageAsync(ev.Id, organiserId, blobName));

        Assert.Empty((await LoadAsync(ev.Id)).Images);
        Assert.Empty(_storage.Blobs);
    }

    // ---------- Publishing ----------

    [Fact]
    public async Task Publish_MakesTheEventPublished()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreateEventAsync(organiserId);
        await AddImageAsync(ev.Id, organiserId);

        await WithServiceAsync(s => s.PublishAsync(ev.Id, organiserId));

        Assert.Equal(EventStatus.Published, (await LoadAsync(ev.Id)).Status);
    }

    // ---------- Cancelling ----------

    [Fact]
    public async Task Cancel_ByTheOrganiser_CancelsTheEvent()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreatePublishedEventAsync(organiserId);

        await WithServiceAsync(s => s.CancelAsync(ev.Id, organiserId));

        var loaded = await LoadAsync(ev.Id);
        Assert.Equal(EventStatus.Cancelled, loaded.Status);
        Assert.Equal(Now, loaded.CancelledAt);
    }

    [Fact]
    public async Task Cancel_EmailsEveryConfirmedAttendee()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreatePublishedEventAsync(organiserId);
        var first = await AddBookingAsync(ev.Id);
        var second = await AddBookingAsync(ev.Id);
        await AddBookingAsync(ev.Id, cancelled: true);

        await WithServiceAsync(s => s.CancelAsync(ev.Id, organiserId));

        Assert.Equal(new[] { first, second }.Order(), _emails.Sent.Select(e => e.To).Order());
        Assert.All(_emails.Sent, e => Assert.StartsWith("Event cancelled", e.Subject));
    }

    [Fact]
    public async Task Cancel_StillCancelsWhenEmailsFail()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreatePublishedEventAsync(organiserId);
        await AddBookingAsync(ev.Id);
        _emails.Fail = true;

        await WithServiceAsync(s => s.CancelAsync(ev.Id, organiserId));

        Assert.Equal(EventStatus.Cancelled, (await LoadAsync(ev.Id)).Status);
    }

    [Fact]
    public async Task Cancel_ByAnAdmin_CancelsAnyEvent()
    {
        var ev = await CreatePublishedEventAsync(await database.CreateUserAsync(Roles.Organiser));
        var adminId = await database.CreateUserAsync(Roles.Admin);

        await WithServiceAsync(s => s.CancelAsync(ev.Id, adminId));

        Assert.Equal(EventStatus.Cancelled, (await LoadAsync(ev.Id)).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Roles.Organiser)]
    public async Task Cancel_RejectsOtherUsersAndOrganisers(string? role)
    {
        var ev = await CreatePublishedEventAsync(await database.CreateUserAsync(Roles.Organiser));
        var userId = await database.CreateUserAsync(role);

        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.CancelAsync(ev.Id, userId)));
    }

    [Fact]
    public async Task Cancel_RejectsALockedOutAdmin()
    {
        var ev = await CreatePublishedEventAsync(await database.CreateUserAsync(Roles.Organiser));
        var adminId = await database.CreateUserAsync(Roles.Admin);
        await ChangeUserAsync(adminId, (m, u) => m.SetLockoutEndDateAsync(u, DateTimeOffset.MaxValue));

        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.CancelAsync(ev.Id, adminId)));
    }

    // ---------- Deleting ----------

    [Fact]
    public async Task Delete_RemovesTheImagesAndHidesTheDraft()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreateEventAsync(organiserId);
        await AddImageAsync(ev.Id, organiserId);
        await AddImageAsync(ev.Id, organiserId);

        await WithServiceAsync(s => s.DeleteAsync(ev.Id, organiserId));

        var loaded = await LoadAsync(ev.Id);
        Assert.Equal(EventStatus.Deleted, loaded.Status);
        Assert.Empty(loaded.Images);
        Assert.Empty(_storage.Blobs);
        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.UpdateAsync(ev.Id, organiserId, Details())));
    }

    // ---------- Who sees which events ----------

    private Task LockOutAsync(string userId) =>
        ChangeUserAsync(userId, (m, u) => m.SetLockoutEndDateAsync(u, DateTimeOffset.MaxValue));

    private Task RemoveOrganiserRoleAsync(string userId) =>
        ChangeUserAsync(userId, (m, u) => m.RemoveFromRoleAsync(u, Roles.Organiser));

    private async Task<List<int>> UpcomingIdsAsync(DateTimeOffset? now = null) =>
        (await WithServiceAsync(s => s.GetUpcomingEventsAsync(), now)).Select(e => e.Id).ToList();

    [Fact]
    public async Task Upcoming_ShowsOnlyPublishedEventsThatHaventEnded()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var published = await CreatePublishedEventAsync(organiserId);
        var draft = await CreateEventAsync(organiserId);
        var cancelled = await CreatePublishedEventAsync(organiserId);
        await WithServiceAsync(s => s.CancelAsync(cancelled.Id, organiserId));

        var ids = await UpcomingIdsAsync();

        Assert.Contains(published.Id, ids);
        Assert.DoesNotContain(draft.Id, ids);
        Assert.DoesNotContain(cancelled.Id, ids);
        Assert.DoesNotContain(published.Id, await UpcomingIdsAsync(published.EndsAt));
    }

    [Fact]
    public async Task Upcoming_HidesEventsOfLockedOrRemovedOrganisers()
    {
        var lockedId = await database.CreateUserAsync(Roles.Organiser);
        var removedId = await database.CreateUserAsync(Roles.Organiser);
        var ofLocked = await CreatePublishedEventAsync(lockedId);
        var ofRemoved = await CreatePublishedEventAsync(removedId);

        await LockOutAsync(lockedId);
        await RemoveOrganiserRoleAsync(removedId);

        var ids = await UpcomingIdsAsync();
        Assert.DoesNotContain(ofLocked.Id, ids);
        Assert.DoesNotContain(ofRemoved.Id, ids);
    }

    [Fact]
    public async Task Upcoming_ShowsTheEventsAgainOnceTheRoleIsBack()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreatePublishedEventAsync(organiserId);
        await RemoveOrganiserRoleAsync(organiserId);

        await ChangeUserAsync(organiserId, (m, u) => m.AddToRoleAsync(u, Roles.Organiser));

        Assert.Contains(ev.Id, await UpcomingIdsAsync());
    }

    [Fact]
    public async Task GetEvent_DraftsAreSeenOnlyByTheirOrganiser()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var draft = await CreateEventAsync(organiserId);
        var otherOrganiserId = await database.CreateUserAsync(Roles.Organiser);
        var adminId = await database.CreateUserAsync(Roles.Admin);

        Assert.NotNull(await WithServiceAsync(s => s.GetEventAsync(draft.Id, organiserId)));
        Assert.Null(await WithServiceAsync(s => s.GetEventAsync(draft.Id, null)));
        Assert.Null(await WithServiceAsync(s => s.GetEventAsync(draft.Id, otherOrganiserId)));
        Assert.Null(await WithServiceAsync(s => s.GetEventAsync(draft.Id, adminId)));
    }

    [Fact]
    public async Task GetEvent_PublishedIsSeenByVisitors()
    {
        var ev = await CreatePublishedEventAsync(await database.CreateUserAsync(Roles.Organiser));

        var loaded = await WithServiceAsync(s => s.GetEventAsync(ev.Id, null));

        Assert.NotNull(loaded);
        Assert.Single(loaded.Images);
    }

    [Fact]
    public async Task GetEvent_HiddenIsSeenOnlyByAdmins()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreatePublishedEventAsync(organiserId);
        var adminId = await database.CreateUserAsync(Roles.Admin);
        await LockOutAsync(organiserId);

        Assert.Null(await WithServiceAsync(s => s.GetEventAsync(ev.Id, null)));
        Assert.Null(await WithServiceAsync(s => s.GetEventAsync(ev.Id, organiserId)));
        Assert.NotNull(await WithServiceAsync(s => s.GetEventAsync(ev.Id, adminId)));
    }

    [Fact]
    public async Task GetEvent_CancelledIsSeenByItsOrganiserAndAdmins()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreatePublishedEventAsync(organiserId);
        await WithServiceAsync(s => s.CancelAsync(ev.Id, organiserId));
        var adminId = await database.CreateUserAsync(Roles.Admin);

        Assert.NotNull(await WithServiceAsync(s => s.GetEventAsync(ev.Id, organiserId)));
        Assert.NotNull(await WithServiceAsync(s => s.GetEventAsync(ev.Id, adminId)));
        Assert.Null(await WithServiceAsync(s => s.GetEventAsync(ev.Id, null)));
    }

    [Fact]
    public async Task GetMyEvents_ReturnsOwnEventsExceptDeleted()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var draft = await CreateEventAsync(organiserId);
        var published = await CreatePublishedEventAsync(organiserId);
        var deleted = await CreateEventAsync(organiserId);
        await WithServiceAsync(s => s.DeleteAsync(deleted.Id, organiserId));
        await CreateEventAsync(await database.CreateUserAsync(Roles.Organiser));

        var mine = await WithServiceAsync(s => s.GetMyEventsAsync(organiserId));

        Assert.Equal([draft.Id, published.Id], mine.Select(e => e.Id).Order());
    }

    [Fact]
    public async Task GetEventsForAdmin_IncludesHiddenEventsButNotDrafts()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var visible = await CreatePublishedEventAsync(organiserId);
        var draft = await CreateEventAsync(organiserId);
        var lockedId = await database.CreateUserAsync(Roles.Organiser);
        var hidden = await CreatePublishedEventAsync(lockedId);
        await LockOutAsync(lockedId);
        var adminId = await database.CreateUserAsync(Roles.Admin);

        var events = await WithServiceAsync(s => s.GetEventsForAdminAsync(adminId));

        Assert.False(Assert.Single(events, e => e.Event.Id == visible.Id).IsHidden);
        var hiddenRow = Assert.Single(events, e => e.Event.Id == hidden.Id);
        Assert.True(hiddenRow.IsHidden);
        Assert.EndsWith("@example.com", hiddenRow.OrganiserEmail);
        Assert.DoesNotContain(events, e => e.Event.Id == draft.Id);
    }

    [Fact]
    public async Task GetEventsForAdmin_RejectsNonAdmins()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);

        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.GetEventsForAdminAsync(organiserId)));
    }

    // ---------- Two requests changing the same event ----------

    [Fact]
    public async Task Update_AfterAnotherSave_AsksToReloadAndKeepsTheirChange()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreateEventAsync(organiserId);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(
            s => s.UpdateAsync(ev.Id, organiserId, Details("Mine")),
            beforeSave: () => ChangeEventElsewhereAsync(ev.Id, e => e.UpdateDetails("Theirs", "Changed elsewhere."))));

        Assert.Contains("Reload", ex.Message);
        Assert.Equal("Theirs", (await LoadAsync(ev.Id)).Name);
    }

    [Fact]
    public async Task AddImage_AfterAnotherSave_DeletesTheUploadedFile()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreateEventAsync(organiserId);

        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(
            s => s.AddImageAsync(ev.Id, organiserId, new MemoryStream([1, 2, 3])),
            beforeSave: () => ChangeEventElsewhereAsync(ev.Id, e => e.UpdateDetails("Theirs", "Changed elsewhere."))));

        Assert.Empty(_storage.Blobs);
        Assert.Empty((await LoadAsync(ev.Id)).Images);
    }

    [Fact]
    public async Task AddImage_TwoAtOnce_TheSecondAsksToReload()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreateEventAsync(organiserId);

        // The other upload saves first with the same SortOrder, so the unique index rejects this one.
        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(
            s => s.AddImageAsync(ev.Id, organiserId, new MemoryStream([1, 2, 3])),
            beforeSave: () => ChangeEventElsewhereAsync(ev.Id, e => e.AddImage("other.jpg"))));

        Assert.Empty(_storage.Blobs);
        Assert.Equal("other.jpg", Assert.Single((await LoadAsync(ev.Id)).Images).BlobName);
    }

    [Fact]
    public async Task RemoveImage_WhileSomeonePublishes_KeepsTheImage()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreateEventAsync(organiserId);
        var blobName = await AddImageAsync(ev.Id, organiserId);

        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(
            s => s.RemoveImageAsync(ev.Id, organiserId, blobName),
            beforeSave: () => ChangeEventElsewhereAsync(ev.Id, e => e.Publish(Now))));

        var loaded = await LoadAsync(ev.Id);
        Assert.Equal(EventStatus.Published, loaded.Status);
        Assert.Single(loaded.Images);
        Assert.Contains(blobName, _storage.Blobs);
    }
}
