using System.Threading.RateLimiting;

namespace EventBooking.Web.Storage;

public static class StorageEndpoints
{
    public const string RateLimitPolicy = "storage";

    /// <summary>Limits each user to 20 storage requests per hour, to protect against runaway costs.</summary>
    public static IServiceCollection AddStorageRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(RateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.User.Identity?.Name ?? "anonymous",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromHours(1),
                    }));
        });

    public static IEndpointRouteBuilder MapStorageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicy);

        // Quick way to confirm the app can reach Blob Storage with its identity.
        group.MapGet("/storage-check", async (ImageStorage storage, CancellationToken ct) =>
            Results.Ok(new
            {
                container = storage.ContainerName,
                imageCount = await storage.CountAsync(ct),
                maxImageCount = storage.MaxImageCount,
            }));

        return endpoints;
    }
}
