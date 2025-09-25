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
            if (string.IsNullOrWhiteSpace(o.SubscriptionId))
                throw new InvalidOperationException("AzureUsage: SubscriptionId is required for cost query.");

            var (start, end) = MonthToDate();
            // Cost Management Query (Usage) MTD grouped by Meter
            var api = $"https://management.azure.com/subscriptions/{o.SubscriptionId}/providers/Microsoft.CostManagement/query?api-version=2024-07-01";

            object serviceNameFilter = new
            {
                dimensions = new
                {
                    name = "ServiceName",
                    operatorProperty = "In",
                    values = new[] { "Azure Container Apps" }
                }
            };

            object? resourceFilter = null;
            var ridForCost = !string.IsNullOrWhiteSpace(o.ContainerAppResourceId) ? o.ContainerAppResourceId : null;
            if (string.IsNullOrWhiteSpace(ridForCost))
            {
                try { ridForCost = GetResourceId(); } catch { /* fall back to RG */ }
            }
            if (!string.IsNullOrWhiteSpace(ridForCost))
            {
                resourceFilter = new
                {
                    dimensions = new
                    {
                        name = "ResourceId",
                        operatorProperty = "In",
                        values = new[] { ridForCost! }
                    }
                };
            }
            else if (!string.IsNullOrWhiteSpace(o.ResourceGroup))
            {
                resourceFilter = new
                {
                    dimensions = new
                    {
                        name = "ResourceGroupName",
                        operatorProperty = "In",
                        values = new[] { o.ResourceGroup! }
                    }
                };
            }

            object filterObject = resourceFilter == null ? serviceNameFilter : new { and = new object[] { serviceNameFilter, resourceFilter } };

            var body = new
            {
                type = "Usage",
                timeframe = "Custom",
                timePeriod = new { from = start.ToString("o"), to = end.ToString("o") },
                dataset = new
                {
                    aggregation = new { totalCost = new { name = "PreTaxCost", function = "Sum" } },
                    granularity = "None",
                    grouping = new[] { new { type = "Dimension", name = "Meter" }, new { type = "Dimension", name = "ServiceName" } },
                    filter = filterObject
                }
            };
            var token = await GetTokenAsync("https://management.azure.com/.default", ct);
            using var req = new HttpRequestMessage(HttpMethod.Post, api)
            {
                Content = JsonContent.Create(body)
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStreamAsync(ct));
            double vcpu = 0, gib = 0;
            if (doc.RootElement.TryGetProperty("properties", out var prop) && prop.TryGetProperty("rows", out var rows))
            {
                foreach (var row in rows.EnumerateArray())
                {
                    // rows schema can vary; fallback to probing dimension values
                    var arr = row.EnumerateArray().ToArray();
                    // Heuristics: Look for Meter names containing 'vCPU (seconds)' or 'Memory (GiB-seconds)'
                    var meter = arr.Select(e => e.ToString()).FirstOrDefault(s => s?.Contains("vCPU", StringComparison.OrdinalIgnoreCase) == true || s?.Contains("GiB-seconds", StringComparison.OrdinalIgnoreCase) == true || s?.Contains("GiB", StringComparison.OrdinalIgnoreCase) == true);
                    var qty = arr.Select(e => { double d; return double.TryParse(e.ToString(), out d) ? d : (double?)null; })
                                 .Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty(0).Max();
                    if (meter != null)
                    {
                        if (meter.Contains("vCPU", StringComparison.OrdinalIgnoreCase)) vcpu += qty;
                        else if (meter.Contains("GiB", StringComparison.OrdinalIgnoreCase)) gib += qty;
                    }
                }
            }
            return (vcpu, gib);
        }
    }
}
