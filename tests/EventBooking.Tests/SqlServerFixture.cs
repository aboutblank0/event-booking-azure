using EventBooking.Web.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace EventBooking.Tests;

/// <summary>
/// Starts a throwaway SQL Server in Docker and applies the app's real migrations to it.
/// xUnit creates this once and shares it between all test classes in the "SqlServer"
/// collection, so the container (which takes a few seconds to start) starts only once.
/// It's removed when the tests finish.
/// </summary>
public class SqlServerFixture : IAsyncLifetime
{
    // A fixed version (not "latest") so test runs don't change when Microsoft publishes a new image.
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Running the migrations (not EnsureCreated) means the migrations themselves are tested too.
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>
    /// A new context each time, like each web request gets its own. Tests use separate contexts
    /// for saving and loading so they read from the database, not from EF's in-memory cache.
    /// </summary>
    public ApplicationDbContext CreateContext() => TestDbContextFactory.Create(_container.GetConnectionString());
}

[CollectionDefinition("SqlServer")]
public class SqlServerCollection : ICollectionFixture<SqlServerFixture>;
