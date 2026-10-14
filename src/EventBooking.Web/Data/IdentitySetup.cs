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

        services.AddScoped<IdentitySeeder>();
        return services;
    }
}
