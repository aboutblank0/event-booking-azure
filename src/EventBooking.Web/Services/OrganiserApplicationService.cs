using EventBooking.Core;
using EventBooking.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EventBooking.Web.Services;

/// <summary>
/// Applying to become an organiser, and admins reviewing applications (docs/roles-design.md).
/// Pages call this rather than the database, so the permission checks live in one place.
/// Roles are always read from the database at the time of the action, never from the page's
/// signed-in user: an interactive page keeps the roles it loaded with (see the design's
/// technical notes). Broken rules are reported as <see cref="BusinessRuleException"/>.
/// </summary>
public class OrganiserApplicationService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider)
{
    /// <summary>The user's most recent application, or null if they never applied.</summary>
    public Task<OrganiserApplication?> GetLatestApplicationAsync(string userId) =>
        db.OrganiserApplications
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.SubmittedAt)
            .FirstOrDefaultAsync();

    /// <summary>Why the user can't apply right now, or null if they can.</summary>
    public async Task<string?> GetReasonCannotApplyAsync(string userId)
    {
        var user = await FindUserAsync(userId);
        var previous = await db.OrganiserApplications.Where(a => a.UserId == userId).ToListAsync();
        return OrganiserApplication.ReasonCannotApply(
            await userManager.GetRolesAsync(user), previous, timeProvider.GetUtcNow());
    }

    public async Task<OrganiserApplication> ApplyAsync(
        string userId, string organisationName, string contactPhone, string? website, string? description)
    {
        var user = await FindUserAsync(userId);
        var roles = await userManager.GetRolesAsync(user);
        var previous = await db.OrganiserApplications.Where(a => a.UserId == userId).ToListAsync();

        var application = OrganiserApplication.Submit(userId, organisationName, contactPhone, website,
            description, roles, previous, timeProvider.GetUtcNow());

        db.OrganiserApplications.Add(application);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 })
        {
            // 2601 is SQL Server's "duplicate key in a unique index". Here that's the
            // one-pending-application index: another submit (e.g. a double click) got in first.
            db.OrganiserApplications.Remove(application);
            throw new BusinessRuleException("You already have an application waiting for review.");
        }

        return application;
    }

    /// <summary>Approves the application and gives the applicant the Organiser role (rules 4 and 5).</summary>
    public async Task ApproveAsync(int applicationId, string adminId)
    {
        await EnsureAdminAsync(adminId);
        var application = await FindApplicationAsync(applicationId);
        var applicant = await FindUserAsync(application.UserId);

        // Organiser and Admin don't mix. Can only happen if the applicant was made an admin
        // while their application was waiting.
        if (await userManager.IsInRoleAsync(applicant, Roles.Admin))
            throw new BusinessRuleException("This applicant is an admin, so they can't become an organiser.");

        application.Approve(adminId, timeProvider.GetUtcNow());

        // AddToRoleAsync saves through the same DbContext (both are created once per request),
        // so its SaveChanges also saves the approval above, in one transaction: either both
        // happen or neither does. It doesn't change the security stamp, so the applicant stays
        // signed in and gets the role on their next page load.
        var result = await userManager.AddToRoleAsync(applicant, Roles.Organiser);

        // Identity catches the RowVersion failure itself and reports it as a ConcurrencyFailure
        // error instead of an exception.
        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure)))
            throw AlreadyReviewed();
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Couldn't give the Organiser role: " + string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    /// <summary>Rejects the application, with an optional reason shown to the applicant (rules 4 and 6).</summary>
    public async Task RejectAsync(int applicationId, string adminId, string? reason)
    {
        await EnsureAdminAsync(adminId);
        var application = await FindApplicationAsync(applicationId);

        application.Reject(adminId, reason, timeProvider.GetUtcNow());
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw AlreadyReviewed();
        }
    }

    private async Task EnsureAdminAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null || !await userManager.IsInRoleAsync(user, Roles.Admin))
            throw new BusinessRuleException("Only admins can review applications.");
    }

    private async Task<ApplicationUser> FindUserAsync(string userId) =>
        await userManager.FindByIdAsync(userId)
        ?? throw new BusinessRuleException("This user no longer exists.");

    private async Task<OrganiserApplication> FindApplicationAsync(int applicationId) =>
        await db.OrganiserApplications.SingleOrDefaultAsync(a => a.Id == applicationId)
        ?? throw new BusinessRuleException("This application no longer exists.");

    // The RowVersion check failed: another admin saved a decision after this one loaded the application.
    private static BusinessRuleException AlreadyReviewed() =>
        new("Another admin has just reviewed this application. Reload the page to see their decision.");
}
