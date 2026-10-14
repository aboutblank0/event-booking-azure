using Microsoft.AspNetCore.Identity;

namespace EventBooking.Web.Data;

public static class IdentitySetup
{
    /// <summary>Registers ASP.NET Identity. Shared by Program.cs and the tests.</summary>
    public static IServiceCollection AddEventBookingIdentity(this IServiceCollection services)
    {
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = true;
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            })
            // Turns on role support (RoleManager, roles in the login cookie).
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // Check the login cookie against the database on every request (default: every 30 minutes).
        // New roles then apply on the next page load, and a changed security stamp signs the user out.
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);

        services.AddScoped<IdentitySeeder>();
        return services;
    }
}
