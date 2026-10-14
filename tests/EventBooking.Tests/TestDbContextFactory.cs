using EventBooking.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace EventBooking.Tests;

internal static class TestDbContextFactory
{
    public static ApplicationDbContext Create(string connectionString, params IInterceptor[] interceptors)
    {
        // Without this, IdentityDbContext builds the older schema (no passkeys table).
        // Keep in sync with IdentitySetup.
        var appServices = new ServiceCollection()
            .Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .BuildServiceProvider();

        return new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .UseApplicationServiceProvider(appServices)
            .AddInterceptors(interceptors)
            .Options);
    }
}
