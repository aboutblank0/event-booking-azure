using EventBooking.Core;
using EventBooking.Web.Data;
using Microsoft.AspNetCore.Identity;

namespace EventBooking.Web.Services;

/// <summary>
/// Checks shared by the services. "Active" means the user exists and isn't locked out, read from the
/// database at that moment, so a removed role or a lock applies straight away.
/// </summary>
internal static class ActiveUsers
{
    // A null LockoutEnd means never locked; "null > now" is false.
    public static bool IsLockedOut(this ApplicationUser user, DateTimeOffset now) => user.LockoutEnd > now;

    public static async Task<bool> IsActiveInRoleAsync(
        this UserManager<ApplicationUser> userManager, string userId, string role, DateTimeOffset now)
    {
        var user = await userManager.FindByIdAsync(userId);
        return user is not null && !user.IsLockedOut(now) && await userManager.IsInRoleAsync(user, role);
    }

    /// <summary>
    /// Users who have the Organiser role and aren't locked out; their events aren't hidden. A query,
    /// not a list, so EF runs it inside the events query as a subquery.
    /// </summary>
    public static IQueryable<string> ActiveOrganiserIds(this ApplicationDbContext db, DateTimeOffset now)
    {
        var organiserRoleIds = db.Roles.Where(r => r.Name == Roles.Organiser).Select(r => r.Id);
        return db.Users
            .Where(u => u.LockoutEnd == null || u.LockoutEnd <= now)
            .Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && organiserRoleIds.Contains(ur.RoleId)))
            .Select(u => u.Id);
    }
}
