using EventBooking.Core;
using EventBooking.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EventBooking.Tests;

/// <summary>
/// How role changes reach a signed-in user. Each request, Identity calls ValidateSecurityStampAsync:
/// a user means the cookie is rebuilt with CreateUserPrincipalAsync, null means sign out.
/// </summary>
[Collection("SqlServer")]
public class LoginRefreshTests(SqlServerFixture database)
{
    [Fact]
    public void LoginCookie_IsCheckedOnEveryRequest()
    {
        using var services = database.CreateIdentityServices();

        var options = services.GetRequiredService<IOptions<SecurityStampValidatorOptions>>().Value;

        Assert.Equal(TimeSpan.Zero, options.ValidationInterval);
    }

    [Fact]
    public async Task LoginCookie_PicksUpANewRoleWithoutSigningOut()
    {
        await using var services = database.CreateIdentityServices();
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync(null);
        var user = await CreateUserAsync(userManager);

        // What the cookie holds after signing in: no roles yet.
        var cookieUser = await signInManager.CreateUserPrincipalAsync(user);
        Assert.False(cookieUser.IsInRole(Roles.Organiser));

        // An admin approves them. The security stamp is left alone.
        await userManager.AddToRoleAsync(user, Roles.Organiser);

        // Next request: the stamp still matches, so the cookie is rebuilt with the new role.
        var verified = await signInManager.ValidateSecurityStampAsync(cookieUser);
        Assert.NotNull(verified);
        var refreshed = await signInManager.CreateUserPrincipalAsync(verified);
        Assert.True(refreshed.IsInRole(Roles.Organiser));
    }

    [Fact]
    public async Task LoginCookie_IsRejectedAfterTheSecurityStampChanges()
    {
        await using var services = database.CreateIdentityServices();
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var user = await CreateUserAsync(userManager);
        var cookieUser = await signInManager.CreateUserPrincipalAsync(user);

        // What removing a role or locking an account will do.
        await userManager.UpdateSecurityStampAsync(user);

        // Next request: the stamp doesn't match, so the user is signed out.
        Assert.Null(await signInManager.ValidateSecurityStampAsync(cookieUser));
    }

    private static async Task<ApplicationUser> CreateUserAsync(UserManager<ApplicationUser> userManager)
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        Assert.True((await userManager.CreateAsync(user)).Succeeded);
        return user;
    }
}
