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

## Azure CLI Setup

The Azure CLI is required for deploying and managing resources. **We recommend using Azure Cloud Shell** for the best compatibility with the commands in this guide.

### Azure Cloud Shell (Recommended)

The easiest way to run the deployment commands is through [Azure Cloud Shell](https://shell.azure.com):

1. Open https://shell.azure.com in your browser
2. Sign in with your Azure account
3. Select **Bash** environment
4. Azure CLI is pre-installed and always up-to-date

**Benefits:**
- No local installation required
- Multi-line commands with `\` work correctly
- Always authenticated with your Azure account
- Consistent Linux/Bash environment

### Local Installation (Alternative)

If you prefer to install locally:

**Windows:**
```powershell
winget install Microsoft.AzureCLI
```

**macOS:**
```bash
brew update && brew install azure-cli
```

**Linux (Ubuntu/Debian):**
```bash
curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash
```

After installation, verify and login:
```bash
az version
az login
```

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

### Generating `AZURE_CREDENTIALS` with Role Assignment Permissions

The deploy workflow relies on the `AZURE_CREDENTIALS` secret to both provision resources and attach Azure Monitor / Cost Management / Cosmos DB for MongoDB roles to the Container App identity. The service principal stored in this secret must be allowed to create role assignments at the subscription scope; otherwise the automatic role configuration in `deploy.yml` and `usage-guardian.csx` will fail.

We recommend granting the principal `Contributor` **and** `User Access Administrator` on the subscription (or a single `Owner` assignment if that fits your policies). The combination keeps resource changes limited to deployment tasks while permitting the GitHub Action to grant the managed identity the monitoring roles it needs.

> **💡 Recommended:** Run the following commands in [Azure Cloud Shell](https://shell.azure.com) (Bash mode) for the best experience. Multi-line commands work correctly there.
>
> **Note:** The `--sdk-auth` flag will show a deprecation warning. This is expected and can be safely ignored - the flag still works and is required for the current GitHub Actions workflow.

1. Sign in to Azure and locate the subscription you plan to use:
   ```bash
   az login
   az account list --output table
   ```
   Copy the **Subscription ID** you want from the table output. You will paste it into the following commands in place of `<subscription-id>`.

2. Set the CLI context to that subscription and create a GitHub Actions service principal with `Contributor` rights. Pick any globally unique service principal name (for example, `https://gha-festival-web`) and replace `<service-principal-name>` with it:
   ```bash
   az account set --subscription <subscription-id>
   az ad sp create-for-rbac \
     --name <service-principal-name> \
     --role Contributor \
     --scopes /subscriptions/<subscription-id> \
     --sdk-auth > azure-credentials.json
   ```
   The command writes `azure-credentials.json`. Upload this file verbatim to the `AZURE_CREDENTIALS` secret later. Copy the `appId` value printed in the JSON so you can use it in the next step.

3. Grant the same service principal `User Access Administrator` so the workflow can attach monitoring roles at deploy time. Run the following commands to fetch the service principal's object ID and assign the role:
   ```bash
   OBJECT_ID=$(az ad sp show --id <service-principal-name> --query id -o tsv)
   az role assignment create \
     --assignee-object-id $OBJECT_ID \
     --assignee-principal-type ServicePrincipal \
     --role "User Access Administrator" \
     --scope /subscriptions/<subscription-id>
   ```
   Using `--assignee-object-id` with `--assignee-principal-type` avoids Graph API permission requirements and prevents warnings during role assignment.

4. (Optional) After the initial deployment creates `rg-<namePrefix>`, you can scope the `Contributor` role down to `/subscriptions/<subscription-id>/resourceGroups/rg-<namePrefix>` while keeping `User Access Administrator` at the subscription level for ongoing role assignments.
5. Upload `azure-credentials.json` to the repository's `AZURE_CREDENTIALS` secret (for example, `gh secret set AZURE_CREDENTIALS < azure-credentials.json`) and delete the local file once the secret is saved.

After these steps the workflow can deploy Bicep templates, create the resource group, and automatically assign the Reader / Monitoring Reader / Cost Management Reader roles that power the usage banner and `usage-guardian.csx` guardrails.

### Default Workflow Behaviour
- `main` branch pushes trigger `.github/workflows/deploy.yml`.
- The workflow builds the Docker image, pushes it to `ghcr.io/<owner>/<repo>`, and then redeploys Azure Container Apps with the new tag.
- If `infra/parameters.json` omits registry parameters, the workflow falls back to `GITHUB_REPOSITORY`/`GITHUB_REPOSITORY_OWNER` so the image name stays aligned with the repo name.

### Manual Deployments
Run the same workflow with **Run workflow** in GitHub Actions to override items such as `imageTag` or `namePrefix`. The Bicep template receives the computed `containerRegistryServer` and `containerRegistryRepository` via inline parameters, so no manual edits are required for the common case.

## Bicep Deployment Details
The deployment uses a two-stage approach:

**Stage 1: Infrastructure (`infra/infrastructure.bicep`)**
- Azure Container Apps managed environment
- Azure Cosmos DB (MongoDB API, vCore) configured for the Free Tier

**Stage 2: Application (`infra/application.bicep`)**
- Container App with system-assigned identity
- Application Insights + Log Analytics workspace (optional)
- Supporting configuration (secrets, environment variables, scaling rules)

Both templates are idempotent and can safely reference existing resources, allowing partial infrastructure updates without recreating existing components.

> The container registry itself is **not** created by Bicep. Images must exist in GHCR (`ghcr.io/<owner>/<repo>:<tag>`) before deployment. The workflow already publishes the image and passes the resolved name to the Bicep deployment.

## Post-Deployment Setup: Role Assignments

The deployment workflow **automatically assigns** the required Azure roles to enable usage monitoring and cost tracking. No manual configuration is needed.

### Automatically Assigned Roles

The workflow assigns the following roles to the Container App's Managed Identity:

1. **Reader** (Resource Group scope) - Access to resource metadata and Cosmos DB information
2. **Monitoring Reader** (Resource Group scope) - Access to Azure Monitor metrics for all resources (Container Apps CPU, memory, requests, data transfer, and Cosmos DB vCore metrics)
3. **Cost Management Reader** (Subscription scope) - Access to Azure Cost Management data (vCPU-seconds, GiB-seconds)

> **Note:** The Monitoring Reader role at resource group scope provides access to metrics from all resources in the group, including Cosmos DB for MongoDB vCore. No additional role assignment is needed for Cosmos DB.

### Manual Assignment (If Needed)

If the automatic assignment fails or you're deploying manually, you can assign roles using these methods:

#### Method 1: Azure Portal (GUI)

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

   > **Note:** The Monitoring Reader role at resource group scope provides access to metrics from all resources, including Cosmos DB. No additional role assignment is needed.

#### Method 2: Azure CLI

```bash
# Get the Container App's Managed Identity Principal ID
PRINCIPAL_ID=$(az containerapp identity show \
  --name <namePrefix>-app \
  --resource-group rg-<namePrefix> \
  --query principalId -o tsv)

echo "Principal ID: $PRINCIPAL_ID"

# Assign Reader role to the resource group
az role assignment create \
  --assignee-object-id $PRINCIPAL_ID \
  --assignee-principal-type ServicePrincipal \
  --role "Reader" \
  --resource-group rg-<namePrefix>

# Assign Monitoring Reader role to the resource group
az role assignment create \
  --assignee-object-id $PRINCIPAL_ID \
  --assignee-principal-type ServicePrincipal \
  --role "Monitoring Reader" \
  --resource-group rg-<namePrefix>

# Assign Cost Management Reader role to the subscription
az role assignment create \
  --assignee-object-id $PRINCIPAL_ID \
  --assignee-principal-type ServicePrincipal \
  --role "Cost Management Reader" \
  --scope /subscriptions/<subscription-id>
```

> **Note**: Role assignments are **permanent** and persist across deployments. The workflow checks for existing assignments to avoid duplicates. The Monitoring Reader role at resource group scope automatically provides access to Cosmos DB metrics.

### Verification

After assigning roles, the Container App can access:
- Azure Monitor metrics for Container Apps (Requests, TxBytes, CPU, Memory)
- Azure Monitor metrics for Cosmos DB for MongoDB vCore (StorageUsed, CpuPercent, MemoryPercent)
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
