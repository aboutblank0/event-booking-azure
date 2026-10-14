using EventBooking.Core;
using Microsoft.AspNetCore.Identity;

namespace EventBooking.Web.Data;

/// <summary>
/// Runs at startup to make sure the roles exist and the admin from configuration has the Admin
/// role. Safe to run every time: it only adds what's missing.
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

    // Skipped cases are logged rather than thrown: the app still works without an admin, and
    // the log explains why there isn't one.
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
            // The admin registers like anyone else; the next startup gives them the role.
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

        // Organiser and Admin don't mix (docs/roles-design.md).
        if (await userManager.IsInRoleAsync(user, Roles.Organiser))
        {
            logger.LogWarning("The Admin:Email user is an organiser, so they weren't made an admin.");
            return;
        }

        EnsureSucceeded(await userManager.AddToRoleAsync(user, Roles.Admin));
        logger.LogInformation("Gave the Admin role to the Admin:Email user.");
    }

    // Identity methods report failure through IdentityResult instead of throwing, so a failed
    // seed would otherwise go unnoticed. Failing startup makes the problem obvious.
    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Seeding Identity failed: " + string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }
}
