using EventBooking.Core;
using Microsoft.AspNetCore.Identity;

namespace EventBooking.Web.Data;

/// <summary>
/// Creates the roles and gives the configured admin the Admin role. Safe to run on every startup.
/// </summary>
public class IdentitySeeder(
    RoleManager<IdentityRole> roleManager,
    UserManager<ApplicationUser> userManager,
    ILogger<IdentitySeeder> logger)
{
    /// <param name="adminEmail">The <c>Admin:Email</c> setting. Null or empty means no admin is seeded.</param>
    public async Task SeedAsync(string? adminEmail)
    {
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole(role)));
            }
        }

        await SeedAdminAsync(adminEmail);
    }

    // Logs rather than throws: the app still works without an admin.
    private async Task SeedAdminAsync(string? adminEmail)
    {
        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            logger.LogWarning("Admin:Email is not set, so no admin was seeded.");
            return;
        }

        var user = await userManager.FindByEmailAsync(adminEmail);
        if (user is null)
        {
                logger.LogWarning("No user is registered with the Admin:Email address yet, so no admin was seeded.");
            return;
        }

        if (!user.EmailConfirmed)
        {
            logger.LogWarning("The Admin:Email user hasn't confirmed their email, so they weren't made an admin.");
            return;
        }

        if (await userManager.IsInRoleAsync(user, Roles.Admin))
        {
            return;
        }

        // A user can't be both an organiser and an admin.
        if (await userManager.IsInRoleAsync(user, Roles.Organiser))
        {
            logger.LogWarning("The Admin:Email user is an organiser, so they weren't made an admin.");
            return;
        }

        EnsureSucceeded(await userManager.AddToRoleAsync(user, Roles.Admin));
        logger.LogInformation("Gave the Admin role to the Admin:Email user.");
    }

    // Identity reports failures in IdentityResult instead of throwing, so check it explicitly.
    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Seeding Identity failed: " + string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }
}
