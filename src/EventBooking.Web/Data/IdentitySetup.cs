using Microsoft.AspNetCore.Identity;

namespace EventBooking.Web.Data;

public static class IdentitySetup
{
    /// <summary>
    /// Registers ASP.NET Identity (users, roles, sign-in). It's an extension method so that
    /// Program.cs and the tests use exactly the same setup.
    /// </summary>
    public static IServiceCollection AddEventBookingIdentity(this IServiceCollection services)
    {
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = true;
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            })
            // Turns on role support (RoleManager, roles in the login cookie). IdentityRole is the
            // built-in role class, stored in the AspNetRoles table.
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // On every request, check the login cookie against the database (default: every 30 minutes).
        // If the user's security stamp still matches, the cookie is rebuilt with their current roles,
        // so a new role applies on their next page load without signing in again. If the stamp changed
        // (role removed, account locked), they're signed out. Costs a couple of small queries per request.
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);

        services.AddScoped<IdentitySeeder>();
        return services;
    }
}
