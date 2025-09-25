# FestivalManagementWeb

## Free Tier Remaining Time Banner

This app can display a banner on every page with the estimated remaining free-tier hours per day for Azure Container Apps (consumption plan).

### How it works
- Uses monthly free budgets: 180,000 vCPU-seconds and 360,000 GiB-seconds.
- It consumes month-to-date usage from the background AzureUsage collector; if that feature is disabled the banner assumes zero usage.
- The service computes total remaining hours limited by CPU/Memory and divides by the remaining days in the current month (including today).

### Configuration (appsettings.json or environment variables)

`FreeTier` section keys (environment variables use `__`):

- `FreeTier__EnableBanner` (bool, default `true`)
- `FreeTier__BudgetVcpuSeconds` (double, default `180000`)
- `FreeTier__BudgetGiBSeconds` (double, default `360000`)
- `FreeTier__Resource__VcpuPerReplica` (double, default `0.25`)
- `FreeTier__Resource__MemoryGiBPerReplica` (double, default `0.5`)
- `FreeTier__Resource__ReplicaFactor` (double, default `1`)
- `FreeTier__Data__BudgetGb` (double, monthly free outbound data in GB; set per your offer)
- `FreeTier__Requests__Budget` (double, default `2000000`)

### Optional: Auto-collect usage from Azure

Set `AzureUsage` to enable background collection (no env var needed for usage):

- `AzureUsage__Enabled=true`
- Identify the app: either set `AzureUsage__ContainerAppResourceId` or set `AzureUsage__SubscriptionId`, `AzureUsage__ResourceGroup`, `AzureUsage__ContainerAppName`
- Refresh cadences: `AzureUsage__MetricsRefreshMinutes` (Requests/TxBytes), `AzureUsage__CostRefreshMinutes` (vCPU/GiB-seconds)

Permissions (Managed Identity recommended):
- Assign system-assigned identity to the Container App
- Grant `Monitoring Reader` on the app/RG and `Cost Management Reader` at subscription scope

When enabled, the banner and limits use the auto-collected metrics (no manual usage inputs required).

Example (environment variables on Azure Container Apps):

```
FreeTier__EnableBanner=true
FreeTier__BudgetVcpuSeconds=180000
FreeTier__BudgetGiBSeconds=360000
FreeTier__Resource__VcpuPerReplica=0.25
FreeTier__Resource__MemoryGiBPerReplica=0.5
FreeTier__Resource__ReplicaFactor=1
FreeTier__Data__BudgetGb=5
FreeTier__Requests__Budget=2000000
```

Notes:
- Enable `AzureUsage__Enabled` to keep CPU/memory usage accurate; otherwise those meters remain at 0.
- Data egress allowance varies by Azure offer; set `FreeTier__Data__BudgetGb` to match your subscription's free outbound data.

## Usage Guardian Function (optional)

- See `UsageGuardianFunction/` for an Azure Functions app that checks usage every 30 minutes and freezes your Container App (min/max replicas = 0) when projected to exceed the monthly free tier.
- Enable with Managed Identity + roles: Container App Contributor, Monitoring Reader, Cost Management Reader.

## Deploy workflow

- Workflow: `.github/workflows/deploy-container-app.yml`
- Builds & deploys the web app container, ensures the Container Apps extension is available, and reapplies external ingress (port 8080) plus scaling defaults.
- By default it sets `minReplicas=0` and `cooldownPeriod=600` seconds so the app scales to zero when HTTP traffic is absent for ~10 minutes.
- Sets container resources to `0.5 vCPU / 1 GiB` to match the consumption plan free-tier friendly profile.
- Ensures the Container Apps environment exists (creates when missing; set `CA_ENVIRONMENT_LOCATION` for new environments).
- Optional Cosmos DB provisioning (auto-created if missing when variables are set):
  - `COSMOS_ACCOUNT_NAME` (lowercase; required to enable the step)
  - `COSMOS_LOCATION` (Azure region for the account, e.g. `japaneast`)
  - `COSMOS_RESOURCE_GROUP` (defaults to `AZURE_RESOURCE_GROUP` when omitted)
  - `COSMOS_SUBSCRIPTION_ID` (defaults to `AZURE_SUBSCRIPTION_ID`)
  - `COSMOS_DATABASE_NAME` (defaults to `FestivalDb`)
  - `COSMOS_COLLECTION_NAMES` (comma separated; defaults to `TextKeyValues,ImageKeyValues`)
  - `COSMOS_PROVISIONING` (`RequestUnits` or `Autoscale`, default `RequestUnits`)
  - `COSMOS_DATABASE_RU` (default `400`; use `4000` when autoscale)
  - `COSMOS_COLLECTION_RU` (optional per-collection RU; leave blank to share database throughput)
  - `COSMOS_ENABLE_FREE_TIER` (`true` to request the subscription's free-tier benefit)
- Supply the `MongoDbSettings__ConnectionString` setting (or equivalent) to the Container App via environment variables/secrets so the app can connect to Cosmos DB.
- To adjust the idle window, create a repository variable `CA_IDLE_COOLDOWN_SECONDS` (seconds). Leaving it empty keeps the 10-minute stop.

## GitHub Actions Guardian (free)

- Workflow: `.github/workflows/usage-guardian.yml`
- Schedules every 30 minutes. Reads metrics (Requests/TxBytes) + cost (vCPU/GiB-seconds) for the same Container App and freezes it when thresholds are met.
- Configure via repo Secrets/Variables and env:
  - Required:
    - Secrets: `AZURE_SUBSCRIPTION_ID`, `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`
    - Variables: `CA_RESOURCE_GROUP`, `CA_APP_NAME`
  - Budgets/thresholds (env in workflow):
    - `BUDGET_VCPU_SECONDS`, `BUDGET_GIB_SECONDS`, `BUDGET_REQUESTS`, `BUDGET_DATA_GB`
    - Projected-used thresholds: `WARN_PCT` (default 95), `STOP_PCT` (default 100)
    - Actual cost guard: `STOP_ON_ACTUAL_COST` (default `true`), `COST_STOP_THRESHOLD` (default `0`, stop once cost > threshold), `COST_WARN_THRESHOLD` (optional warn threshold)
    - Remaining-budget thresholds (stop when remaining <= pct): `REMAIN_WARN_PCT`, `REMAIN_STOP_PCT`
  - Optional: `DISABLE_INGRESS_ON_STOP=true` to close external ingress when freezing
