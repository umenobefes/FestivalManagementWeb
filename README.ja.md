# FestivalManagementWeb

FestivalManagementWebは、フェスティバルコンテンツを管理するためのASP.NET Core 8.0アプリケーションです。クラウドデプロイメントはAzure Container AppsとAzure Cosmos DB（MongoDB API）を使用し、イメージはGitHub Container Registry（GHCR）に公開されます。

## 主な機能
- テキストと画像のキー/バリューコンテンツのCRUD管理
- ASP.NET IdentityをベースとしたGoogle OAuth 2.0サインイン
- Azureフリーティアの使用状況を追跡する組み込みダッシュボード
- フリークォータを超える前に警告（またはオプションで停止）するセーフティ機能
- デプロイメントメタデータをミラーリングするGit連携フック

## 技術スタック

| レイヤー | 主要技術 |
| --- | --- |
| バックエンド | ASP.NET Core 8.0 MVC (C# 12) |
| データ | Azure Cosmos DB (MongoDB API) via MongoDB.Driver & GridFS |
| 認証 | ASP.NET Identity + Google OAuth 2.0サインイン |
| フロントエンド | Razor Views + Bootstrap 5 (`wwwroot`内のアセット) |
| インフラ | Azure Container Apps, GitHub Container Registry (GHCR) |
| 可観測性 | Azure Monitorメトリクス、コスト管理API、Application Insights（オプション） |
| 自動化 | Bicepテンプレート、GitHub Actions CI/CD、使用状況ガードスクリプト |

### 開発スタック
- .NET 8 SDK とツール (Visual Studio 2022 17.10+ または VS Code + C# Dev Kit)
- Azure CLI 2.64+ (リソースプロビジョニング/使用状況コレクターのテスト用)
- Docker Desktop (CI イメージに準拠したローカルコンテナビルド)
- MongoDB Shell ツール (Cosmos DB（Mongo API）コレクションの検査用)
- オプション: Node.js 20+ (`wwwroot`配下の静的アセットをカスタマイズする場合)

## Azure CLI セットアップ

Azure CLI はリソースのデプロイと管理に必要です。**このガイドのコマンドとの互換性のため、Azure Cloud Shell の使用を推奨します。**

### Azure Cloud Shell（推奨）

デプロイコマンドを実行する最も簡単な方法は、[Azure Cloud Shell](https://shell.azure.com) を使用することです：

1. https://shell.azure.com をブラウザで開く
2. Azure アカウントでサインイン
3. **Bash** 環境を選択
4. Azure CLI がプリインストールされ、常に最新の状態

**利点:**
- ローカルインストール不要
- `\` による複数行コマンドが正しく動作
- Azure アカウントで常に認証済み
- 一貫した Linux/Bash 環境

### ローカルインストール（代替手段）

ローカルにインストールする場合：

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

インストール後、確認とログイン：
```bash
az version
az login
```

## デプロイメント必須項目

### 必要なGitHub Secrets
ワークフローを実行するリポジトリに以下のシークレットを登録してください：

| シークレット | 目的 |
| --- | --- |
| `AZURE_CREDENTIALS` | `az ad sp create-for-rbac --sdk-auth`の出力（対象サブスクリプションの共同作成者権限） |
| `APP_SECRETS` | アプリケーション設定JSON（Google OAuth、初期管理者ユーザー、Gitミラー設定など） |
| `GHCR_USERNAME` | コンテナリポジトリを所有するGitHubアカウント/組織 |
| `GHCR_TOKEN` | GHCRへの`write:packages`スコープを持つPersonal Access Token（または細粒度トークン） |

> 別のリポジトリにGHCR認証情報がある場合は、そこでPATを生成し、値をここの`GHCR_TOKEN`に貼り付けてください。ワークフローは実行時にこれらのシークレットを読み取るだけで、追加の配線は不要です。

### `AZURE_CREDENTIALS` のロール割り当て権限を付与する手順

GitHub Actions のデプロイワークフローは、`AZURE_CREDENTIALS` に保存したサービス プリンシパルを使って Azure リソースを作成し、Azure Monitor / Cost Management / Cosmos DB for MongoDB のロールをコンテナー アプリのマネージド ID に自動で付与します。そのため、このサービス プリンシパルにはサブスクリプション レベルでロール割り当てを作成できる権限が必要です。

推奨構成はサブスクリプションに対して `Contributor` と `User Access Administrator` を両方付与することです (ポリシーが許せば `Owner` 1 つでも可)。`Contributor` でデプロイ作業を制限しつつ、`User Access Administrator` でロール割り当てが行えます。

> **💡 推奨:** 以下のコマンドは [Azure Cloud Shell](https://shell.azure.com) （Bash モード）で実行することを推奨します。複数行コマンドが正しく動作します。
>
> **注意:** `--sdk-auth` フラグは非推奨の警告が表示されますが、これは想定内で無視して問題ありません。フラグは現在も動作し、現在の GitHub Actions ワークフローに必要です。

1. Azure にサインインし、対象サブスクリプションを確認します。
   ```bash
   az login
   az account list --output table
   ```
   表に表示された **Subscription ID** を控え、今後の手順で `<subscription-id>` の代わりに貼り付けてください。

2. CLI のコンテキストをそのサブスクリプションに切り替え、GitHub Actions 用サービス プリンシパルを `Contributor` 権限付きで作成します。グローバルに一意なサービス プリンシパル名 (例: `https://gha-festival-web`) を決め、`<service-principal-name>` を置き換えてください。
   ```bash
   az account set --subscription <subscription-id>
   az ad sp create-for-rbac \
     --name <service-principal-name> \
     --role Contributor \
     --scopes /subscriptions/<subscription-id> \
     --sdk-auth > azure-credentials.json
   ```
   生成された `azure-credentials.json` はそのまま `AZURE_CREDENTIALS` シークレットの値になります。ファイルに表示される `appId` の値をコピーして、次の手順で使えるようにしておきます。

3. 同じサービス プリンシパルに `User Access Administrator` を付与し、デプロイ時にロール割り当てが行えるようにします。以下のコマンドでサービス プリンシパルのオブジェクト ID を取得してロールを割り当てます。
   ```bash
   OBJECT_ID=$(az ad sp show --id <service-principal-name> --query id -o tsv)
   az role assignment create \
     --assignee-object-id $OBJECT_ID \
     --assignee-principal-type ServicePrincipal \
     --role "User Access Administrator" \
     --scope /subscriptions/<subscription-id>
   ```
   `--assignee-object-id` と `--assignee-principal-type` を使用することで、Graph API 権限が不要になり、ロール割り当て時の警告を回避できます。

4. (任意) 初回デプロイで `rg-<namePrefix>` が作成された後は、`Contributor` のスコープを `/subscriptions/<subscription-id>/resourceGroups/rg-<namePrefix>` に絞り、`User Access Administrator` はサブスクリプションに残すと継続的にロール付与が可能です。
5. `azure-credentials.json` の内容をリポジトリの `AZURE_CREDENTIALS` シークレットへ登録し、登録後はローカル ファイルを削除してください (例: `gh secret set AZURE_CREDENTIALS < azure-credentials.json`)。

これでデプロイ時に Bicep テンプレートの適用・リソース グループ作成・Container Apps / Cosmos DB / Cost Management のロール割り当てが自動で実行でき、`usage-guardian.csx` やアプリ内の使用量表示が動作します。

### デフォルトのワークフロー動作
- `main`ブランチへのpushで`.github/workflows/deploy.yml`がトリガーされます。
- ワークフローはDockerイメージをビルドし、`ghcr.io/<owner>/<repo>`にpushし、その後新しいタグでAzure Container Appsを再デプロイします。
- `infra/parameters.json`でレジストリパラメータが省略されている場合、ワークフローは`GITHUB_REPOSITORY`/`GITHUB_REPOSITORY_OWNER`にフォールバックするため、イメージ名はリポジトリ名と一致します。

### 手動デプロイメント
GitHub Actionsで**Run workflow**を使用して同じワークフローを実行し、`imageTag`や`namePrefix`などの項目を上書きできます。Bicepテンプレートは計算された`containerRegistryServer`と`containerRegistryRepository`をインラインパラメータで受け取るため、一般的なケースでは手動編集は不要です。

## Bicepデプロイメントの詳細
`infra/main.bicep`のテンプレートでプロビジョニングされるもの：
- Azure Container Apps管理環境
- Azure Cosmos DB（MongoDB API、vCore）フリーティア構成
- Application Insights + Log Analyticsワークスペース
- システム割り当てIDを持つContainer App
- サポート構成（シークレット、環境変数、スケーリングルール）

> コンテナレジストリ自体はBicepで**作成されません**。デプロイ前にGHCR（`ghcr.io/<owner>/<repo>:<tag>`）にイメージが存在する必要があります。ワークフローは既にイメージを公開し、解決された名前をBicepデプロイメントに渡します。

## デプロイ後のセットアップ：ロールの割り当て

デプロイワークフローが**自動的に**必要なAzureロールを割り当てます。手動設定は不要です。

### 自動的に割り当てられるロール

ワークフローは以下のロールをContainer AppのManaged Identityに割り当てます：

1. **閲覧者（Reader）**（リソースグループスコープ） - リソースメタデータとCosmos DB情報へのアクセス
2. **監視閲覧者（Monitoring Reader）**（リソースグループスコープ） - Azure Monitorメトリクス（CPU、メモリ、リクエスト、データ転送）へのアクセス
3. **Cost Management 閲覧者（Cost Management Reader）**（サブスクリプションスコープ） - Azure Cost Managementデータ（vCPU秒、GiB秒）へのアクセス
4. **監視閲覧者（Monitoring Reader）**（Cosmos DBスコープ） - Cosmos DB vCoreメトリクス（StorageUsed、CPU、Memory）へのアクセス

### 手動での割り当て（必要な場合）

自動割り当てが失敗した場合や手動でデプロイする場合は、以下の方法でロールを割り当てられます：

#### 方法1: Azure Portal（GUI）

1. **Azure Portal**を開く → **Container App**（`<namePrefix>-app`）に移動
2. 左メニューの**セキュリティ** → **ID**に移動
3. **システム割り当て済み**IDの**状態**が**オン**になっていることを確認（デプロイメントで自動的に有効化）
4. **Azureロールの割り当て**ボタンをクリック
5. **+ ロールの割り当ての追加**をクリックし、以下をそれぞれ追加：

   **① 閲覧者ロール**
   - スコープ：**リソース グループ**
   - サブスクリプション：サブスクリプションを選択
   - リソース グループ：`rg-<namePrefix>`
   - ロール：**閲覧者**
   - **保存**をクリック

   **② 監視閲覧者ロール**
   - スコープ：**リソース グループ**
   - サブスクリプション：サブスクリプションを選択
   - リソース グループ：`rg-<namePrefix>`
   - ロール：**監視閲覧者**
   - **保存**をクリック

   **③ Cost Management 閲覧者ロール**
   - スコープ：**サブスクリプション**
   - サブスクリプション：サブスクリプションを選択
   - ロール：**Cost Management 閲覧者**
   - **保存**をクリック

#### 方法2: Azure CLI

```bash
# Container AppのManaged IdentityのPrincipal IDを取得
PRINCIPAL_ID=$(az containerapp identity show \
  --name <namePrefix>-app \
  --resource-group rg-<namePrefix> \
  --query principalId -o tsv)

echo "Principal ID: $PRINCIPAL_ID"

# リソースグループに閲覧者ロールを割り当て
az role assignment create \
  --assignee $PRINCIPAL_ID \
  --role "Reader" \
  --resource-group rg-<namePrefix>

# リソースグループに監視閲覧者ロールを割り当て
az role assignment create \
  --assignee $PRINCIPAL_ID \
  --role "Monitoring Reader" \
  --resource-group rg-<namePrefix>

# サブスクリプションにCost Management 閲覧者ロールを割り当て
az role assignment create \
  --assignee $PRINCIPAL_ID \
  --role "Cost Management Reader" \
  --scope /subscriptions/<subscription-id>

# Cosmos DB vCoreメトリクス用に監視閲覧者を割り当て
az role assignment create \
  --assignee $PRINCIPAL_ID \
  --role "Monitoring Reader" \
  --scope /subscriptions/<subscription-id>/resourceGroups/rg-<namePrefix>/providers/Microsoft.DocumentDB/mongoClusters/<cosmos-account-name>
```

> **注意**: ロールの割り当ては**永続的**で、デプロイをまたいで保持されます。ワークフローは既存の割り当てを確認し、重複を回避します。

### 検証

ロール割り当て後、Container Appは以下にアクセスできるようになります：
- Container AppsのAzure Monitorメトリクス（Requests、TxBytes、CPU、Memory）
- Cosmos DB vCoreメトリクス（StorageUsed、CpuPercent、MemoryPercent）
- 使用状況追跡のためのAzure Cost Managementデータ

使用状況バナーとCosmos DB監視は、ロール割り当て後数分以内に動作を開始します。

## フリーティア使用状況バナー
Webアプリは、Azure Container Appsのフリーティア容量の残りを推定表示するバナーを表示できます。

- 予算はデフォルトで月間180,000 vCPU秒と360,000 GiB秒（Azureのフリーティア許容量）です。
- 割り当てられたManaged Identityでメトリクスをクエリできるように、`AzureUsage__Enabled=true`を設定してバックグラウンド収集を有効にします。
- 主要な環境変数（または`appsettings.json`）には以下が含まれます：
  - `FreeTier__Resource__VcpuPerReplica = 0.25`
  - `FreeTier__Resource__MemoryGiBPerReplica = 0.5`
  - `FreeTier__BudgetVcpuSeconds`、`FreeTier__BudgetGiBSeconds`、`FreeTier__Requests__Budget`、`FreeTier__Data__BudgetGb`

## 使用状況ガーディアン（オプション）
`scripts/usage-guardian.csx`には、予測使用量が設定したしきい値を超えた場合にContainer Appをフリーズ（min/maxレプリカを0に設定）できるAzure Functions workerが含まれています。Managed IdentityにContainer App Contributor + Monitoring Reader + Cost Management Readerを付与し、スケジュール実行（例：30分ごと）してください。

## ローカル開発
1. `FestivalManagementWeb/appsettings.Development.json`を作成：
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
2. `dotnet run --project FestivalManagementWeb`でローカルサイトを実行します。

## ロードマップ / 注意事項
- ステージングロールアウトが必要な場合は、GHCRプッシュとAzureデプロイを別々のワークフローに分割することを検討してください。
- `infra/parameters.json`は引き続き`namePrefix`、`location`、`imageTag`を制御します。ワークフローはレジストリ設定を自動的に上書きしますが、異なるGHCRリポジトリまたはタグを使用する場合は明示的な値を設定できます。
