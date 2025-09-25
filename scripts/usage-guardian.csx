#r "nuget: Azure.Identity, 1.12.0"
#r "nuget: Azure.ResourceManager, 1.14.0"
#r "nuget: Azure.ResourceManager.AppContainers, 1.1.0"
#r "nuget: Azure.Monitor.Query, 1.3.0"
#r "nuget: System.Text.Json, 8.0.3"

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;

const string ManagementEndpoint = "https://management.azure.com";
const string ManagementScope = "https://management.azure.com/.default";
const string AccountApiVersion = "2024-05-15";
const string MetricsApiVersion = "2018-01-01";

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

await RunAsync();

static async Task RunAsync()
{
    string RequireEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            Console.Error.WriteLine($"Environment variable '{name}' is required.");
            Environment.Exit(1);
        }
        return value!;
    }

    string? OptionalEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    double GetDouble(string name)
    {
        var raw = RequireEnv(name);
        if (!double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value))
        {
            Console.Error.WriteLine($"Environment variable '{name}' must be numeric.");
            Environment.Exit(1);
        }
        return value;
    }

    double? GetOptionalDouble(string name)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        if (!double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value))
        {
            Console.Error.WriteLine($"Environment variable '{name}' must be numeric if provided.");
            Environment.Exit(1);
        }
        return value;
    }

    var subscriptionId = RequireEnv("SUBSCRIPTION_ID");
    var resourceGroup = RequireEnv("RESOURCE_GROUP");
    var appName = RequireEnv("APP_NAME");

    double budgetCpuSeconds = GetDouble("BUDGET_VCPU_SECONDS");
    double budgetGibSeconds = GetDouble("BUDGET_GIB_SECONDS");
    double budgetRequests = GetDouble("BUDGET_REQUESTS");
    double budgetDataGb = GetDouble("BUDGET_DATA_GB");
    double warnPct = GetDouble("WARN_PCT");
    double stopPct = GetDouble("STOP_PCT");
    double? remainStopPct = GetOptionalDouble("REMAIN_STOP_PCT");
    double? remainWarnPct = GetOptionalDouble("REMAIN_WARN_PCT");
    bool disableIngress = string.Equals(Environment.GetEnvironmentVariable("DISABLE_INGRESS_ON_STOP"), "true", StringComparison.OrdinalIgnoreCase);

    var cosmosAccount = OptionalEnv("COSMOS_ACCOUNT_NAME");
    var cosmosResourceGroup = OptionalEnv("COSMOS_RESOURCE_GROUP") ?? resourceGroup;
    var cosmosSubscription = OptionalEnv("COSMOS_SUBSCRIPTION_ID") ?? subscriptionId;
    var cosmosDatabase = OptionalEnv("COSMOS_DATABASE_NAME");
    var cosmosCollectionsRaw = OptionalEnv("COSMOS_COLLECTION_NAMES");
    var cosmosProvisioning = OptionalEnv("COSMOS_PROVISIONING") ?? "vCore";
    double cosmosFreeRuLimit = GetOptionalDouble("COSMOS_FREE_RU_LIMIT") ?? 1000;
    double cosmosFreeStorageGb = GetOptionalDouble("COSMOS_FREE_STORAGE_GB") ?? 32;
    double cosmosWarnRuPct = GetOptionalDouble("COSMOS_WARN_RU_PCT") ?? warnPct;
    double cosmosStopRuPct = GetOptionalDouble("COSMOS_STOP_RU_PCT") ?? stopPct;
    double cosmosWarnStoragePct = GetOptionalDouble("COSMOS_WARN_STORAGE_PCT") ?? warnPct;
    double cosmosStopStoragePct = GetOptionalDouble("COSMOS_STOP_STORAGE_PCT") ?? stopPct;

    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ExcludeAzureCliCredential = false,
        ExcludeInteractiveBrowserCredential = true,
        ExcludeSharedTokenCacheCredential = true,
        ExcludeVisualStudioCodeCredential = true,
        ExcludeVisualStudioCredential = true
    });

    using var httpClient = new HttpClient();

    var armClient = new ArmClient(credential, subscriptionId);
    var containerAppId = ContainerAppResource.CreateResourceIdentifier(subscriptionId, resourceGroup, appName);

    ContainerAppResource containerApp;
    try
    {
        var response = await armClient.GetContainerAppResource(containerAppId).GetAsync();
        containerApp = response.Value;
    }
    catch (RequestFailedException ex) when (ex.Status == 404)
    {
        Console.Error.WriteLine($"Container App '{appName}' not found in resource group '{resourceGroup}'.");
        Environment.Exit(1);
        return;
    }

    string resourceId = containerApp.Data.Id;

    DateTime utcNow = DateTime.UtcNow;
    DateTime start = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    var metricsClient = new MetricsQueryClient(credential);
    var metricsResponse = await metricsClient.QueryResourceAsync(
        resourceId,
        new[] { "Requests", "TxBytes" },
        new MetricsQueryOptions
        {
            Granularity = TimeSpan.FromHours(1),
            TimeRange = new QueryTimeRange(start, utcNow),
            Aggregations = { MetricAggregationType.Total }
        });

    MetricsQueryResult metricsResult = metricsResponse.Value;

    double SumMetric(string metricName)
    {
        var metric = metricsResult.Metrics.FirstOrDefault(m => string.Equals(m.Name, metricName, StringComparison.OrdinalIgnoreCase));
        if (metric == null)
        {
            return 0;
        }

        return metric.TimeSeries
            .SelectMany(ts => ts.Values)
            .Where(point => point.Total.HasValue)
            .Sum(point => point.Total!.Value);
    }

    double requestsUsed = SumMetric("Requests");
    double txBytes = SumMetric("TxBytes");

    Console.WriteLine($"MTD Requests={requestsUsed}, TxBytes={txBytes}");

    var costClient = new MetricsQueryClient(credential);
    var costResponse = await costClient.QueryResourceAsync(
        resourceId,
        new[] { "CpuUsage", "MemoryUsage" },
        new MetricsQueryOptions
        {
            Granularity = TimeSpan.FromHours(1),
            TimeRange = new QueryTimeRange(start, utcNow),
            Aggregations = { MetricAggregationType.Total }
        });

    MetricsQueryResult costResult = costResponse.Value;

    double SumCost(string metricName)
    {
        var metric = costResult.Metrics.FirstOrDefault(m => string.Equals(m.Name, metricName, StringComparison.OrdinalIgnoreCase));
        if (metric == null)
        {
            return 0;
        }

        return metric.TimeSeries
            .SelectMany(ts => ts.Values)
            .Where(point => point.Total.HasValue)
            .Sum(point => point.Total!.Value);
    }

    double vcpuSeconds = SumCost("CpuUsage");
    double gibSeconds = SumCost("MemoryUsage");

    Console.WriteLine($"MTD vCPU-s={vcpuSeconds}, GiB-s={gibSeconds}");

    int day = utcNow.Day;
    int daysInMonth = DateTime.DaysInMonth(utcNow.Year, utcNow.Month);
    int remainingDays = Math.Max(1, daysInMonth - day + 1);

    double ProjectedPct(double used, double budget)
    {
        if (budget <= 0)
        {
            return 0;
        }

        double daily = used / Math.Max(day, 1);
        double projected = used + (daily * remainingDays);
        return Math.Round(projected * 100 / budget, 2);
    }

    double RemainingPct(double used, double budget)
    {
        if (budget <= 0)
        {
            return 0;
        }

        double usedPct = Math.Clamp((used / budget) * 100, 0, 100);
        return Math.Round(100 - usedPct, 2);
    }

    double cpuProj = ProjectedPct(vcpuSeconds, budgetCpuSeconds);
    double memProj = ProjectedPct(gibSeconds, budgetGibSeconds);
    double reqProj = ProjectedPct(requestsUsed, budgetRequests);
    double dataGb = Math.Round(txBytes / 1_000_000_000d, 3);
    double dataProj = ProjectedPct(dataGb, budgetDataGb);

    Console.WriteLine($"Proj% CPU={cpuProj} MEM={memProj} REQ={reqProj} DATA={dataProj} (remDays={remainingDays})");

    double cpuRemain = RemainingPct(vcpuSeconds, budgetCpuSeconds);
    double memRemain = RemainingPct(gibSeconds, budgetGibSeconds);
    double reqRemain = RemainingPct(requestsUsed, budgetRequests);
    double dataRemain = RemainingPct(dataGb, budgetDataGb);

    Console.WriteLine($"Remain% CPU={cpuRemain} MEM={memRemain} REQ={reqRemain} DATA={dataRemain}");

    bool shouldStop =
        cpuProj >= stopPct ||
        memProj >= stopPct ||
        reqProj >= stopPct ||
        dataProj >= stopPct ||
        (remainStopPct.HasValue && (
            cpuRemain <= remainStopPct.Value ||
            memRemain <= remainStopPct.Value ||
            reqRemain <= remainStopPct.Value ||
            dataRemain <= remainStopPct.Value));

    bool shouldWarn =
        cpuProj >= warnPct ||
        memProj >= warnPct ||
        reqProj >= warnPct ||
        dataProj >= warnPct ||
        (remainWarnPct.HasValue && (
            cpuRemain <= remainWarnPct.Value ||
            memRemain <= remainWarnPct.Value ||
            reqRemain <= remainWarnPct.Value ||
            dataRemain <= remainWarnPct.Value));

    CosmosStatus? cosmosStatus = null;
    if (!string.IsNullOrWhiteSpace(cosmosAccount))
    {
        var cosmosCollections = string.IsNullOrWhiteSpace(cosmosCollectionsRaw)
            ? Array.Empty<string>()
            : cosmosCollectionsRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        cosmosStatus = await CheckCosmosAsync(
            credential,
            httpClient,
            cosmosSubscription!,
            cosmosResourceGroup,
            cosmosAccount!,
            cosmosDatabase,
            cosmosCollections,
            cosmosProvisioning,
            cosmosFreeRuLimit,
            cosmosFreeStorageGb,
            cosmosWarnRuPct,
            cosmosStopRuPct,
            cosmosWarnStoragePct,
            cosmosStopStoragePct,
            utcNow);

        foreach (var line in cosmosStatus.LogLines)
        {
            Console.WriteLine(line);
        }

        if (cosmosStatus.ShouldStop)
        {
            shouldStop = true;
        }
        else if (cosmosStatus.ShouldWarn)
        {
            shouldWarn = true;
        }
    }
    else
    {
        Console.WriteLine("Cosmos check skipped (COSMOS_ACCOUNT_NAME not set).");
    }

    if (shouldStop)
    {
        Console.WriteLine("Threshold reached: freezing Container App");

        var properties = new Dictionary<string, object?>
        {
            ["template"] = new Dictionary<string, object?>
            {
                ["scale"] = new Dictionary<string, object?>
                {
                    ["minReplicas"] = 0,
                    ["maxReplicas"] = 0
                }
            }
        };

        if (disableIngress)
        {
            properties["configuration"] = new Dictionary<string, object?>
            {
                ["ingress"] = null
            };
        }

        var patch = new Dictionary<string, object?>
        {
            ["properties"] = properties
        };

        string patchJson = JsonSerializer.Serialize(patch, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        await containerApp.UpdateAsync(WaitUntil.Completed, BinaryData.FromString(patchJson));
    }
    else if (shouldWarn)
    {
        Console.WriteLine("Warning threshold reached. No action taken.");
    }
    else
    {
        Console.WriteLine("Within limits.");
    }
}

