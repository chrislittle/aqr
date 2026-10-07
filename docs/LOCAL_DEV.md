# Local development

> [!WARNING]
> **Experimental. Not working.** Work-in-progress prototype; see the [README](../README.md) before using anything here.

AQR runs locally in three modes. Pick one based on what you need to test.

| Mode | Database | Data | History / "as of" | Setup |
|---|---|---|---|---|
| **UI preview** | In-memory | Synthetic (Contoso) | No (current state only) | None: `dotnet run --project src/Aqr.Web` |
| **Full local (recommended)** | **Azure SQL Database container**, the real Azure SQL engine | Synthetic | Yes: temporal tables, retention | `scripts/dev-sql.ps1` |
| **Live data** | Azure SQL Database container | Your Azure tenant, via your `az login` | Yes | `scripts/dev-sql.ps1 -Mock $false` plus the settings below |

In Development the app signs you in as a fake **AQR.Admin** (`Auth:DevUser` in `appsettings.Development.json`). This never happens outside Development, where the app only trusts Easy Auth.

## Azure SQL Database container

AQR's local database is the **Azure SQL Database container**: the Azure SQL Database engine itself running locally
(`SERVERPROPERTY('EngineEdition') = 5`). It isn't the SQL Server image. See the Azure SQL team's
[Build locally, ship to cloud](https://devblogs.microsoft.com/azure-sql/build-locally-ship-to-cloud-for-0-azure-sql-for-modern-app-developers/)
post and the [container docs](https://aka.ms/azuresqldb-container). The repo also includes Microsoft's Azure SQL agent
skills (`.agents/skills/azuresql-db-*`, installed with `npx skills add microsoft/azure-sql-database-container`).

The container is in **Private Preview** and is for development only. Production is Azure SQL Database (`azd up`).

### One-time prerequisites (you)

1. **Sign up for the Private Preview** to get the registry username and password: <https://aka.ms/sqldbcontainerpreview-signup>.
2. **Install a container runtime.** On Windows 11, WSL containers (`wslc`) come with WSL. This step needs admin rights and a reboot:
   ```powershell
   wsl --install
   ```
   Docker Desktop or Podman also work.
3. **Sign in to the registry.** The password is a secret, so only you can do this:
   ```powershell
   wslc login sqldbpreview-dpgaeqhmgphzd4bk.azurecr.io -u <username>   # WSL containers (no Linux distro needed)
   # or: docker login sqldbpreview-dpgaeqhmgphzd4bk.azurecr.io -u <username>
   ```

### Start it

```powershell
pwsh ./scripts/dev-sql.ps1            # synthetic data
dotnet run --project src/Aqr.Web
```

The script pulls the image, starts `aqr-sqldb` on port 1433, waits for the engine, creates the `aqr` database, and checks
it really is the Azure SQL engine (EngineEdition 5). It then saves the connection string in **.NET user-secrets**. The
generated `sa` password never touches the repo. On startup the app applies EF Core migrations and sets
`HISTORY_RETENTION_PERIOD` on every temporal table.

### SQL integration tests

`tests/Aqr.Tests/SqlIntegrationTests.cs` runs against the real engine: migrations, 1-year retention on all 11 temporal
tables, write-on-change creating zero history for unchanged rows, "as of" queries, trends, and the single-writer lock.
These tests are skipped unless `AQR_TEST_SQL` is set:

```powershell
$env:AQR_TEST_SQL = "Server=localhost,1433;User Id=sa;Password=<from user-secrets>;Encrypt=True;TrustServerCertificate=True;"
dotnet test
```

Each test creates and drops its own database.

## Live data locally

`az login` to the tenant first, check `az account show`, then:

```powershell
dotnet user-secrets set "Aqr:UseMock" "false" --project src/Aqr.Web
dotnet user-secrets set "Aqr:ManagementGroupIds" "<mg-id>" --project src/Aqr.Web    # and/or
dotnet user-secrets set "Aqr:SubscriptionIds" "<sub-id>" --project src/Aqr.Web     # explicit subscriptions
dotnet run --project src/Aqr.Web
```

The sync reads with your own Azure credentials (Azure CLI, then Azure PowerShell). It never runs Resource Graph unscoped: with no management group and no subscription list configured, it refuses to start. That matters when your login can see delegated (Lighthouse) subscriptions. Every call is read-only: Resource Graph,
`Microsoft.Quota`, Resource SKUs and Subscriptions. You need Reader on the management group.
