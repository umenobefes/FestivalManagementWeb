# FestivalManagementWeb

FestivalManagementWeb is an ASP.NET Core 8.0 application for managing festival content. The cloud deployment targets Azure Container Apps with Azure Cosmos DB (MongoDB API), and the images are published to GitHub Container Registry (GHCR).

## Key Features
- CRUD management for text and image key/value content
- Google OAuth 2.0 based sign-in layered on ASP.NET Identity
- Built-in usage dashboard that tracks Azure free-tier consumption
- Safety rails that warn (or optionally stop) the workload before exceeding free quotas
- Git integration hooks for mirroring deployment metadata

## Tech Stack

| Layer | Primary Technologies |
| --- | --- |
| Backend | ASP.NET Core 8.0 MVC (C# 12) |
| Data | Azure Cosmos DB (MongoDB API) via MongoDB.Driver & GridFS |
| Identity | ASP.NET Identity with Google OAuth 2.0 sign-in |
| Frontend | Razor Views + Bootstrap 5 assets in `wwwroot` |
| Infrastructure | Azure Container Apps, GitHub Container Registry (GHCR) |
| Observability | Azure Monitor metrics, cost management APIs, Application Insights (optional) |
| Automation | Bicep templates, GitHub Actions CI/CD, usage guard scripts |

### Development Stack
- .NET 8 SDK and tooling (Visual Studio 2022 17.10+ or VS Code + C# Dev Kit)
- Azure CLI 2.64+ for resource provisioning/testing of usage collectors
- Docker Desktop (local container builds aligned with CI images)
- MongoDB Shell tools for inspecting Cosmos DB (Mongo API) collections
- Optional: Node.js 20+ if you customize static assets under `wwwroot`

## Deployment Essentials

### Required GitHub Secrets
Register the following secrets in the repository that runs the workflow:

| Secret | Purpose |
| --- | --- |
| `AZURE_CREDENTIALS` | Output of `az ad sp create-for-rbac --sdk-auth` (Contributor on the target subscription) |
| `APP_SECRETS` | App configuration JSON (Google OAuth, initial admin user, git mirror settings, etc.) |
| `GHCR_USERNAME` | GitHub account/organisation that owns the container repository |
| `GHCR_TOKEN` | Personal Access Token (or fine-grained token) with at least `write:packages` scope for GHCR |

> If you keep GHCR credentials in another repository, generate a PAT there and paste the value into `GHCR_TOKEN` here. The workflow only reads these secrets at runtime; no additional wiring is required.

### Default Workflow Behaviour
- `main` branch pushes trigger `.github/workflows/deploy.yml`.
- The workflow builds the Docker image, pushes it to `ghcr.io/<owner>/<repo>`, and then redeploys Azure Container Apps with the new tag.
- If `infra/parameters.json` omits registry parameters, the workflow falls back to `GITHUB_REPOSITORY`/`GITHUB_REPOSITORY_OWNER` so the image name stays aligned with the repo name.

### Manual Deployments
Run the same workflow with **Run workflow** in GitHub Actions to override items such as `imageTag` or `namePrefix`. The Bicep template receives the computed `containerRegistryServer` and `containerRegistryRepository` via inline parameters, so no manual edits are required for the common case.

## Bicep Deployment Details
The template at `infra/main.bicep` provisions:
- Azure Container Apps managed environment
- Azure Cosmos DB (MongoDB API, vCore) configured for the Free Tier
- Application Insights + Log Analytics workspace
- Container App with system-assigned identity
- Supporting configuration (secrets, environment variables, scaling rules)

> The container registry itself is **not** created by Bicep. Images must exist in GHCR (`ghcr.io/<owner>/<repo>:<tag>`) before deployment. The workflow already publishes the image and passes the resolved name to the Bicep deployment.

## Post-Deployment Setup: Role Assignments

After the first deployment, you must **manually assign Azure roles** to the Container App's Managed Identity to enable usage monitoring and cost tracking.

### Required Roles

The Container App needs the following roles to query Azure metrics and cost data:

1. **Reader** - Access to resource metadata and Cosmos DB information
2. **Monitoring Reader** - Access to Azure Monitor metrics (CPU, memory, requests, data transfer)
3. **Cost Management Reader** - Access to Azure Cost Management data (vCPU-seconds, GiB-seconds)

### Assignment Steps

1. Open **Azure Portal** → Navigate to your **Container App** (`<namePrefix>-app`)
2. Go to **Security** → **Identity** in the left menu
3. Verify **System assigned** identity **Status** is **On** (enabled automatically by deployment)
4. Click **Azure role assignments** button
5. Click **+ Add role assignment** and add each of the following:

   **① Reader Role**
   - Scope: **Resource group**
   - Subscription: Select your subscription
   - Resource group: `rg-<namePrefix>`
   - Role: **Reader**
   - Click **Save**

   **② Monitoring Reader Role**
   - Scope: **Resource group**
   - Subscription: Select your subscription
   - Resource group: `rg-<namePrefix>`
   - Role: **Monitoring Reader**
   - Click **Save**

   **③ Cost Management Reader Role**
   - Scope: **Subscription**
   - Subscription: Select your subscription
   - Role: **Cost Management Reader**
   - Click **Save**

> **Note**: These role assignments are **permanent** and only need to be configured once. Future deployments will not require this step.

### Verification

After assigning roles, the Container App can access:
- Azure Monitor metrics for Container Apps (Requests, TxBytes, CPU, Memory)
- Cosmos DB vCore metrics (StorageUsed, CpuPercent, MemoryPercent)
- Azure Cost Management data for usage tracking

The usage banner and Cosmos DB monitoring will start working within a few minutes after role assignment.

## Free-Tier Usage Banner
The web app can display a banner showing estimated remaining free-tier capacity for Azure Container Apps.

- Budgets default to 180,000 vCPU-seconds and 360,000 GiB-seconds per month (the Azure free allowances).
- Enable background collection by setting `AzureUsage__Enabled=true` so the app can query metrics with the assigned managed identity.
- Key environment variables (or `appsettings.json`) include:
  - `FreeTier__Resource__VcpuPerReplica = 0.25`
  - `FreeTier__Resource__MemoryGiBPerReplica = 0.5`
  - `FreeTier__BudgetVcpuSeconds`, `FreeTier__BudgetGiBSeconds`, `FreeTier__Requests__Budget`, `FreeTier__Data__BudgetGb`

## Usage Guardian (Optional)
`scripts/usage-guardian.csx` contains an Azure Functions worker that can freeze the Container App (set min/max replicas to 0) if projected usage exceeds your configured thresholds. Grant the managed identity Container App Contributor + Monitoring Reader + Cost Management Reader and schedule it (e.g., every 30 minutes).

## Local Development
1. Create `FestivalManagementWeb/appsettings.Development.json`:
   ```json
   {
     "MongoDbSettings": {
       "ConnectionString": "your-cosmos-connection-string",
       "DatabaseName": "festival-dev"
     },
     "Authentication": {
       "Google": {
         "ClientId": "your-google-client-id",
         "ClientSecret": "your-google-client-secret"
       }
     },
     "InitialUser": {
       "Email": "admin@example.com"
     }
   }
   ```
2. Run the site locally with `dotnet run --project FestivalManagementWeb`.

## Roadmap / Notes
- Consider splitting the GHCR push and the Azure deploy into separate workflows if you need staged rollouts.
- `infra/parameters.json` still controls `namePrefix`, `location`, and `imageTag`. The workflow overrides registry settings automatically, but you can set explicit values if you use a different GHCR repository or tag.
