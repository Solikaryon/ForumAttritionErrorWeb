# ForoAttritionErrorWeb

ASP.NET Core Razor Pages web application for an internal forum (categories, posts, replies, and user authentication).

## Requirements

- .NET SDK 10.0 (or a compatible SDK for `net10.0`)
- SQL Server (if you want to enable real data access)

## Run locally

1. Restore packages:

   ```bash
   dotnet restore
   ```

2. Run the app:

   ```bash
   dotnet run
   ```

3. Open the URL shown in the console (for example, `https://localhost:xxxx`).

## Database configuration

The app can start without a configured database connection. In that case, when it tries to query data, it will show a missing configuration message.

### Option A: Environment variable (recommended)

Set:

- `ConnectionStrings__AttritionForo`

Expected format:

```text
Server=<SERVER_IP_OR_HOST>;Database=<DB_NAME>;User ID=<DB_USER>;Password=<DB_PASSWORD>;TrustServerCertificate=True;Encrypt=False
```

### Option B: appsettings

You can define the connection string in:

- `appsettings.Development.json`
- `appsettings.json`

Field:

- `ConnectionStrings:AttritionForo`

## Important files

- `Program.cs`: main app and services configuration.
- `Infrastructure/DatabaseBootstrapper.cs`: database initialization.
- `Data/*.cs`: SQL repositories (users, categories, posts, replies).
- `Pages/`: Razor Pages views (Account and Forum).

## Publish output

A publish output already exists at `publish/win-x64-singlefile/`.

## Notes

- Do not store real credentials in the repository.
- Use environment variables or user secrets for sensitive values.
