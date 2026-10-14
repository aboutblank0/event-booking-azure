using System.Security.Claims;
using EventBooking.Core;
using EventBooking.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace EventBooking.Tests;

/// <summary>
/// Checks the named policies. A ClaimsPrincipal (normally built from the login cookie) is built by hand.
/// </summary>
public class AuthorizationPolicyTests
{
    private static async Task<bool> IsAllowedAsync(string policy, params string[] roles)
    {
        var services = new ServiceCollection().AddLogging().AddEventBookingAuthorization().BuildServiceProvider();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var claims = roles.Select(r => new Claim(ClaimTypes.Role, r)).Append(new Claim(ClaimTypes.Name, "someone"));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));

        return (await authorization.AuthorizeAsync(user, policy)).Succeeded;
    }

    [Fact]
    public async Task ReviewApplications_OnlyAdmins()
    {
        Assert.True(await IsAllowedAsync(AuthorizationPolicies.ReviewApplications, Roles.Admin));
        Assert.False(await IsAllowedAsync(AuthorizationPolicies.ReviewApplications, Roles.Organiser));
        Assert.False(await IsAllowedAsync(AuthorizationPolicies.ReviewApplications));
    }
}
