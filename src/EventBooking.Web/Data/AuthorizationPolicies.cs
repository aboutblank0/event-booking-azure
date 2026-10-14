using EventBooking.Core;

namespace EventBooking.Web.Data;

/// <summary>
/// Named permissions for pages, e.g. [Authorize(Policy = AuthorizationPolicies.ReviewApplications)].
/// Which roles grant each one is decided here.
/// </summary>
public static class AuthorizationPolicies
{
    public const string ReviewApplications = nameof(ReviewApplications);
    public const string CanCreateEvents = nameof(CanCreateEvents);

    public static IServiceCollection AddEventBookingAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(ReviewApplications, policy => policy.RequireRole(Roles.Admin))
            .AddPolicy(CanCreateEvents, policy => policy.RequireRole(Roles.Organiser));
        return services;
    }
}
