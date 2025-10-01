using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using FestivalManagementWeb.Models;
using Microsoft.Extensions.Options;

namespace FestivalManagementWeb.Services
{
    public class AzureUsageProvider : IAzureUsageProvider
    {
        private readonly IOptionsMonitor<AzureUsageSettings> _options;
        private readonly HttpClient _http = new HttpClient();

        public AzureUsageProvider(IOptionsMonitor<AzureUsageSettings> options)
        {
            _options = options;
        }

        private string GetResourceId()
        {
            var o = _options.CurrentValue;
            if (!string.IsNullOrWhiteSpace(o.ContainerAppResourceId)) return o.ContainerAppResourceId!;
            if (string.IsNullOrWhiteSpace(o.SubscriptionId) || string.IsNullOrWhiteSpace(o.ResourceGroup) || string.IsNullOrWhiteSpace(o.ContainerAppName))
                throw new InvalidOperationException("AzureUsage: SubscriptionId/ResourceGroup/ContainerAppName are required or provide ContainerAppResourceId.");
            return $"/subscriptions/{o.SubscriptionId}/resourceGroups/{o.ResourceGroup}/providers/Microsoft.App/containerApps/{o.ContainerAppName}";
        }

        private static (DateTime startUtc, DateTime endUtc) MonthToDate()
        {
            var now = DateTime.UtcNow;
            var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            return (start, now);
        }

        private async Task<string> GetTokenAsync(string scope, CancellationToken ct)
        {
            var cred = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeManagedIdentityCredential = false,
                ExcludeEnvironmentCredential = false,
                ExcludeSharedTokenCacheCredential = true,
                ExcludeInteractiveBrowserCredential = true
            });
            var token = await cred.GetTokenAsync(new TokenRequestContext(new[] { scope }), ct);
            return token.Token;
        }

        public async Task<(double requests, double txBytes)> GetMetricsMonthToDateAsync(CancellationToken ct)
        {
            var o = _options.CurrentValue;
            if (!o.Enabled) return (0, 0);
            var rid = GetResourceId();
            var (start, end) = MonthToDate();
            var startIso = Uri.EscapeDataString(start.ToString("O"));
            var endIso = Uri.EscapeDataString(end.ToString("O"));
            var api = $"https://management.azure.com{rid}/providers/microsoft.insights/metrics" +
                      $"?metricnames=Requests,TxBytes&aggregation=Total&timespan={startIso}/{endIso}&interval=PT1H&api-version=2018-01-01";

            var token = await GetTokenAsync("https://management.azure.com/.default", ct);
            using var req = new HttpRequestMessage(HttpMethod.Get, api);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStreamAsync(ct));
            double SumMetric(string name)
            {
                var root = doc.RootElement;
                var val = root.GetProperty("value").EnumerateArray().FirstOrDefault(v => v.GetProperty("name").GetProperty("value").GetString() == name);
                if (val.ValueKind == JsonValueKind.Undefined) return 0;
                var dataArr = val.GetProperty("timeseries")[0].GetProperty("data").EnumerateArray();
                double sum = 0;
                foreach (var d in dataArr)
                {
                    if (d.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number)
                        sum += t.GetDouble();
                }
                return sum;
            }
            var requests = SumMetric("Requests");
            var tx = SumMetric("TxBytes");
            return (requests, tx);
        }

        public async Task<(double vcpuSeconds, double giBSeconds)> GetCostMonthToDateAsync(CancellationToken ct)
        {
            var o = _options.CurrentValue;
            if (!o.Enabled) return (0, 0);

            var rid = GetResourceId();
            var (start, end) = MonthToDate();

            // Get Container App configuration to retrieve CPU and Memory allocation
            var (vcpuPerReplica, memoryGiBPerReplica) = await GetContainerAppResourceConfigAsync(rid, ct);

            // Get Replica Count metrics month-to-date
            var startIso = Uri.EscapeDataString(start.ToString("O"));
            var endIso = Uri.EscapeDataString(end.ToString("O"));
            var api = $"https://management.azure.com{rid}/providers/microsoft.insights/metrics" +
                      $"?metricnames=Replicas&aggregation=Average&timespan={startIso}/{endIso}&interval=PT1H&api-version=2018-01-01";

            var token = await GetTokenAsync("https://management.azure.com/.default", ct);
            using var req = new HttpRequestMessage(HttpMethod.Get, api);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStreamAsync(ct));

            // Calculate vCPU-seconds and GiB-seconds from replica count over time
            double totalVcpuSeconds = 0;
            double totalGiBSeconds = 0;

            var root = doc.RootElement;
            var val = root.GetProperty("value").EnumerateArray().FirstOrDefault(v => v.GetProperty("name").GetProperty("value").GetString() == "Replicas");
            if (val.ValueKind != JsonValueKind.Undefined)
            {
                var dataArr = val.GetProperty("timeseries")[0].GetProperty("data").EnumerateArray();
                foreach (var d in dataArr)
                {
                    if (d.TryGetProperty("average", out var avg) && avg.ValueKind == JsonValueKind.Number)
                    {
                        var replicaCount = avg.GetDouble();
                        // Each data point represents 1 hour (3600 seconds)
                        totalVcpuSeconds += replicaCount * 3600 * vcpuPerReplica;
                        totalGiBSeconds += replicaCount * 3600 * memoryGiBPerReplica;
                    }
                }
            }

            return (totalVcpuSeconds, totalGiBSeconds);
        }

        private async Task<(double vcpu, double memoryGiB)> GetContainerAppResourceConfigAsync(string resourceId, CancellationToken ct)
        {
            var api = $"https://management.azure.com{resourceId}?api-version=2023-05-01";
            var token = await GetTokenAsync("https://management.azure.com/.default", ct);
            using var req = new HttpRequestMessage(HttpMethod.Get, api);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStreamAsync(ct));

            // Extract CPU and Memory from template.containers[0].resources
            var root = doc.RootElement;
            if (root.TryGetProperty("properties", out var props) &&
                props.TryGetProperty("template", out var template) &&
                template.TryGetProperty("containers", out var containers) &&
                containers.GetArrayLength() > 0)
            {
                var container = containers[0];
                if (container.TryGetProperty("resources", out var resources))
                {
                    double vcpu = 0.25; // default
                    double memoryGiB = 0.5; // default

                    if (resources.TryGetProperty("cpu", out var cpuProp))
                    {
                        var cpuStr = cpuProp.GetString();
                        if (!string.IsNullOrEmpty(cpuStr) && double.TryParse(cpuStr, out var cpuVal))
                        {
                            vcpu = cpuVal;
                        }
                    }

                    if (resources.TryGetProperty("memory", out var memProp))
                    {
                        var memStr = memProp.GetString();
                        if (!string.IsNullOrEmpty(memStr))
                        {
                            // Memory is in format like "0.5Gi" or "1Gi"
                            memStr = memStr.Replace("Gi", "").Replace("gi", "").Trim();
                            if (double.TryParse(memStr, out var memVal))
                            {
                                memoryGiB = memVal;
                            }
                        }
                    }

                    return (vcpu, memoryGiB);
                }
            }

            // Fallback to defaults if not found
            return (0.25, 0.5);
        }
    }
}
