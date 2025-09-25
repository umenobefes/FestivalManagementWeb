# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Development Commands

This is an ASP.NET Core 8.0 web application. Common commands:

```bash
# Build the application
dotnet build

# Run in development mode
dotnet run --project FestivalManagementWeb

# Build and run with hot reload
dotnet watch run --project FestivalManagementWeb

# Build for release
dotnet build -c Release

# Publish for deployment
dotnet publish -c Release

# Run tests (if any test projects exist)
dotnet test

# Docker build (from solution root)
docker build -f FestivalManagementWeb/Dockerfile -t festivalmgmt .
```

## Architecture Overview

### Core Application Structure
- **Framework**: ASP.NET Core 8.0 MVC with Razor views
- **Database**: MongoDB (via Cosmos DB with MongoDB API)
- **Authentication**: ASP.NET Identity with MongoDB storage + Google OAuth
- **Deployment**: Azure Container Apps with cost monitoring

### Key Domain Concepts

**Year-Based Data Segregation**: The application uses a year-based branching system where all data entities inherit from `BaseModel` containing a `Year` property. This allows switching between different years' data sets.

**Key-Value Storage System**:
- `TextKeyValue`: Stores string-based configuration/content
- `ImageKeyValue`: Stores images via MongoDB GridFS
- Both support deployment tracking with `Deployed` and `DeployedDate` fields

### Service Architecture

**Repository Pattern**:
- `ITextKeyValueRepository` / `TextKeyValueRepository`
- `IImageKeyValueRepository` / `ImageKeyValueRepository`
- Both implement year-aware CRUD operations with MongoDB

**Core Services**:
- `IFreeTierService`: Azure Container Apps cost monitoring and banner display
- `IAzureUsageProvider`: Collects real-time Azure metrics (CPU, memory, requests, data transfer)
- `ICosmosFreeTierProvider`: Monitors Cosmos DB free tier usage
- `IGitService`: Git operations for deployment tracking
- `IYearBranchService`: Year-based data switching functionality
- `IRequestQuotaService`: Daily request limiting

**Background Services**:
- `AutoUsageRefreshHostedService`: Periodically refreshes Azure usage metrics
- `RequestQuotaMiddleware`: Enforces daily request limits

### Configuration Requirements

**Essential appsettings.json sections**:
- `MongoDbSettings`: Connection string and database name for Cosmos DB
- `FreeTier`: Azure Container Apps cost monitoring settings
- `AzureUsage`: Auto-collection of Azure metrics (optional but recommended)
- `Authentication:Google`: Google OAuth credentials
- `InitialUser`: Seed admin user email
- `GitSettings`: Git repository configuration for deployment tracking

### Azure Integration Features

**Free Tier Monitoring**: The app displays a banner showing estimated remaining Azure Container Apps free tier hours, calculated from:
- Monthly budgets: 180,000 vCPU-seconds, 360,000 GiB-seconds
- Configurable resource allocation per replica
- Real-time usage collection from Azure APIs

**Cost Management**:
- Automatic scaling to zero when idle (configurable cooldown)
- Request quota enforcement to prevent overages
- Integration with GitHub Actions for usage-based deployment freezing

### Data Access Patterns

**MongoDB Operations**:
- Uses compound indexes on `Year + Key` for efficient year-based queries
- GridFS for image storage with `ObjectId` references
- Automatic year assignment for legacy data migration
- Unique constraints enforced at database level

### Development Notes

- The app uses Japanese display names in data annotations (`[Display(Name = "キー")]`)
- All controllers inherit standard MVC patterns with dependency injection
- View components are used for reusable UI elements (banner display)
- Middleware runs early in pipeline to enforce quotas before serving static files

### Deployment Architecture

- Containerized via Docker with multi-stage builds
- Deployed to Azure Container Apps consumption plan
- GitHub Actions workflows for CI/CD with usage monitoring
- Auto-provision Cosmos DB with configurable throughput
- Managed Identity for secure Azure API access

### Git Integration

The application includes git-based deployment tracking:
- Records deployment status per key-value pair
- Integrates with remote repositories via LibGit2Sharp
- Tracks deployment timestamps for audit trails