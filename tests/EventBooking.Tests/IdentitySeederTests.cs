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
    /// The same Identity services as Program.cs, pointed at the test database. RoleManager and
    /// UserManager need a lot of supporting services, so it's easier to let Identity register them.
    /// </summary>
    private ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(database.ConnectionString));
        services.AddIdentityCore<ApplicationUser>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddScoped<IdentitySeeder>();
        return services.BuildServiceProvider();
    }

    private async Task SeedAsync()
    {
        await using var services = CreateServices();
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync();
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
}