static async Task<CosmosStatus> CheckCosmosAsync(
    TokenCredential credential,
    HttpClient http,
    string subscriptionId,
    string resourceGroup,
    string accountName,
    string? databaseName,
    IReadOnlyCollection<string> collectionNames,
    string provisioning,
    double freeRuLimit,
    double freeStorageGb,
    double warnRuPct,
    double stopRuPct,
    double warnStoragePct,
    double stopStoragePct,
    DateTime utcNow)
{
    var status = new CosmosStatus();
    var normalizedProvisioning = provisioning?.Trim() ?? "vCore";
    var resourceType = string.Equals(normalizedProvisioning, "vCore", StringComparison.OrdinalIgnoreCase)
        ? "Microsoft.DocumentDB/mongoClusters"
        : "Microsoft.DocumentDB/databaseAccounts";
    var accountResourceId = $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/{resourceType}/{accountName}";

    string token;
    try
    {
        token = (await credential.GetTokenAsync(new TokenRequestContext(new[] { ManagementScope }), CancellationToken.None)).Token;
    }
    catch (Exception ex)
    {
        status.ShouldWarn = true;
        status.AddLine($"Cosmos token acquisition failed: {ex.Message}");
        return status;
    }

    using var accountDoc = await GetJsonAsync(http, $"{ManagementEndpoint}{accountResourceId}?api-version={AccountApiVersion}", token);
    if (accountDoc == null)
    {
        status.ShouldWarn = true;
        status.AddLine("Cosmos DB account metadata not found.");
        return status;
    }

    var root = accountDoc.RootElement;
    if (root.TryGetProperty("properties", out var props))
    {
        if (props.TryGetProperty("enableFreeTier", out var enable) && enable.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            status.FreeTierActive = enable.GetBoolean();
        }
        else if (props.TryGetProperty("isFreeTierAccount", out var isFree) && isFree.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            status.FreeTierActive = isFree.GetBoolean();
        }

        if (props.TryGetProperty("capabilities", out var capabilities) && capabilities.ValueKind == JsonValueKind.Array)
        {
            foreach (var capability in capabilities.EnumerateArray())
            {
                if (capability.TryGetProperty("name", out var nameElement))
                {
                    var name = nameElement.GetString();
                    if (!string.IsNullOrWhiteSpace(name) && name.Contains("Mongo", StringComparison.OrdinalIgnoreCase))
                    {
                        status.ApiIsMongo = true;
                        break;
                    }
                }
            }
        }
    }

    if (!status.ApiIsMongo && root.TryGetProperty("kind", out var kindElement))
    {
        var kind = kindElement.GetString();
        if (!string.IsNullOrWhiteSpace(kind) && kind.Contains("Mongo", StringComparison.OrdinalIgnoreCase))
        {
            status.ApiIsMongo = true;
        }
    }

    status.AddLine($"Cosmos account: {accountName} (free-tier active={status.FreeTierActive}, apiMongo={status.ApiIsMongo})");

    if (!status.FreeTierActive)
    {
        status.ShouldWarn = true;
        status.AddLine("Cosmos free tier is not enabled on this account.");
    }

    if (!status.ApiIsMongo)
    {
        status.ShouldWarn = true;
        status.AddLine("Cosmos account is not configured for the MongoDB API.");
    }

    if (string.Equals(normalizedProvisioning, "RequestUnits", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(databaseName))
    {
        double totalRu = 0;
        bool observed = false;

        var dbThroughput = await TryGetThroughputAsync(http, $"{ManagementEndpoint}{accountResourceId}/mongodbDatabases/{databaseName}/throughputSettings/default?api-version={AccountApiVersion}", token);
        if (dbThroughput.HasValue)
        {
            totalRu += dbThroughput.Value;
            observed = true;
            status.AddLine($"Cosmos database throughput: {dbThroughput.Value} RU/s");
        }

        var names = new HashSet<string>(collectionNames, StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0)
        {
            var resolved = await GetCollectionNamesAsync(http, $"{ManagementEndpoint}{accountResourceId}/mongodbDatabases/{databaseName}/collections?api-version={AccountApiVersion}", token);
            foreach (var n in resolved)
            {
                names.Add(n);
            }
        }

        foreach (var coll in names)
        {
            var throughput = await TryGetThroughputAsync(http, $"{ManagementEndpoint}{accountResourceId}/mongodbDatabases/{databaseName}/collections/{coll}/throughputSettings/default?api-version={AccountApiVersion}", token);
            if (throughput.HasValue)
            {
                totalRu += throughput.Value;
                observed = true;
                status.AddLine($"Cosmos collection throughput: {coll} -> {throughput.Value} RU/s");
            }
        }

        if (observed)
        {
            status.ProvisionedRu = totalRu;
            if (freeRuLimit > 0)
            {
                status.RuPercent = Math.Round((totalRu / freeRuLimit) * 100, 2);
                if (status.RuPercent >= stopRuPct)
                {
                    status.ShouldStop = true;
                    status.AddLine($"Cosmos RU usage {status.RuPercent}% exceeds stop threshold {stopRuPct}%.");
                }
                else if (status.RuPercent >= warnRuPct)
                {
                    status.ShouldWarn = true;
                    status.AddLine($"Cosmos RU usage {status.RuPercent}% exceeds warn threshold {warnRuPct}%.");
                }
            }
        }
        else
        {
            status.AddLine("Cosmos throughput not reported (shared throughput only).");
        }
    }
    else if (!string.IsNullOrWhiteSpace(databaseName))
    {
        status.AddLine($"Cosmos provisioning '{normalizedProvisioning}' does not support RU check.");
    }
    else
    {
        status.AddLine("Cosmos throughput check skipped (DatabaseName not set).");
    }

    var metricNamespace = string.Equals(normalizedProvisioning, "vCore", StringComparison.OrdinalIgnoreCase)
        ? "microsoft.documentdb/mongoclusters"
        : "microsoft.documentdb/databaseaccounts";
    var storageGb = await GetStorageGbAsync(http, accountResourceId, token, utcNow, metricNamespace);
    if (storageGb.HasValue)
    {
        status.StorageGb = storageGb.Value;
        status.AddLine($"Cosmos storage: {storageGb.Value:F2} GB");
        if (freeStorageGb > 0)
        {
            status.StoragePercent = Math.Round((storageGb.Value / freeStorageGb) * 100, 2);
            if (status.StoragePercent >= stopStoragePct)
            {
                status.ShouldStop = true;
                status.AddLine($"Cosmos storage usage {status.StoragePercent}% exceeds stop threshold {stopStoragePct}%.");
            }
            else if (status.StoragePercent >= warnStoragePct)
            {
                status.ShouldWarn = true;
                status.AddLine($"Cosmos storage usage {status.StoragePercent}% exceeds warn threshold {warnStoragePct}%.");
            }
        }
    }
    else
    {
        status.AddLine("Cosmos storage metric unavailable.");
    }

    if (status.ShouldStop)
    {
        status.ShouldWarn = true; // surface warning alongside stop action
    }

    return status;
}

static async Task<JsonDocument?> GetJsonAsync(HttpClient http, string requestUri, string token)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    using var response = await http.SendAsync(request, CancellationToken.None);
    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return null;
    }

    response.EnsureSuccessStatusCode();
    var stream = await response.Content.ReadAsStreamAsync(CancellationToken.None);
    return await JsonDocument.ParseAsync(stream, cancellationToken: CancellationToken.None);
}

