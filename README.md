# Event Booking Platform

[![CI](https://github.com/aboutblank0/event-booking-azure/actions/workflows/ci.yml/badge.svg)](https://github.com/aboutblank0/event-booking-azure/actions/workflows/ci.yml)
[![Deploy](https://github.com/aboutblank0/event-booking-azure/actions/workflows/deploy.yml/badge.svg)](https://github.com/aboutblank0/event-booking-azure/actions/workflows/deploy.yml)

A web app where organisers publish events and people book places at them.

Live site: <https://jc-eventbooking-f3e9bwgyfyd4fbcg.ukwest-01.azurewebsites.net>
(it runs on a free tier, so the first page load can take a few seconds)

## About

I made this project to learn .NET and Azure, which I hadn't used before. I wrote it with
[Claude Code](https://claude.com/claude-code), designing each feature first and building it in
small pull requests.

## Features

- Browse upcoming events, with images, a map of the venue and the number of places left.
- Book a place and cancel it, and see your upcoming and past bookings.
- Apply to become an organiser. Organisers can create, publish and cancel events and see who is attending.
- Admins review organiser applications and manage users.
- Accounts with email confirmation, two-factor authentication and passkeys.

## What I used

- .NET 10, Blazor, ASP.NET Core Identity and EF Core
- Azure App Service, Azure SQL and Blob Storage, all on free tiers
- Azure Communication Services to send emails (paid per email, a fraction of a cent each)
- GitHub Actions to test every pull request and deploy every merge to `main`
- xUnit and Testcontainers for tests (256 so far)

## Things it handles

- Concurrency, so two people can't book the last place and edits don't overwrite each other.
- Permissions, so role changes and locked accounts take effect straight away.
- Image uploads, checked by file content and limited in size.
- Time zones: event times are shown as they are at the venue.
- No secrets are stored in the code, and the app and GitHub Actions sign in to Azure without passwords.

## Project structure

- `src/EventBooking.Core`: entities and business rules
- `src/EventBooking.Web`: Blazor pages, services, database and Identity
- `tests/EventBooking.Tests`: unit and integration tests

## Roadmap

- [ ] Infrastructure as code with Bicep
- [ ] Monitoring with Application Insights and a health check
- [ ] Email attendees when an event is cancelled
- [ ] Let organisers who have events delete their account

## Running it yourself

Needs the .NET 10 SDK, Docker and the Azure CLI.

1. Copy `.env.example` to `.env` and choose a SQL Server password.
2. Set your local settings with [User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets):

   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=EventBooking;User Id=sa;Password=<your .env password>;TrustServerCertificate=True" --project src/EventBooking.Web
   dotnet user-secrets set "Storage:BlobEndpoint" "https://<your-storage-account>.blob.core.windows.net/" --project src/EventBooking.Web
   dotnet user-secrets set "Admin:Email" "<your email>" --project src/EventBooking.Web
   ```

3. Run `az login`. Your account needs the **Storage Blob Data Contributor** role on the storage account.
4. Start the database, a local email inbox and the app:

   ```bash
   docker compose up -d
   dotnet watch --project src/EventBooking.Web
   ```

   Emails sent while developing go to the local inbox at <http://localhost:8025>, not to real addresses.

The `Admin:Email` user gets the Admin role at startup, once registered and confirmed. On Azure, register
and confirm the admin account on the live site first, then add the app setting `Admin__Email`.

On Azure, emails need the app settings `Email__Endpoint` and `Email__SenderAddress` (from the Communication
Services resource), and the App Service's managed identity needs the **Communication and Email Service Owner**
role on that resource.
