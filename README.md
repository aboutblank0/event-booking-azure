# Event Booking Platform

## Running locally

From the repository root:

```bash
docker compose up -d
dotnet watch --project src/EventBooking.Web
```

The first command starts the SQL Server database container. The second runs the web app in Development mode with hot reload.
