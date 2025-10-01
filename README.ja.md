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

初回デプロイ後、使用状況監視とコスト追跡を有効にするために、Container AppのManaged Identityに**Azureロールを手動で割り当てる**必要があります。

### 必要なロール

Container AppがAzureメトリクスとコストデータをクエリするには、以下のロールが必要です：

1. **閲覧者（Reader）** - リソースメタデータとCosmos DB情報へのアクセス
2. **監視閲覧者（Monitoring Reader）** - Azure Monitorメトリクス（CPU、メモリ、リクエスト、データ転送）へのアクセス
3. **Cost Management 閲覧者（Cost Management Reader）** - Azure Cost Managementデータ（vCPU秒、GiB秒）へのアクセス

### 割り当て手順

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

> **注意**: これらのロールの割り当ては**永続的**で、一度だけ設定すれば済みます。今後のデプロイメントではこのステップは不要です。

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
