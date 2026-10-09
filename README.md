# Event Booking Platform

## Running locally

### One-time setup

1. Copy `.env.example` to `.env` and choose a SQL Server password.
2. Store your local settings in [User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) (kept outside the repo):

   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=EventBooking;User Id=sa;Password=<your .env password>;TrustServerCertificate=True" --project src/EventBooking.Web
   dotnet user-secrets set "Storage:BlobEndpoint" "https://<your-storage-account>.blob.core.windows.net/" --project src/EventBooking.Web
   ```

3. Sign in to Azure with `az login`. Locally, the app uses your Azure CLI login to access Blob Storage,
   so your account needs the **Storage Blob Data Contributor** role on the storage account.

### Run

From the repository root:

```bash
docker compose up -d
dotnet watch --project src/EventBooking.Web
```

The first command starts the SQL Server database container. The second runs the web app in Development mode with hot reload.
