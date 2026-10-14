using EventBooking.Core;
using EventBooking.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EventBooking.Tests;

/// <summary>
/// Integration tests: save and load organiser applications in a real SQL Server (see
/// SqlServerFixture). The database is shared, so each test creates its own users.
/// </summary>
[Collection("SqlServer")]
public class OrganiserApplicationDatabaseTests(SqlServerFixture database)
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

    private static OrganiserApplication NewApplication(string userId) =>
        OrganiserApplication.Submit(userId, "Lisbon Tech Club", "+351 912 345 678",
            "https://example.org", "We run monthly meetups.", [], [], Now);

    private async Task<int> SaveAsync(OrganiserApplication application)
    {
        await using var db = database.CreateContext();
        db.OrganiserApplications.Add(application);
        await db.SaveChangesAsync();
        return application.Id;
    }

    [Fact]
    public async Task Application_SavesAndLoadsWithAllValues()
    {
        var userId = await CreateUserAsync();
        var id = await SaveAsync(NewApplication(userId));

        await using var db = database.CreateContext();
        var loaded = await db.OrganiserApplications.SingleAsync(a => a.Id == id);

        Assert.Equal(userId, loaded.UserId);
        Assert.Equal("Lisbon Tech Club", loaded.OrganisationName);
        Assert.Equal("+351 912 345 678", loaded.ContactPhone);
        Assert.Equal("https://example.org", loaded.Website);
        Assert.Equal("We run monthly meetups.", loaded.Description);
        Assert.Equal(ApplicationStatus.Pending, loaded.Status);
        Assert.Equal(Now, loaded.SubmittedAt);
        Assert.Null(loaded.ReviewedById);
        Assert.Null(loaded.ReviewedAt);
    }

    [Fact]
    public async Task Application_ReviewIsSaved()
    {
        var adminId = await CreateUserAsync();
        var id = await SaveAsync(NewApplication(await CreateUserAsync()));

        await using (var db = database.CreateContext())
        {
            var application = await db.OrganiserApplications.SingleAsync(a => a.Id == id);
            application.Reject(adminId, "Please add a website.", Now.AddDays(1));
            await db.SaveChangesAsync();
        }

        await using var check = database.CreateContext();
        var loaded = await check.OrganiserApplications.SingleAsync(a => a.Id == id);
        Assert.Equal(ApplicationStatus.Rejected, loaded.Status);
        Assert.Equal(adminId, loaded.ReviewedById);
        Assert.Equal(Now.AddDays(1), loaded.ReviewedAt);
        Assert.Equal("Please add a website.", loaded.RejectionReason);
    }

    [Fact]
    public async Task Application_SecondReviewOfAStaleCopyFails()
    {
        var firstAdmin = await CreateUserAsync();
        var secondAdmin = await CreateUserAsync();
        var id = await SaveAsync(NewApplication(await CreateUserAsync()));

        // Two admins open the same pending application at the same time.
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstCopy = await first.OrganiserApplications.SingleAsync(a => a.Id == id);
        var secondCopy = await second.OrganiserApplications.SingleAsync(a => a.Id == id);

        firstCopy.Approve(firstAdmin, Now);
        await first.SaveChangesAsync();

        // The second copy still looks Pending, so Core allows it, but its RowVersion is old.
        secondCopy.Reject(secondAdmin, null, Now);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var check = database.CreateContext();
        Assert.Equal(ApplicationStatus.Approved, (await check.OrganiserApplications.SingleAsync(a => a.Id == id)).Status);
    }

    [Fact]
    public async Task Application_SecondPendingApplicationIsRejectedByTheDatabase() // rule 1
    {
        var userId = await CreateUserAsync();
        await SaveAsync(NewApplication(userId));

        // Skips the Core check (empty history), like two submits arriving at the same moment.
        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(NewApplication(userId)));
    }

    [Fact]
    public async Task Application_ReviewedApplicationsDoNotBlockANewOne()
    {
        var adminId = await CreateUserAsync();
        var userId = await CreateUserAsync();
        var rejected = NewApplication(userId);
        rejected.Reject(adminId, null, Now);
        await SaveAsync(rejected);

        // Only Pending rows are in the unique index, so this saves.
        var id = await SaveAsync(NewApplication(userId));

        Assert.NotEqual(0, id);
    }

    [Fact]
    public async Task DeletingTheApplicant_DeletesTheirApplications()
    {
        var userId = await CreateUserAsync();
        var id = await SaveAsync(NewApplication(userId));

        await using (var db = database.CreateContext())
        {
            db.Users.Remove(await db.Users.SingleAsync(u => u.Id == userId));
            await db.SaveChangesAsync();
        }

        await using var check = database.CreateContext();
        Assert.False(await check.OrganiserApplications.AnyAsync(a => a.Id == id));
    }

    [Fact]
    public async Task Reviewer_CannotBeDeletedWhileTheyHaveReviews()
    {
        var adminId = await CreateUserAsync();
        var application = NewApplication(await CreateUserAsync());
        application.Approve(adminId, Now);
        await SaveAsync(application);

        await using var db = database.CreateContext();
        db.Users.Remove(await db.Users.SingleAsync(u => u.Id == adminId));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
