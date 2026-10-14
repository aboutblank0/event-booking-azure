using EventBooking.Core;
using EventBooking.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EventBooking.Web.Services;

/// <summary>An application with its applicant's email, for the admin pages.</summary>
public record ApplicationWithApplicant(OrganiserApplication Application, string? ApplicantEmail);

/// <summary>
/// Applying to become an organiser, and admins reviewing applications.
/// Roles are read from the database, not the page's signed-in user, because an interactive
/// page keeps the roles it loaded with. Broken rules throw <see cref="BusinessRuleException"/>.
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
            // 2601 is "duplicate key in a unique index": another submit got in first.
            db.OrganiserApplications.Remove(application);
            throw new BusinessRuleException("You already have an application waiting for review.");
        }

        return application;
    }

    /// <summary>Pending applications, oldest first, for admins to review.</summary>
    public async Task<List<ApplicationWithApplicant>> GetPendingApplicationsAsync(string adminId)
    {
        await EnsureAdminAsync(adminId);
        // Sorted before the join, because EF can't sort by a property of the record.
        var pending = db.OrganiserApplications
            .Where(a => a.Status == ApplicationStatus.Pending)
            .OrderBy(a => a.SubmittedAt);
        return await WithApplicant(pending).ToListAsync();
    }

    /// <summary>One application of any status, or null if it doesn't exist. Admins only.</summary>
    public async Task<ApplicationWithApplicant?> GetApplicationAsync(int applicationId, string adminId)
    {
        await EnsureAdminAsync(adminId);
        return await WithApplicant(db.OrganiserApplications.Where(a => a.Id == applicationId))
            .SingleOrDefaultAsync();
    }

    /// <summary>Approves the application and gives the applicant the Organiser role.</summary>
    public async Task ApproveAsync(int applicationId, string adminId)
    {
        await EnsureAdminAsync(adminId);
        var application = await FindApplicationAsync(applicationId);
        var applicant = await FindUserAsync(application.UserId);

        // A user can't be both an organiser and an admin.
        if (await userManager.IsInRoleAsync(applicant, Roles.Admin))
            throw new BusinessRuleException("This applicant is an admin, so they can't become an organiser.");

        application.Approve(adminId, timeProvider.GetUtcNow());

        // Shares this request's DbContext, so its save also saves the approval, in one transaction.
        var result = await userManager.AddToRoleAsync(applicant, Roles.Organiser);

        // Identity reports a RowVersion conflict as an error, not an exception.
        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure)))
            throw AlreadyReviewed();
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Couldn't give the Organiser role: " + string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    /// <summary>Rejects the application, with an optional reason shown to the applicant.</summary>
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

    // Core can't reference ApplicationUser, so the email is joined in by UserId (one SQL query).
    private IQueryable<ApplicationWithApplicant> WithApplicant(IQueryable<OrganiserApplication> applications) =>
        applications.Join(db.Users, a => a.UserId, u => u.Id, (a, u) => new ApplicationWithApplicant(a, u.Email));

    private async Task EnsureAdminAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null || !await userManager.IsInRoleAsync(user, Roles.Admin))
            throw new BusinessRuleException("Only admins can see and review applications.");
    }

    private async Task<ApplicationUser> FindUserAsync(string userId) =>
        await userManager.FindByIdAsync(userId)
        ?? throw new BusinessRuleException("This user no longer exists.");

    private async Task<OrganiserApplication> FindApplicationAsync(int applicationId) =>
        await db.OrganiserApplications.SingleOrDefaultAsync(a => a.Id == applicationId)
        ?? throw new BusinessRuleException("This application no longer exists.");

    // For when the RowVersion check fails.
    private static BusinessRuleException AlreadyReviewed() =>
        new("Another admin has just reviewed this application. Reload the page to see their decision.");
}
