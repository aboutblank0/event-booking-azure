using EventBooking.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventBooking.Tests;

internal static class TestDbContextFactory
{
    public static ApplicationDbContext Create(string connectionString)
    {
        // IdentityDbContext reads the Identity schema version from the app's services. Without
        // this it would build the older schema (no passkeys table) and not match the migrations.
        // Keep in sync with AddIdentityCore in Program.cs.
        var appServices = new ServiceCollection()
            .Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .BuildServiceProvider();

        return new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .UseApplicationServiceProvider(appServices)
            .Options);
    }
}
