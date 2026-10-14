using EventBooking.Core;

namespace EventBooking.Web.Data;

/// <summary>
/// Named permissions for pages (docs/roles-design.md, "Permission checks in one place").
/// A page says what it needs ([Authorize(Policy = AuthorizationPolicies.ReviewApplications)]),
/// and which roles give it is decided here, once.
/// Pages only control who can open them; the services check again when something is saved.
/// </summary>
public static class AuthorizationPolicies
{
    public const string ReviewApplications = nameof(ReviewApplications);

    public static IServiceCollection AddEventBookingAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(ReviewApplications, policy => policy.RequireRole(Roles.Admin));
        return services;
    }
}
