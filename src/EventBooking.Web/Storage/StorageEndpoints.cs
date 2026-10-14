using System.Threading.RateLimiting;
using EventBooking.Core;
using Microsoft.AspNetCore.Mvc;

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

        // Binding an IFormFile makes ASP.NET Core check the antiforgery token automatically.
        group.MapPost("/images", async (IFormFile file, ImageStorage storage, CancellationToken ct) =>
            {
                var result = await storage.UploadAsync(file, ct);
                return result.Error switch
                {
                    ImageUploadError.None => Results.Ok(new { name = result.BlobName }),
                    ImageUploadError.Empty => Results.BadRequest("The file is empty."),
                    ImageUploadError.TooLarge => Results.BadRequest("Images must be 2 MB or smaller."),
                    ImageUploadError.NotAnImage => Results.BadRequest("Only JPEG, PNG and WebP images are allowed."),
                    ImageUploadError.StorageFull => Results.BadRequest("The image storage limit has been reached."),
                    _ => Results.BadRequest(),
                };
            })
            // Reject oversized requests early. The extra 64 KB is for the multipart form overhead.
            .WithMetadata(new RequestSizeLimitAttribute(ImageUploadRules.MaxSizeBytes + 64 * 1024));

        return endpoints;
    }
}
