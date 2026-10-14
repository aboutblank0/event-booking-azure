using EventBooking.Core;
using Microsoft.AspNetCore.Identity;

namespace EventBooking.Web.Data;

/// <summary>
/// Runs at startup to make sure the roles exist. Safe to run every time: it only adds what's missing.
/// </summary>
public class IdentitySeeder(RoleManager<IdentityRole> roleManager)
{
    public async Task SeedAsync()
    {
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole(role)));
            }
        }
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
