using System.Threading.RateLimiting;

namespace EventBooking.Web.Components.Account;

public static class EmailRateLimiting
{
    public const string Policy = "email";

    /// <summary>
    /// Limits each IP address to 10 requests per 15 minutes on the pages that send email,
    /// so one visitor can't use up the hourly sending allowance.
    /// </summary>
    public static IServiceCollection AddEmailRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            // Applies to every policy. Writing a body stops the status code page showing "Not Found".
            options.OnRejected = (context, cancellationToken) =>
                new ValueTask(context.HttpContext.Response.WriteAsync(
                    "Too many attempts. Please try again in a few minutes.", cancellationToken));

            // Visitors on these pages are usually signed out, so partition by IP, not user name.
            options.AddPolicy(Policy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(15),
                    }));
        });
}
