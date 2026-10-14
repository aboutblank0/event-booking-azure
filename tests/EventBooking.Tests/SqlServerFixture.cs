using EventBooking.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace EventBooking.Tests;

/// <summary>
/// Starts a throwaway SQL Server in Docker and applies the migrations. xUnit shares one instance
/// between all test classes in the "SqlServer" collection.
/// </summary>
public class SqlServerFixture : IAsyncLifetime
{
    // A fixed version, so test runs don't change when a new image is published.
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Migrate (not EnsureCreated), so the migrations are tested too.
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>
    /// A new context each time. Use separate ones for saving and loading, so loads hit the database.
    /// </summary>
    public ApplicationDbContext CreateContext() => TestDbContextFactory.Create(ConnectionString);

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>
    /// The app's Identity services, pointed at this database, plus what the web host normally adds.
    /// </summary>
    public ServiceProvider CreateIdentityServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddAuthentication();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(ConnectionString));
        services.AddEventBookingIdentity();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates a user with a unique email, optionally in a role. The database is shared, so each
    /// test creates its own users.
    /// </summary>
    public async Task<string> CreateUserAsync(string? role = null)
    {
        await using var services = CreateIdentityServices();
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
}

[CollectionDefinition("SqlServer")]
public class SqlServerCollection : ICollectionFixture<SqlServerFixture>;
