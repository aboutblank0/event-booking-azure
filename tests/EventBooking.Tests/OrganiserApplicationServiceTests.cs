using EventBooking.Core;
using EventBooking.Web.Data;
using EventBooking.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventBooking.Tests;

/// <summary>
/// Integration tests for OrganiserApplicationService against a real SQL Server, with the app's
/// real Identity setup. Each call runs in its own scope, like a separate web request.
/// The database is shared, so each test creates its own users.
/// </summary>
[Collection("SqlServer")]
public class OrganiserApplicationServiceTests(SqlServerFixture database)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A clock that always says the given time.</summary>
    private class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Runs <paramref name="action"/> with a service from a fresh scope, at the given time.</summary>
    private async Task<T> WithServiceAsync<T>(Func<OrganiserApplicationService, Task<T>> action, DateTimeOffset? now = null)
    {
        await using var services = database.CreateIdentityServices();
        await using var scope = services.CreateAsyncScope();
        // Creates the service with its other dependencies taken from the scope.
        var service = ActivatorUtilities.CreateInstance<OrganiserApplicationService>(
            scope.ServiceProvider, new FixedTimeProvider(now ?? Now));
        return await action(service);
    }

    private Task WithServiceAsync(Func<OrganiserApplicationService, Task> action, DateTimeOffset? now = null) =>
        WithServiceAsync(async s => { await action(s); return true; }, now);

    private async Task<string> CreateUserAsync(string? role = null)
    {
        await using var services = database.CreateIdentityServices();
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync(null);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{Guid.NewGuid()}@example.com";
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        Assert.True((await userManager.CreateAsync(user)).Succeeded);
        if (role is not null)
            Assert.True((await userManager.AddToRoleAsync(user, role)).Succeeded);
        return user.Id;
    }

    private async Task<(IList<string> Roles, string? SecurityStamp)> GetUserStateAsync(string userId)
    {
        await using var services = database.CreateIdentityServices();
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByIdAsync(userId))!;
        return (await userManager.GetRolesAsync(user), user.SecurityStamp);
    }

    private Task<OrganiserApplication> ApplyAsync(string userId, DateTimeOffset? now = null) =>
        WithServiceAsync(s => s.ApplyAsync(userId, "Lisbon Tech Club", "+351 912 345 678", null, null), now);

    private async Task<OrganiserApplication> LoadAsync(int id)
    {
        await using var db = database.CreateContext();
        return await db.OrganiserApplications.SingleAsync(a => a.Id == id);
    }

    // ---------- Applying ----------

    [Fact]
    public async Task Apply_SavesAPendingApplication()
    {
        var userId = await CreateUserAsync();

        var application = await ApplyAsync(userId);

        var loaded = await LoadAsync(application.Id);
        Assert.Equal(ApplicationStatus.Pending, loaded.Status);
        Assert.Equal(userId, loaded.UserId);
        Assert.Equal(Now, loaded.SubmittedAt);
        Assert.Equal(loaded.Id, (await WithServiceAsync(s => s.GetLatestApplicationAsync(userId)))!.Id);
    }

    [Theory] // rule 2, with the roles read from the database
    [InlineData(Roles.Organiser)]
    [InlineData(Roles.Admin)]
    public async Task Apply_RejectsOrganisersAndAdmins(string role)
    {
        var userId = await CreateUserAsync(role);

        await Assert.ThrowsAsync<BusinessRuleException>(() => ApplyAsync(userId));
    }

    [Fact]
    public async Task Apply_RejectsASecondPendingApplication() // rule 1
    {
        var userId = await CreateUserAsync();
        await ApplyAsync(userId);

        await Assert.ThrowsAsync<BusinessRuleException>(() => ApplyAsync(userId));
    }

    [Fact]
    public async Task Apply_TwoAtTheSameMoment_OnlyOneIsSaved() // rule 1, the database check
    {
        var userId = await CreateUserAsync();

        // Both start before either has saved, like a double click on the submit button.
        var results = await Task.WhenAll(
            Record.ExceptionAsync(() => ApplyAsync(userId)),
            Record.ExceptionAsync(() => ApplyAsync(userId)));

        // Usually one fails in the database (a duplicate in the unique index) and is turned into
        // the same BusinessRuleException; if one finishes first, the Core check catches the other.
        Assert.Single(results, e => e is null);
        Assert.Single(results, e => e is BusinessRuleException);
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.OrganiserApplications.CountAsync(a => a.UserId == userId));
    }

    [Fact]
    public async Task Apply_AfterRejection_WaitsOneMonth() // rule 3
    {
        var adminId = await CreateUserAsync(Roles.Admin);
        var userId = await CreateUserAsync();
        var first = await ApplyAsync(userId);
        await WithServiceAsync(s => s.RejectAsync(first.Id, adminId, null));

        await Assert.ThrowsAsync<BusinessRuleException>(() => ApplyAsync(userId, Now.AddDays(20)));
        Assert.NotNull(await WithServiceAsync(s => s.GetReasonCannotApplyAsync(userId), Now.AddDays(20)));
        Assert.Null(await WithServiceAsync(s => s.GetReasonCannotApplyAsync(userId), Now.AddMonths(1)));
        var second = await ApplyAsync(userId, Now.AddMonths(1));

        Assert.Equal(second.Id, (await WithServiceAsync(s => s.GetLatestApplicationAsync(userId), Now.AddMonths(1)))!.Id);
    }

    // ---------- Reviewing ----------

    [Fact]
    public async Task Approve_GivesTheOrganiserRole_WithoutChangingTheSecurityStamp() // rules 4 and 5
    {
        var adminId = await CreateUserAsync(Roles.Admin);
        var userId = await CreateUserAsync();
        var application = await ApplyAsync(userId);
        var (_, stampBefore) = await GetUserStateAsync(userId);

        await WithServiceAsync(s => s.ApproveAsync(application.Id, adminId));

        var loaded = await LoadAsync(application.Id);
        Assert.Equal(ApplicationStatus.Approved, loaded.Status);
        Assert.Equal(adminId, loaded.ReviewedById);
        Assert.Equal(Now, loaded.ReviewedAt);

        var (roles, stampAfter) = await GetUserStateAsync(userId);
        Assert.Equal([Roles.Organiser], roles);
        // An unchanged stamp means they stay signed in and get the role on their next page load.
        Assert.Equal(stampBefore, stampAfter);
    }

    [Fact]
    public async Task Reject_KeepsTheUserANormalUser() // rules 4 and 6
    {
        var adminId = await CreateUserAsync(Roles.Admin);
        var userId = await CreateUserAsync();
        var application = await ApplyAsync(userId);

        await WithServiceAsync(s => s.RejectAsync(application.Id, adminId, "Please add a website."));

        var loaded = await LoadAsync(application.Id);
        Assert.Equal(ApplicationStatus.Rejected, loaded.Status);
        Assert.Equal("Please add a website.", loaded.RejectionReason);
        Assert.Empty((await GetUserStateAsync(userId)).Roles);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Roles.Organiser)]
    public async Task Review_OnlyAdminsCanReview(string? role)
    {
        var notAdminId = await CreateUserAsync(role);
        var application = await ApplyAsync(await CreateUserAsync());

        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.ApproveAsync(application.Id, notAdminId)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.RejectAsync(application.Id, notAdminId, null)));
        Assert.Equal(ApplicationStatus.Pending, (await LoadAsync(application.Id)).Status);
    }

    [Fact]
    public async Task Review_ChecksTheAdminRoleInTheDatabaseAtTheTimeOfTheAction()
    {
        // An admin whose role was removed after their page loaded can't review any more.
        var adminId = await CreateUserAsync(Roles.Admin);
        var application = await ApplyAsync(await CreateUserAsync());
        await using (var services = database.CreateIdentityServices())
        await using (var scope = services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await userManager.RemoveFromRoleAsync((await userManager.FindByIdAsync(adminId))!, Roles.Admin);
        }

        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.ApproveAsync(application.Id, adminId)));
    }

    [Fact]
    public async Task Approve_RejectsAnApplicantWhoIsNowAnAdmin()
    {
        var adminId = await CreateUserAsync(Roles.Admin);
        var userId = await CreateUserAsync();
        var application = await ApplyAsync(userId);
        await using (var services = database.CreateIdentityServices())
        await using (var scope = services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await userManager.AddToRoleAsync((await userManager.FindByIdAsync(userId))!, Roles.Admin);
        }

        await Assert.ThrowsAsync<BusinessRuleException>(() => WithServiceAsync(s => s.ApproveAsync(application.Id, adminId)));
        Assert.Equal([Roles.Admin], (await GetUserStateAsync(userId)).Roles);
    }

    [Fact]
    public async Task Approve_AfterAnotherAdminRejected_FailsAndGivesNoRole()
    {
        var firstAdmin = await CreateUserAsync(Roles.Admin);
        var secondAdmin = await CreateUserAsync(Roles.Admin);
        var userId = await CreateUserAsync();
        var application = await ApplyAsync(userId);

        await using var services = database.CreateIdentityServices();
        await using var scope = services.CreateAsyncScope();
        var approver = ActivatorUtilities.CreateInstance<OrganiserApplicationService>(scope.ServiceProvider, new FixedTimeProvider(Now));
        // The approving admin's request has loaded the application (EF keeps it in this scope)...
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.OrganiserApplications.SingleAsync(a => a.Id == application.Id);

        // ...when another admin rejects it.
        await WithServiceAsync(s => s.RejectAsync(application.Id, secondAdmin, null));

        // The approval uses its stale copy, so the RowVersion check fails and nothing is saved.
        await Assert.ThrowsAsync<BusinessRuleException>(() => approver.ApproveAsync(application.Id, firstAdmin));
        Assert.Equal(ApplicationStatus.Rejected, (await LoadAsync(application.Id)).Status);
        Assert.Empty((await GetUserStateAsync(userId)).Roles);
    }
}
