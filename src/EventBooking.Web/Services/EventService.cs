using EventBooking.Core;
using EventBooking.Web.Data;
using EventBooking.Web.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EventBooking.Web.Services;

/// <summary>What an organiser fills in for an event. Images are added one at a time instead.</summary>
public record EventDetails(
    string Name,
    string Description,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string TimeZoneId,
    string VenueName,
    string Address,
    double Latitude,
    double Longitude,
    int Capacity);

/// <summary>An event with its organiser's email, for the admin pages.</summary>
public record EventForAdmin(Event Event, string? OrganiserEmail, bool IsHidden);

/// <summary>
/// Creating and managing events. Every change checks, against the database at that moment, that the
/// user is an organiser, isn't locked out, and organises the event. Broken rules throw
/// <see cref="BusinessRuleException"/>.
/// An event is hidden while its organiser lacks the Organiser role or is locked out; only admins see
/// hidden events, and nobody sees other organisers' drafts.
/// </summary>
public class EventService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IImageStorage imageStorage,
    TimeProvider timeProvider)
{
    /// <summary>Published events that haven't ended, soonest first, for anyone to browse.</summary>
    public Task<List<Event>> GetUpcomingEventsAsync()
    {
        var now = timeProvider.GetUtcNow();
        var activeOrganiserIds = ActiveOrganiserIds();
        return db.Events
            .Include(e => e.Images)
            .Where(e => e.Status == EventStatus.Published && e.EndsAt > now)
            .Where(e => activeOrganiserIds.Contains(e.OrganiserId))
            .OrderBy(e => e.StartsAt)
            .ToListAsync();
    }

    /// <summary>
    /// One event, or null if it doesn't exist or the user can't see it. Pass a null user for visitors.
    /// </summary>
    public async Task<Event?> GetEventAsync(int eventId, string? userId)
    {
        var ev = await db.Events.Include(e => e.Images).SingleOrDefaultAsync(e => e.Id == eventId);
        if (ev is null)
            return null;

        if (userId is not null && await IsActiveInRoleAsync(userId, Roles.Admin))
            return ev.Status == EventStatus.Draft ? null : ev;

        if (!await ActiveOrganiserIds().ContainsAsync(ev.OrganiserId))
            return null;
        var isOwn = userId is not null && ev.IsOrganisedBy(userId);
        return ev.Status == EventStatus.Published || isOwn ? ev : null;
    }

    /// <summary>The organiser's own events of every status, soonest first.</summary>
    public async Task<List<Event>> GetMyEventsAsync(string userId)
    {
        await EnsureActiveOrganiserAsync(userId);
        return await db.Events
            .Include(e => e.Images)
            .Where(e => e.OrganiserId == userId)
            .OrderBy(e => e.StartsAt)
            .ToListAsync();
    }

    /// <summary>Every published or cancelled event, hidden ones included, soonest first.</summary>
    public async Task<List<EventForAdmin>> GetEventsForAdminAsync(string adminId)
    {
        if (!await IsActiveInRoleAsync(adminId, Roles.Admin))
            throw new BusinessRuleException("Only admins can see all events.");

        // Core can't reference ApplicationUser, so the email is joined in by OrganiserId (one SQL query).
        var activeOrganiserIds = ActiveOrganiserIds();
        return await db.Events
            .Where(e => e.Status != EventStatus.Draft)
            .OrderBy(e => e.StartsAt)
            .Join(db.Users, e => e.OrganiserId, u => u.Id, (e, u) => new EventForAdmin(
                e, u.Email, !activeOrganiserIds.Contains(e.OrganiserId)))
            .ToListAsync();
    }

    /// <summary>Creates a Draft event organised by the user.</summary>
    public async Task<Event> CreateAsync(string userId, EventDetails details)
    {
        await EnsureActiveOrganiserAsync(userId);

        var ev = Event.Create(userId, details.Name, details.Description, details.StartsAt, details.EndsAt,
            details.TimeZoneId, details.VenueName, details.Address, details.Latitude, details.Longitude,
            details.Capacity, timeProvider.GetUtcNow());

        db.Events.Add(ev);
        await db.SaveChangesAsync();
        return ev;
    }

    /// <summary>Saves the form's values. Dates count as changed only if they differ from the saved ones.</summary>
    public async Task UpdateAsync(int eventId, string userId, EventDetails details)
    {
        var ev = await FindOwnEventAsync(eventId, userId);

        ev.UpdateDetails(details.Name, details.Description);
        // A published event's dates can't change, but its edit form still sends them back unchanged.
        if (ev.StartsAt != details.StartsAt || ev.EndsAt != details.EndsAt || ev.TimeZoneId != details.TimeZoneId)
            ev.ChangeSchedule(details.StartsAt, details.EndsAt, details.TimeZoneId, timeProvider.GetUtcNow());
        ev.ChangeVenue(details.VenueName, details.Address, details.Latitude, details.Longitude);
        // TODO: pass the real count of confirmed bookings once bookings exist.
        ev.ChangeCapacity(details.Capacity, confirmedBookings: 0);

        await db.SaveChangesAsync();
    }

    /// <summary>Uploads an image into the event and returns its new blob name.</summary>
    public async Task<string> AddImageAsync(int eventId, string userId, Stream content,
        CancellationToken cancellationToken = default)
    {
        var ev = await FindOwnEventAsync(eventId, userId);
        // Checked before uploading, so a full or cancelled event doesn't cost an upload.
        ev.EnsureCanAddImage();

        var result = await imageStorage.UploadAsync(content, cancellationToken);
        if (result.Error != ImageUploadError.None)
            throw new BusinessRuleException(UploadErrorMessage(result.Error));
        var blobName = result.BlobName!;

        try
        {
            ev.AddImage(blobName);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Nothing refers to the file, so remove it from storage.
            await imageStorage.DeleteAsync(blobName, CancellationToken.None);
            throw;
        }

        return blobName;
    }

    /// <summary>Removes the image from the event and deletes its file.</summary>
    public async Task RemoveImageAsync(int eventId, string userId, string blobName,
        CancellationToken cancellationToken = default)
    {
        var ev = await FindOwnEventAsync(eventId, userId);
        ev.RemoveImage(blobName);
        await db.SaveChangesAsync(cancellationToken);

        // Deleted only after the save, so a saved event never refers to a missing file.
        await imageStorage.DeleteAsync(blobName, cancellationToken);
    }

    public async Task PublishAsync(int eventId, string userId)
    {
        var ev = await FindOwnEventAsync(eventId, userId);
        ev.Publish(timeProvider.GetUtcNow());
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Deletes a draft and its images. The event stays in the database, but no query returns it again.
    /// </summary>
    public async Task DeleteAsync(int eventId, string userId, CancellationToken cancellationToken = default)
    {
        var ev = await FindOwnEventAsync(eventId, userId);
        var blobNames = ev.Images.Select(i => i.BlobName).ToList();
        ev.Delete();
        await db.SaveChangesAsync(cancellationToken);

        foreach (var blobName in blobNames)
            await imageStorage.DeleteAsync(blobName, cancellationToken);
    }

    /// <summary>Organisers can cancel their own published events; admins can cancel any.</summary>
    public async Task CancelAsync(int eventId, string userId)
    {
        var ev = await IsActiveInRoleAsync(userId, Roles.Admin)
            ? await FindEventAsync(eventId)
            : await FindOwnEventAsync(eventId, userId);
        ev.Cancel(timeProvider.GetUtcNow());
        await db.SaveChangesAsync();
    }

    private async Task EnsureActiveOrganiserAsync(string userId)
    {
        if (!await IsActiveInRoleAsync(userId, Roles.Organiser))
            throw new BusinessRuleException("Only organisers can create and manage events.");
    }

    private async Task<bool> IsActiveInRoleAsync(string userId, string role)
    {
        var user = await userManager.FindByIdAsync(userId);
        return user is not null && !IsLockedOut(user) && await userManager.IsInRoleAsync(user, role);
    }

    /// <summary>
    /// Users who have the Organiser role and aren't locked out. A query, not a list, so EF runs it
    /// inside the events query as a subquery.
    /// </summary>
    private IQueryable<string> ActiveOrganiserIds()
    {
        var now = timeProvider.GetUtcNow();
        var organiserRoleIds = db.Roles.Where(r => r.Name == Roles.Organiser).Select(r => r.Id);
        return db.Users
            .Where(u => u.LockoutEnd == null || u.LockoutEnd <= now)
            .Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && organiserRoleIds.Contains(ur.RoleId)))
            .Select(u => u.Id);
    }

    // A null LockoutEnd means never locked; "null > now" is false.
    private bool IsLockedOut(ApplicationUser user) => user.LockoutEnd > timeProvider.GetUtcNow();

    /// <summary>Loads the event with its images, after checking the user may change it.</summary>
    private async Task<Event> FindOwnEventAsync(int eventId, string userId)
    {
        await EnsureActiveOrganiserAsync(userId);

        var ev = await FindEventAsync(eventId);
        if (!ev.IsOrganisedBy(userId))
            throw new BusinessRuleException("You can only change events you organise.");
        return ev;
    }

    private async Task<Event> FindEventAsync(int eventId) =>
        await db.Events.Include(e => e.Images).SingleOrDefaultAsync(e => e.Id == eventId)
        ?? throw new BusinessRuleException("This event no longer exists.");

    private static string UploadErrorMessage(ImageUploadError error) => error switch
    {
        ImageUploadError.Empty => "The file is empty.",
        ImageUploadError.TooLarge => "Images must be 2 MB or smaller.",
        ImageUploadError.NotAnImage => "Only JPEG, PNG and WebP images are allowed.",
        ImageUploadError.StorageFull => "The image storage limit has been reached.",
        _ => "The image couldn't be uploaded.",
    };
}