static async Task<double?> TryGetThroughputAsync(HttpClient http, string requestUri, string token)
{
    using var doc = await GetJsonAsync(http, requestUri, token);
    if (doc == null)
    {
        return null;
    }

    if (doc.RootElement.TryGetProperty("properties", out var props) &&
        props.TryGetProperty("resource", out var resource) &&
        resource.TryGetProperty("throughput", out var throughputElement) &&
        throughputElement.ValueKind == JsonValueKind.Number)
    {
        return throughputElement.GetDouble();
    }

    return null;
}

static async Task<IReadOnlyCollection<string>> GetCollectionNamesAsync(HttpClient http, string requestUri, string token)
{
    using var doc = await GetJsonAsync(http, requestUri, token);
    if (doc == null)
    {
        return Array.Empty<string>();
    }

    if (!doc.RootElement.TryGetProperty("value", out var valueArray))
    {
        return Array.Empty<string>();
    }

    var list = new List<string>();
    foreach (var item in valueArray.EnumerateArray())
    {
        if (item.TryGetProperty("properties", out var props) &&
            props.TryGetProperty("resource", out var resource) &&
            resource.TryGetProperty("id", out var idElement))
        {
            var id = idElement.GetString();
            if (!string.IsNullOrWhiteSpace(id))
            {
                list.Add(id);
            }
        }
    }

    return list;
}

