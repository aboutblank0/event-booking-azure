using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using EventBooking.Web.Components;
using EventBooking.Web.Components.Account;
using EventBooking.Web.Data;
using EventBooking.Web.Storage;

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

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// Azure Blob Storage for event images. There is no key or connection string: the app signs in
// with an Entra ID identity instead. Locally that's your `az login` user; on Azure it's the
// App Service's managed identity. (DefaultAzureCredential would try both, but it first probes
// for a managed identity, which can hang on machines outside Azure, so we choose explicitly.)
TokenCredential azureCredential = builder.Environment.IsDevelopment()
    ? new AzureCliCredential()
    : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
var blobEndpoint = builder.Configuration["Storage:BlobEndpoint"] ?? throw new InvalidOperationException("Setting 'Storage:BlobEndpoint' not found.");
var imagesContainer = builder.Configuration["Storage:ImagesContainer"] ?? throw new InvalidOperationException("Setting 'Storage:ImagesContainer' not found.");
var maxImageCount = builder.Configuration.GetValue<int>("Storage:MaxImageCount");
builder.Services.AddSingleton(new BlobServiceClient(new Uri(blobEndpoint), azureCredential));
builder.Services.AddSingleton(sp => new ImageStorage(
    sp.GetRequiredService<BlobServiceClient>().GetBlobContainerClient(imagesContainer), maxImageCount));
builder.Services.AddStorageRateLimiting();

var app = builder.Build();

// Apply any pending EF Core migrations at startup so the database schema matches the code.
// DbContext is a scoped service, so we create a scope to resolve it outside of a request.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
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
