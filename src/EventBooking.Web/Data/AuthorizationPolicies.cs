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
    public const string ManageUsers = nameof(ManageUsers);
    public const string ModerateEvents = nameof(ModerateEvents);

    public static IServiceCollection AddEventBookingAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(ReviewApplications, policy => policy.RequireRole(Roles.Admin))
            .AddPolicy(CanCreateEvents, policy => policy.RequireRole(Roles.Organiser))
            .AddPolicy(ManageUsers, policy => policy.RequireRole(Roles.Admin))
            .AddPolicy(ModerateEvents, policy => policy.RequireRole(Roles.Admin));
        return services;
    }
}
