using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using EventBooking.Web.Components;
using EventBooking.Web.Components.Account;
using EventBooking.Web.Data;
using EventBooking.Web.Services;
using EventBooking.Web.Storage;
using Microsoft.AspNetCore.Identity.UI.Services;
using Azure.Communication.Email;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, o => o.EnableRetryOnFailure()));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddEventBookingIdentity();
builder.Services.AddEventBookingAuthorization();

// Services use TimeProvider instead of DateTimeOffset.UtcNow, so tests can fix the time.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<OrganiserApplicationService>();
builder.Services.AddScoped<EventService>();
builder.Services.AddScoped<UserAdminService>();
builder.Services.AddScoped<BookingService>();

// Blob Storage signs in without keys: as the `az login` user locally, and with the App Service's
// managed identity on Azure. (DefaultAzureCredential can hang outside Azure, so choose explicitly.)
TokenCredential azureCredential = builder.Environment.IsDevelopment()
    ? new AzureCliCredential()
    : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);

// Setup email sending.
if (builder.Environment.IsDevelopment())
{
    var smtpHost = builder.Configuration["Email:SmtpHost"];
    var smtpPort = builder.Configuration.GetValue<int>("Email:SmtpPort");
    var smtpSender = builder.Configuration["Email:SenderAddress"];

    if (string.IsNullOrEmpty(smtpHost) || smtpPort <= 0 || string.IsNullOrEmpty(smtpSender))
    {
        throw new InvalidOperationException("Email:SmtpHost, Email:SmtpPort and Email:SenderAddress must be set in development.");
    }
    builder.Services.AddSingleton<IEmailSender>(sp => new SmtpEmailSender(smtpHost, smtpPort, smtpSender));
}
else
{
    var emailEndpoint = builder.Configuration["Email:Endpoint"] ?? throw new InvalidOperationException("Setting 'Email:Endpoint' not found.");
    var senderAddress = builder.Configuration["Email:SenderAddress"] ?? throw new InvalidOperationException("Setting 'Email:SenderAddress' not found.");
    builder.Services.AddSingleton<IEmailSender>(sp => new AzureEmailSender(new EmailClient(new Uri(emailEndpoint), azureCredential), senderAddress));
}
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityEmailSender>();


var blobEndpoint = builder.Configuration["Storage:BlobEndpoint"] ?? throw new InvalidOperationException("Setting 'Storage:BlobEndpoint' not found.");
var imagesContainer = builder.Configuration["Storage:ImagesContainer"] ?? throw new InvalidOperationException("Setting 'Storage:ImagesContainer' not found.");
var maxImageCount = builder.Configuration.GetValue<int>("Storage:MaxImageCount");
builder.Services.AddSingleton(new BlobServiceClient(new Uri(blobEndpoint), azureCredential));
builder.Services.AddSingleton(sp => new ImageStorage(
    sp.GetRequiredService<BlobServiceClient>().GetBlobContainerClient(imagesContainer), maxImageCount));
builder.Services.AddSingleton<IImageStorage>(sp => sp.GetRequiredService<ImageStorage>());
builder.Services.AddSingleton(new ImageUrls(new Uri(blobEndpoint), imagesContainer));
builder.Services.AddStorageRateLimiting();
builder.Services.AddEmailRateLimiting();

var app = builder.Build();

// Apply pending migrations at startup. DbContext is scoped, so it needs a scope outside a request.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();

    // Admin:Email comes from user secrets locally and App Service settings on Azure.
    await scope.ServiceProvider.GetRequiredService<IdentitySeeder>()
        .SeedAsync(app.Configuration["Admin:Email"]);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();
app.UseRateLimiter();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.MapStorageEndpoints();

app.Run();
