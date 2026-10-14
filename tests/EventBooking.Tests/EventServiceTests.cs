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

    private static EventDetails Details(string name = "Lisbon Tech Meetup", DateTimeOffset? startsAt = null) =>
        new(name, "Talks and networking.", startsAt ?? NextWeek, (startsAt ?? NextWeek).AddHours(3),
            "Europe/Lisbon", "Lisbon Congress Centre", "Praça das Indústrias, Lisbon", 38.70, -9.20, 100);

    private async Task<T> WithServiceAsync<T>(Func<EventService, Task<T>> action)
    {
        await using var services = database.CreateIdentityServices();
        await using var scope = services.CreateAsyncScope();
        var service = ActivatorUtilities.CreateInstance<EventService>(
            scope.ServiceProvider, _storage, new FixedTimeProvider(Now));
        return await action(service);
    }

    private Task WithServiceAsync(Func<EventService, Task> action) =>
        WithServiceAsync(async s => { await action(s); return true; });

    private Task<Event> CreateEventAsync(string organiserId) =>
        WithServiceAsync(s => s.CreateAsync(organiserId, Details()));

    private Task<string> AddImageAsync(int eventId, string userId) =>
        WithServiceAsync(s => s.AddImageAsync(eventId, userId, new MemoryStream([1, 2, 3])));

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
        var ev = await CreateEventAsync(organiserId);
        await AddImageAsync(ev.Id, organiserId);
        await WithServiceAsync(s => s.PublishAsync(ev.Id, organiserId));

        await WithServiceAsync(s => s.UpdateAsync(ev.Id, organiserId, Details("Renamed")));
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => WithServiceAsync(s => s.UpdateAsync(ev.Id, organiserId, Details("Renamed", NextWeek.AddDays(1)))));

        Assert.Equal("Renamed", (await LoadAsync(ev.Id)).Name);
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
    public async Task AddImage_SaveFails_DeletesTheUploadedFile()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreateEventAsync(organiserId);
        // Another upload saves first and takes the same SortOrder, so this save fails.
        _storage.AfterUpload = async () =>
        {
            await using var db = database.CreateContext();
            var other = await db.Events.Include(e => e.Images).SingleAsync(e => e.Id == ev.Id);
            other.AddImage("other.jpg");
            await db.SaveChangesAsync();
        };

        await Assert.ThrowsAsync<DbUpdateException>(() => AddImageAsync(ev.Id, organiserId));

        Assert.Empty(_storage.Blobs);
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

    // ---------- Publishing and deleting ----------

    [Fact]
    public async Task Publish_MakesTheEventPublished()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreateEventAsync(organiserId);
        await AddImageAsync(ev.Id, organiserId);

        await WithServiceAsync(s => s.PublishAsync(ev.Id, organiserId));

        Assert.Equal(EventStatus.Published, (await LoadAsync(ev.Id)).Status);
    }

    [Fact]
    public async Task Delete_HidesTheDraftFromLaterChanges()
    {
        var organiserId = await database.CreateUserAsync(Roles.Organiser);
        var ev = await CreateEventAsync(organiserId);

        await WithServiceAsync(s => s.DeleteAsync(ev.Id, organiserId));

        Assert.Equal(EventStatus.Deleted, (await LoadAsync(ev.Id)).Status);
        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.UpdateAsync(ev.Id, organiserId, Details())));
    }
}