static async Task<double?> GetStorageGbAsync(HttpClient http, string accountResourceId, string token, DateTime utcNow, string metricNamespace)
{
    var start = utcNow.AddHours(-6).ToString("O");
    var end = utcNow.ToString("O");
    var timespan = Uri.EscapeDataString($"{start}/{end}");
    var requestUri = $"{ManagementEndpoint}{accountResourceId}/providers/microsoft.insights/metrics?metricnames=TotalAccountStorage&aggregation=Maximum&timespan={timespan}&interval=PT1H&metricnamespace={Uri.EscapeDataString(metricNamespace)}&api-version={MetricsApiVersion}";

    using var doc = await GetJsonAsync(http, requestUri, token);
    if (doc == null)
    {
        return null;
    }

    if (!doc.RootElement.TryGetProperty("value", out var metricsArray))
    {
        return null;
    }

    foreach (var metric in metricsArray.EnumerateArray())
    {
        if (!metric.TryGetProperty("timeseries", out var seriesArray))
        {
            continue;
        }

        foreach (var series in seriesArray.EnumerateArray())
        {
            if (!series.TryGetProperty("data", out var dataArray))
            {
                continue;
            }

            foreach (var point in dataArray.EnumerateArray().Reverse())
            {
                if (point.TryGetProperty("maximum", out var maxElement) && maxElement.ValueKind == JsonValueKind.Number)
                {
                    return maxElement.GetDouble() / 1_073_741_824d;
                }
                if (point.TryGetProperty("total", out var totalElement) && totalElement.ValueKind == JsonValueKind.Number)
                {
                    return totalElement.GetDouble() / 1_073_741_824d;
                }
            }
        }
    }

    return null;
}

sealed class CosmosStatus
{
    private readonly List<string> _lines = new();

    public bool FreeTierActive { get; set; }
    public bool ApiIsMongo { get; set; }
    public double? ProvisionedRu { get; set; }
    public double? StorageGb { get; set; }
    public double? RuPercent { get; set; }
    public double? StoragePercent { get; set; }
    public bool ShouldWarn { get; set; }
    public bool ShouldStop { get; set; }
    public IReadOnlyList<string> LogLines => _lines;

    public void AddLine(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            _lines.Add($"Cosmos: {message}");
        }
    }
}
