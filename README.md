# Event Booking Platform

## Running locally

### One-time setup

1. Copy `.env.example` to `.env` and choose a SQL Server password.
2. Store your local settings in [User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) (kept outside the repo):

   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=EventBooking;User Id=sa;Password=<your .env password>;TrustServerCertificate=True" --project src/EventBooking.Web
   dotnet user-secrets set "Storage:BlobEndpoint" "https://<your-storage-account>.blob.core.windows.net/" --project src/EventBooking.Web
   ```

3. Choose the admin account. Register it in the app first, confirm its email, then set its email address
   and restart the app. At startup, that user gets the Admin role.

   ```bash
   dotnet user-secrets set "Admin:Email" "<your email>" --project src/EventBooking.Web
   ```

   For production, see [Setting up the admin](#setting-up-the-admin).

4. Sign in to Azure with `az login`. Locally, the app uses your Azure CLI login to access Blob Storage,
   so your account needs the **Storage Blob Data Contributor** role on the storage account.

### Run

From the repository root:

```bash
docker compose up -d
dotnet watch --project src/EventBooking.Web
```

The first command starts the SQL Server database container. The second runs the web app in Development mode with hot reload.

## Azure

Merging to `main` runs the Deploy workflow, which tests the app and deploys it to the App Service
`jc-eventbooking`.

### Setting up the admin

Production has its own database, so its admin is set up separately from your local one. Do this once,
after the first deploy:

1. **Register on the live site** with the admin's email and confirm it (until the app sends real
   emails, the confirmation link is shown on screen). Do this *before* step 2: otherwise anyone who
   registers with that address first would become the admin.
2. **Add the app setting** `Admin__Email` with that email. App Service passes app settings to the app
   as environment variables, and .NET reads `__` as `:`, so the code sees it as `Admin:Email`.
   Either in the portal (App Service → **Settings → Environment variables** → add it, then **Apply**),
   or with the Azure CLI:

   ```bash
   az webapp config appsettings set --name jc-eventbooking --resource-group <your resource group> --settings Admin__Email=<your email>
   ```

3. **Check it worked.** Saving a setting restarts the app, and at startup it gives that user the Admin
   role. The log stream should show `Gave the Admin role to the Admin:Email user.`:

   ```bash
   az webapp log tail --name jc-eventbooking --resource-group <your resource group>
   ```

   If it logs a warning instead, it says why (e.g. the email isn't confirmed yet). Fix that, then
   restart the app from the portal or with `az webapp restart`.

The setting stays on the App Service, so later deploys and restarts keep the same admin.
