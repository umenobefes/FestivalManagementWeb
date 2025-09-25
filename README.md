# FestivalManagementWeb

ASP.NET Core 8.0で構築されたフェスティバル管理Webアプリケーションです。Azure Container AppsとCosmos DB（MongoDB API）を使用してクラウドにデプロイされます。

## 🚀 主な機能

- **キー・バリュー管理**: テキストと画像のキー・バリューストレージ
- **ユーザー認証**: Google OAuth 2.0認証とASP.NET Identity
- **Git統合**: デプロイ履歴の追跡とリポジトリ連携
- **Azure監視**: Container AppsとCosmos DBの使用量監視
- **無料枠管理**: Azureの無料枠使用量を監視・制御

## 🏗️ 技術スタック

- **Backend**: ASP.NET Core 8.0 MVC
- **Database**: Azure Cosmos DB (MongoDB API)
- **Authentication**: ASP.NET Identity + Google OAuth
- **Infrastructure**: Azure Container Apps, Azure Container Registry
- **Monitoring**: Application Insights, Log Analytics
- **Deployment**: GitHub Actions + Bicep IaC

## 📦 簡単デプロイ（推奨）

### 1. GitHub Secrets設定

以下の **2つのSecrets** を設定：

#### Azure認証
```bash
# Service Principal作成
az ad sp create-for-rbac \
  --name "festival-mgmt-sp" \
  --role contributor \
  --scopes /subscriptions/{your-subscription-id} \
  --sdk-auth
```
- `AZURE_CREDENTIALS` - 上記コマンドの出力JSON全体

#### アプリケーション設定
- `APP_SECRETS` - アプリケーション設定情報（JSON形式）

**APP_SECRETSの内容例:**
```json
{
  "googleClientId": "your-google-client-id",
  "googleClientSecret": "your-google-client-secret",
  "initialUserEmail": "admin@example.com",
  "gitSettings": {
    "authorName": "Your Name",
    "authorEmail": "you@example.com",
    "token": "github_pat_xxx",
    "cloneUrl": "https://github.com/user/repo.git"
  }
}
```

### 2. デプロイ実行

**mainブランチにプッシュ** または **GitHub Actions手動実行** で自動デプロイ開始

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

## 🔧 新しいBicepベースのデプロイ

**推奨デプロイ方法** - Bicepテンプレートで完全自動化

### デプロイの流れ
1. **GitHub Actions** (`.github/workflows/deploy.yml`) - 統合ワークフロー
2. **Bicepテンプレート** で Azure リソース作成 (`infra/main.bicep`)
3. **Docker イメージ** ビルド・プッシュ
4. **Container App** デプロイ・環境変数設定

### 自動作成されるリソース
- Azure Container Apps環境
- Azure Container Registry
- Azure Cosmos DB (MongoDB API、無料枠有効)
- Application Insights
- Log Analytics Workspace
- 必要な環境変数・監視設定

### 無料枠最適化
- Container Apps: 0.25 vCPU, 0.5Gi メモリ
- Cosmos DB: 無料枠有効（1000 RU/s, 25GB）
- 自動スケールゼロ（アイドル時）

## 🔧 ローカル開発環境

### 必要な設定

1. **appsettings.Development.json**を作成:
```json
{
  "MongoDbSettings": {
    "ConnectionString": "your-cosmos-connection-string",
    "DatabaseName": "festival-test"
  },
  "Authentication": {
    "Google": {
      "ClientId": "your-google-client-id",
      "ClientSecret": "your-google-client-secret"
    }
  },
  "InitialUser": {
    "Email": "your-email@example.com"
  }
}
```

2. **アプリケーション起動**:
```bash
dotnet run --project FestivalManagementWeb
```

## 📊 使用量監視（オプション）

### Usage Guardian CSX
- **Workflow**: `.github/workflows/usage-guardian-csx.yml`
- **機能**: 30分ごとにAzure使用量をチェックし、予算超過時にContainer Appを停止
- **設定**: Azure認証は `AZURE_CREDENTIALS` から自動取得
- **しきい値**: 無料枠予算（vCPU、メモリ、リクエスト数、データ転送量）に基づく自動制御
