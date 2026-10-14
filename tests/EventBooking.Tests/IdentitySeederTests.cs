using EventBooking.Core;
using EventBooking.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventBooking.Tests;

/// <summary>
/// Integration tests for the startup seeding in docs/roles-design.md, against a real SQL Server.
/// The database is shared with other tests, so each test uses its own users.
/// </summary>
[Collection("SqlServer")]
public class IdentitySeederTests(SqlServerFixture database)
{
    /// <summary>
    /// The app's Identity services (the same method Program.cs calls), pointed at the test database.
    /// Logging and data protection are normally added by the web host, so the test adds them itself.
    /// </summary>
    private ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(database.ConnectionString));
        services.AddEventBookingIdentity();
        return services.BuildServiceProvider();
    }

    private async Task SeedAsync(string? adminEmail = null)
    {
        await using var services = CreateServices();
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync(adminEmail);
    }

    /// <summary>Registers a user through UserManager, which fills in the normalized email it searches by.</summary>
    private async Task<string> CreateUserAsync(bool emailConfirmed = true, string? role = null)
    {
        // Roles must exist before a user can be given one.
        await SeedAsync();

        var email = $"{Guid.NewGuid()}@example.com";
        await using var services = CreateServices();
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = emailConfirmed };
        Assert.True((await userManager.CreateAsync(user)).Succeeded);
        if (role is not null)
        {
            Assert.True((await userManager.AddToRoleAsync(user, role)).Succeeded);
        }
        return email;
    }

    private async Task<IList<string>> GetRolesAsync(string email)
    {
        await using var services = CreateServices();
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await userManager.GetRolesAsync((await userManager.FindByEmailAsync(email))!);
    }

    [Fact]
    public async Task Seed_CreatesTheRoles()
    {
        await SeedAsync();

        await using var db = database.CreateContext();
        var roles = await db.Roles.Select(r => r.Name).ToListAsync();
        Assert.Contains(Roles.Organiser, roles);
        Assert.Contains(Roles.Admin, roles);
    }

    [Fact]
    public async Task Seed_CanRunAgainWithoutDuplicatingRoles()
    {
        // Runs on every startup, so the second run must be harmless.
        await SeedAsync();
        await SeedAsync();

        await using var db = database.CreateContext();
        Assert.Equal(1, await db.Roles.CountAsync(r => r.Name == Roles.Admin));
    }

    [Fact]
    public async Task Seed_MakesTheConfigUserAnAdmin()
    {
        var email = await CreateUserAsync();

        await SeedAsync(email);

        Assert.Equal([Roles.Admin], await GetRolesAsync(email));
    }

    [Fact]
    public async Task Seed_FindsTheConfigUserWhateverTheCaseOfTheEmail()
    {
        // Identity compares emails case-insensitively, so the setting doesn't need the exact case.
        var email = await CreateUserAsync();

        await SeedAsync(email.ToUpperInvariant());

        Assert.Equal([Roles.Admin], await GetRolesAsync(email));
    }

    [Fact]
    public async Task Seed_CanRunAgainForAnExistingAdmin()
    {
        var email = await CreateUserAsync();

        await SeedAsync(email);
        await SeedAsync(email);

        Assert.Equal([Roles.Admin], await GetRolesAsync(email));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nobody-registered@example.com")]
    public async Task Seed_DoesNothingWithoutARegisteredConfigUser(string? adminEmail)
    {
        // Startup must not fail: the admin may not have registered yet.
        await SeedAsync(adminEmail);
    }

    [Fact]
    public async Task Seed_DoesNotPromoteAUserWithAnUnconfirmedEmail()
    {
        var email = await CreateUserAsync(emailConfirmed: false);

        await SeedAsync(email);

        Assert.Empty(await GetRolesAsync(email));
    }

    [Fact]
    public async Task Seed_DoesNotPromoteAnOrganiser()
    {
        // Organiser and Admin don't mix.
        var email = await CreateUserAsync(role: Roles.Organiser);

        await SeedAsync(email);

        Assert.Equal([Roles.Organiser], await GetRolesAsync(email));
    }
}
