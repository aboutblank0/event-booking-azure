using EventBooking.Core;
using EventBooking.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EventBooking.Web.Services;

/// <summary>A user as the admin pages show them.</summary>
public record UserForAdmin(
    string Id,
    string? Email,
    bool IsOrganiser,
    bool IsAdmin,
    bool IsLocked,
    bool IsConfigAdmin);

/// <summary>
/// Admins managing users: removing the Organiser role, giving or removing the Admin role, and
/// locking accounts. Every action checks, against the database at that moment, that the caller is
/// an admin who isn't locked out. Broken rules throw <see cref="BusinessRuleException"/>.
/// </summary>
/// <remarks>
/// Removing a role or locking an account updates the user's security stamp, which signs them out on
/// their next request. Giving a role must not, so the new role simply appears on their next page load.
/// </remarks>
public class UserAdminService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IConfiguration configuration,
    TimeProvider timeProvider)
{
    /// <summary>The most users a list shows; searching narrows it down.</summary>
    public const int MaxListedUsers = 100;

    /// <summary>Users whose email contains <paramref name="search"/> (all users if blank), by email.</summary>
    public async Task<List<UserForAdmin>> GetUsersAsync(string adminId, string? search)
    {
        await EnsureActiveAdminAsync(adminId);

        var users = db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            users = users.Where(u => u.Email!.Contains(search.Trim()));

        return await ForAdmin(users.OrderBy(u => u.Email).Take(MaxListedUsers)).ToListAsync();
    }

    /// <summary>One user, or null if they don't exist.</summary>
    public async Task<UserForAdmin?> GetUserAsync(string adminId, string userId)
    {
        await EnsureActiveAdminAsync(adminId);
        return await ForAdmin(db.Users.Where(u => u.Id == userId)).SingleOrDefaultAsync();
    }

    /// <summary>Makes an organiser a normal user again. Their events are hidden until the role is given back.</summary>
    public async Task RemoveOrganiserAsync(string adminId, string userId)
    {
        await EnsureActiveAdminAsync(adminId);
        var user = await FindUserAsync(userId);
        if (!await userManager.IsInRoleAsync(user, Roles.Organiser))
            throw new BusinessRuleException("This user isn't an organiser.");

        EnsureSucceeded(await userManager.RemoveFromRoleAsync(user, Roles.Organiser));
        EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user));
    }

    public async Task GiveAdminAsync(string adminId, string userId)
    {
        await EnsureActiveAdminAsync(adminId);
        var user = await FindUserAsync(userId);
        if (await userManager.IsInRoleAsync(user, Roles.Admin))
            throw new BusinessRuleException("This user is already an admin.");
        // A user can't be both an organiser and an admin.
        if (await userManager.IsInRoleAsync(user, Roles.Organiser))
            throw new BusinessRuleException("Organisers can't be made admins. Remove their Organiser role first.");

        EnsureSucceeded(await userManager.AddToRoleAsync(user, Roles.Admin));
    }

    public async Task RemoveAdminAsync(string adminId, string userId)
    {
        await EnsureActiveAdminAsync(adminId);
        var user = await FindUserAsync(userId);
        EnsureNotProtected(adminId, user, "remove the Admin role from");
        if (!await userManager.IsInRoleAsync(user, Roles.Admin))
            throw new BusinessRuleException("This user isn't an admin.");

        EnsureSucceeded(await userManager.RemoveFromRoleAsync(user, Roles.Admin));
        EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user));
    }

    /// <summary>Stops the user signing in, and signs them out. An organiser's events are hidden meanwhile.</summary>
    public async Task LockAsync(string adminId, string userId)
    {
        await EnsureActiveAdminAsync(adminId);
        var user = await FindUserAsync(userId);
        EnsureNotProtected(adminId, user, "lock");
        if (IsLockedOut(user))
            throw new BusinessRuleException("This account is already locked.");

        // Locks only work while lockout is enabled for the user.
        EnsureSucceeded(await userManager.SetLockoutEnabledAsync(user, true));
        EnsureSucceeded(await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue));
        EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user));
    }

    public async Task UnlockAsync(string adminId, string userId)
    {
        await EnsureActiveAdminAsync(adminId);
        var user = await FindUserAsync(userId);
        if (!IsLockedOut(user))
            throw new BusinessRuleException("This account isn't locked.");

        EnsureSucceeded(await userManager.SetLockoutEndDateAsync(user, null));
        // Otherwise a few wrong passwords from before the lock could lock them out again.
        EnsureSucceeded(await userManager.ResetAccessFailedCountAsync(user));
    }

    /// <summary>Works out roles and lock status in the same SQL query as the users.</summary>
    private IQueryable<UserForAdmin> ForAdmin(IQueryable<ApplicationUser> users)
    {
        var now = timeProvider.GetUtcNow();
        var organiserRoleIds = db.Roles.Where(r => r.Name == Roles.Organiser).Select(r => r.Id);
        var adminRoleIds = db.Roles.Where(r => r.Name == Roles.Admin).Select(r => r.Id);
        var configAdminEmail = ConfigAdminNormalizedEmail();

        return users.Select(u => new UserForAdmin(
            u.Id,
            u.Email,
            db.UserRoles.Any(ur => ur.UserId == u.Id && organiserRoleIds.Contains(ur.RoleId)),
            db.UserRoles.Any(ur => ur.UserId == u.Id && adminRoleIds.Contains(ur.RoleId)),
            u.LockoutEnd > now,
            configAdminEmail != null && u.NormalizedEmail == configAdminEmail));
    }

    /// <summary>The config admin and the admin themselves can't lose the Admin role or be locked.</summary>
    private void EnsureNotProtected(string adminId, ApplicationUser user, string action)
    {
        if (user.Id == adminId)
            throw new BusinessRuleException($"You can't {action} yourself.");
        if (user.NormalizedEmail is not null && user.NormalizedEmail == ConfigAdminNormalizedEmail())
            throw new BusinessRuleException($"You can't {action} the admin set in the app's configuration.");
    }

    // Normalized like Identity's own lookups, so differences in letter case don't matter.
    private string? ConfigAdminNormalizedEmail()
    {
        var email = configuration["Admin:Email"];
        return string.IsNullOrWhiteSpace(email) ? null : userManager.NormalizeEmail(email);
    }

    private async Task EnsureActiveAdminAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null || IsLockedOut(user) || !await userManager.IsInRoleAsync(user, Roles.Admin))
            throw new BusinessRuleException("Only admins can manage users.");
    }

    // A null LockoutEnd means never locked; "null > now" is false.
    private bool IsLockedOut(ApplicationUser user) => user.LockoutEnd > timeProvider.GetUtcNow();

    private async Task<ApplicationUser> FindUserAsync(string userId) =>
        await userManager.FindByIdAsync(userId)
        ?? throw new BusinessRuleException("This user no longer exists.");

    // Identity reports failures, including another admin changing the user first, as errors.
    private static void EnsureSucceeded(IdentityResult result)
    {
        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure)))
            throw new BusinessRuleException(
                "This user was changed somewhere else at the same time. Reload the page and try again.");
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
