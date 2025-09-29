using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using FestivalManagementWeb.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Linq;

namespace FestivalManagementWeb.Services
{
    public class CosmosFreeTierProvider : ICosmosFreeTierProvider, IDisposable
    {
        private const string ManagementEndpoint = "https://management.azure.com";
        private const string AccountApiVersion = "2024-05-15";
        private const string MetricsApiVersion = "2018-01-01";

        private readonly IOptionsMonitor<FreeTierSettings> _freeTierOptions;
        private readonly ILogger<CosmosFreeTierProvider> _logger;
        private readonly HttpClient _httpClient;
        private readonly TokenCredential _credential;
        private bool _disposed;

        public CosmosFreeTierProvider(IOptionsMonitor<FreeTierSettings> freeTierOptions, ILogger<CosmosFreeTierProvider> logger)
        {
            _freeTierOptions = freeTierOptions;
            _logger = logger;
            _httpClient = new HttpClient();
            _credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeInteractiveBrowserCredential = true,
                ExcludeVisualStudioCredential = true,
                ExcludeVisualStudioCodeCredential = true,
                ExcludeSharedTokenCacheCredential = true,
                ExcludeAzureCliCredential = false,
                ExcludeEnvironmentCredential = false
            });
        }

        public async Task<CosmosFreeTierStatus> GetStatusAsync(CancellationToken ct)
        {
            var settings = _freeTierOptions.CurrentValue?.Cosmos;
            if (settings == null || !settings.Enabled)
            {
                return CosmosFreeTierStatus.Disabled();
            }

            var status = new CosmosFreeTierStatus
            {
                Enabled = true,
                AccountName = settings.AccountName,
                DatabaseName = settings.DatabaseName,
                Provisioning = settings.Provisioning,
                FreeTierStorageLimitGb = settings.Provisioning == CosmosProvisioningModel.VCore
                    ? settings.FreeTierVCoreStorageGb
                    : settings.FreeTierStorageGb,
                CheckedAtUtc = DateTime.UtcNow
            };

            string accountResourceId;
            try
            {
                accountResourceId = ResolveAccountResourceId(settings);
            }
            catch (Exception ex)
            {
                status.Error = ex.Message;
                status.AddIssue(ex.Message);
                return status;
            }

            status.AccountResourceId = accountResourceId;

            try
            {
                var token = await GetTokenAsync(ct).ConfigureAwait(false);
                await PopulateStatusAsync(status, settings, accountResourceId, token, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                status.Error = ex.Message;
                status.AddIssue(ex.Message);
                _logger.LogWarning(ex, "Cosmos free-tier check failed");
            }

            return status;
        }

        private static string ResolveAccountResourceId(CosmosFreeTierSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.AccountResourceId))
            {
                return settings.AccountResourceId!;
            }

            var subscriptionId = settings.SubscriptionOverride ?? settings.SubscriptionId;
            if (string.IsNullOrWhiteSpace(subscriptionId) || string.IsNullOrWhiteSpace(settings.ResourceGroup) || string.IsNullOrWhiteSpace(settings.AccountName))
            {
                throw new InvalidOperationException("Cosmos free-tier check requires AccountResourceId or SubscriptionId/ResourceGroup/AccountName.");
            }

            var resourceType = settings.Provisioning == CosmosProvisioningModel.VCore ? "mongoClusters" : "databaseAccounts";
            return $"/subscriptions/{subscriptionId}/resourceGroups/{settings.ResourceGroup}/providers/Microsoft.DocumentDB/{resourceType}/{settings.AccountName}";
        }

        private async Task PopulateStatusAsync(CosmosFreeTierStatus status, CosmosFreeTierSettings settings, string accountResourceId, string token, CancellationToken ct)
        {
            using var accountDoc = await GetJsonAsync($"{ManagementEndpoint}{accountResourceId}?api-version={AccountApiVersion}", token, ct).ConfigureAwait(false);
            if (accountDoc == null)
            {
                status.Error = "Cosmos DB account metadata not found.";
                status.AddIssue(status.Error);
                return;
            }

            var root = accountDoc.RootElement;
            ParseAccountMetadata(status, root);

            if (status.ApiIsMongo == false)
            {
                status.AddIssue("Cosmos DB account is not configured for the MongoDB API.");
            }

            if (status.FreeTierActive == false)
            {
                status.AddIssue("Free tier is not enabled on the Cosmos DB account.");
            }

            await PopulateStorageAsync(status, settings, accountResourceId, token, ct).ConfigureAwait(false);

            status.OverallWithinFreeTier = status.FreeTierActive != false
                                           && status.WithinStorageLimit != false;

            if (status.ShouldStop)
            {
                status.ShouldWarn = true;
            }
        }

        private static void ParseAccountMetadata(CosmosFreeTierStatus status, JsonElement root)
        {
            if (status.ApiIsMongo != true && status.Provisioning == CosmosProvisioningModel.VCore)
            {
                status.ApiIsMongo = true;
            }

            if (root.TryGetProperty("properties", out var properties))
            {
                if (properties.TryGetProperty("enableFreeTier", out var enableFreeTier) && enableFreeTier.ValueKind == JsonValueKind.True)
                {
                    status.FreeTierActive = true;
                }
                else if (properties.TryGetProperty("enableFreeTier", out enableFreeTier) && enableFreeTier.ValueKind == JsonValueKind.False)
                {
                    status.FreeTierActive = false;
                }
                else if (properties.TryGetProperty("isFreeTierAccount", out var isFreeTierAccount))
                {
                    if (isFreeTierAccount.ValueKind == JsonValueKind.True) status.FreeTierActive = true;
                    if (isFreeTierAccount.ValueKind == JsonValueKind.False) status.FreeTierActive = false;
                }

                if (properties.TryGetProperty("capabilities", out var capabilities) && capabilities.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cap in capabilities.EnumerateArray())
                    {
                        if (cap.ValueKind != JsonValueKind.Object) continue;
                        if (cap.TryGetProperty("name", out var nameElement))
                        {
                            var name = nameElement.GetString();
                            if (!string.IsNullOrWhiteSpace(name) && name.IndexOf("Mongo", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                status.ApiIsMongo = true;
                                break;
                            }
                        }
                    }
                }

                if (status.FreeTierActive == null && status.Provisioning == CosmosProvisioningModel.VCore && properties.TryGetProperty("compute", out var compute) && compute.ValueKind == JsonValueKind.Object)
                {
                    if (compute.TryGetProperty("tier", out var tierElement) && tierElement.ValueKind == JsonValueKind.String)
                    {
                        var tier = tierElement.GetString();
                        if (!string.IsNullOrWhiteSpace(tier) && tier.Equals("Free", StringComparison.OrdinalIgnoreCase))
                        {
                            status.FreeTierActive = true;
                        }
                    }
                }
            }

            if (status.ApiIsMongo != true && root.TryGetProperty("kind", out var kindElement))
            {
                var kind = kindElement.GetString();
                if (!string.IsNullOrWhiteSpace(kind) && kind.IndexOf("Mongo", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    status.ApiIsMongo = true;
                }
            }
        }

        private async Task PopulateStorageAsync(CosmosFreeTierStatus status, CosmosFreeTierSettings settings, string accountResourceId, string token, CancellationToken ct)
        {
            var storageGb = await GetStorageGbAsync(accountResourceId, settings, token, ct).ConfigureAwait(false);
            status.StorageGb = storageGb;

            if (!storageGb.HasValue || storageGb.Value < 0)
            {
                status.Warning ??= "Cosmos storage metric unavailable.";
                return;
            }

            if (status.FreeTierStorageLimitGb > 0)
            {
                status.StoragePercentOfLimit = Math.Round((storageGb.Value / status.FreeTierStorageLimitGb) * 100, 2);
                status.WithinStorageLimit = storageGb.Value <= status.FreeTierStorageLimitGb + 1e-6;

                if (status.WithinStorageLimit == false)
                {
                    status.ShouldStop = true;
                    status.AddIssue($"Account storage {storageGb.Value:F2} GB exceeds free-tier allowance {status.FreeTierStorageLimitGb:F2} GB.");
                }
                else if (settings.WarnStoragePercent.HasValue && status.StoragePercentOfLimit.HasValue && status.StoragePercentOfLimit.Value >= settings.WarnStoragePercent.Value)
                {
                    status.ShouldWarn = true;
                    status.AddIssue($"Account storage is at {status.StoragePercentOfLimit}% of the free-tier allowance.");
                }
            }
        }

        private async Task<JsonDocument?> GetJsonAsync(string requestUri, string token, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        }

        private async Task<double?> GetStorageGbAsync(string accountResourceId, CosmosFreeTierSettings settings, string token, CancellationToken ct)
        {
            var end = DateTime.UtcNow;
            var start = end.AddHours(-6);
            var timespan = Uri.EscapeDataString($"{start:O}/{end:O}");
            var isVCore = settings.Provisioning == CosmosProvisioningModel.VCore;
            var defaultNamespace = isVCore ? "microsoft.documentdb/mongoClusters" : "microsoft.documentdb/databaseaccounts";
            var metricNamespace = string.IsNullOrWhiteSpace(settings.MetricNamespace)
                ? defaultNamespace
                : settings.MetricNamespace;
            var metricName = isVCore ? "StorageUsed" : "TotalAccountStorage";

            var uri = $"{ManagementEndpoint}{accountResourceId}/providers/microsoft.insights/metrics?metricnames={metricName}&aggregation=Maximum&timespan={timespan}&interval=PT1H&metricnamespace={Uri.EscapeDataString(metricNamespace)}&api-version={MetricsApiVersion}";

            using var metricsDoc = await GetJsonAsync(uri, token, ct).ConfigureAwait(false);
            if (metricsDoc == null)
            {
                return null;
            }

            if (metricsDoc.RootElement.TryGetProperty("value", out var metricsArray))
            {
                foreach (var metric in metricsArray.EnumerateArray())
                {
                    if (!metric.TryGetProperty("timeseries", out var seriesArray)) continue;
                    foreach (var series in seriesArray.EnumerateArray())
                    {
                        if (!series.TryGetProperty("data", out var dataArray)) continue;
                        foreach (var data in dataArray.EnumerateArray().Reverse())
                        {
                            double? candidate = null;
                            if (data.TryGetProperty("maximum", out var maximum) && maximum.ValueKind == JsonValueKind.Number)
                            {
                                candidate = maximum.GetDouble();
                            }
                            else if (data.TryGetProperty("total", out var total) && total.ValueKind == JsonValueKind.Number)
                            {
                                candidate = total.GetDouble();
                            }

                            if (candidate.HasValue)
                            {
                                return candidate.Value / 1_073_741_824d;
                            }
                        }
                    }
                }
            }

            return null;
        }

        private async Task<string> GetTokenAsync(CancellationToken ct)
        {
            var token = await _credential.GetTokenAsync(new TokenRequestContext(new[] { "https://management.azure.com/.default" }), ct).ConfigureAwait(false);
            return token.Token;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _httpClient.Dispose();
            _disposed = true;
        }
    }
}
