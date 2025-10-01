using System;
using System.Threading;
using System.Threading.Tasks;
using FestivalManagementWeb.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FestivalManagementWeb.Services
{
    public class AutoUsageRefreshHostedService : BackgroundService
    {
        private readonly ILogger<AutoUsageRefreshHostedService> _logger;
        private readonly IOptionsMonitor<AzureUsageSettings> _options;
        private readonly IOptionsMonitor<FreeTierSettings> _freeTierOptions;
        private readonly IAzureUsageProvider _provider;
        private readonly ICosmosFreeTierProvider _cosmosProvider;
        private readonly IAutoUsageState _state;

        public AutoUsageRefreshHostedService(
            ILogger<AutoUsageRefreshHostedService> logger,
            IOptionsMonitor<AzureUsageSettings> options,
            IOptionsMonitor<FreeTierSettings> freeTierOptions,
            IAzureUsageProvider provider,
            ICosmosFreeTierProvider cosmosProvider,
            IAutoUsageState state)
        {
            _logger = logger;
            _options = options;
            _freeTierOptions = freeTierOptions;
            _provider = provider;
            _cosmosProvider = cosmosProvider;
            _state = state;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var azureOptions = _options.CurrentValue;
            var cosmosSettings = _freeTierOptions.CurrentValue?.Cosmos ?? new CosmosFreeTierSettings();

            _state.SetEnabled(azureOptions.Enabled);

            var azureEnabled = azureOptions.Enabled;
            var cosmosEnabled = cosmosSettings.Enabled;

            if (!azureEnabled && !cosmosEnabled)
            {
                _logger.LogInformation("Usage auto-refresh disabled (Azure usage + Cosmos free-tier).");
                return;
            }

            if (azureEnabled)
            {
                _logger.LogInformation("AzureUsage auto-refresh started: metrics {MetricsRefreshMinutes}m, cost {CostRefreshMinutes}m", azureOptions.MetricsRefreshMinutes, azureOptions.CostRefreshMinutes);
            }
            else
            {
                _logger.LogInformation("AzureUsage auto-refresh disabled; continuing with Cosmos free-tier checks only.");
            }

            if (cosmosEnabled)
            {
                _logger.LogInformation("Cosmos free-tier checks enabled: refresh {RefreshMinutes}m", Math.Max(5, cosmosSettings.RefreshMinutes));
            }

            var metricsEvery = azureEnabled ? TimeSpan.FromMinutes(Math.Max(1, azureOptions.MetricsRefreshMinutes)) : Timeout.InfiniteTimeSpan;
            var costEvery = azureEnabled ? TimeSpan.FromMinutes(Math.Max(1, azureOptions.CostRefreshMinutes)) : Timeout.InfiniteTimeSpan;
            var cosmosEvery = cosmosEnabled ? TimeSpan.FromMinutes(Math.Max(5, cosmosSettings.RefreshMinutes)) : Timeout.InfiniteTimeSpan;

            var nextMetrics = DateTime.UtcNow;
            var nextCost = DateTime.UtcNow;
            var nextCosmos = DateTime.UtcNow;

            // Startup: immediately fetch all data with retry
            if (azureEnabled)
            {
                await RefreshMetricsWithRetryAsync(3, stoppingToken);
                await RefreshCostWithRetryAsync(3, stoppingToken);
                nextMetrics = DateTime.UtcNow + metricsEvery;
                nextCost = DateTime.UtcNow + costEvery;
            }
            if (cosmosEnabled)
            {
                await RefreshCosmosWithRetryAsync(3, stoppingToken);
                nextCosmos = DateTime.UtcNow + cosmosEvery;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;

                if (azureEnabled && now >= nextMetrics)
                {
                    await RefreshMetricsWithRetryAsync(1, stoppingToken);
                    nextMetrics = now + metricsEvery;
                }

                if (azureEnabled && now >= nextCost)
                {
                    await RefreshCostWithRetryAsync(1, stoppingToken);
                    nextCost = now + costEvery;
                }

                if (cosmosEnabled && now >= nextCosmos)
                {
                    await RefreshCosmosWithRetryAsync(1, stoppingToken);
                    nextCosmos = now + cosmosEvery;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task RefreshMetricsWithRetryAsync(int maxRetries, CancellationToken ct)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    var (req, tx) = await _provider.GetMetricsMonthToDateAsync(ct).ConfigureAwait(false);
                    _state.SetMetrics(req, tx, DateTime.UtcNow);
                    _logger.LogInformation("AzureUsage metrics refreshed: Requests={Requests}, TxBytes={TxBytes}", req, tx);
                    return;
                }
                catch (Exception ex)
                {
                    if (i == maxRetries - 1)
                    {
                        _logger.LogWarning(ex, "AzureUsage metrics refresh failed after {Retries} retries", maxRetries);
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task RefreshCostWithRetryAsync(int maxRetries, CancellationToken ct)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    var (vcpu, gib) = await _provider.GetCostMonthToDateAsync(ct).ConfigureAwait(false);
                    _state.SetCost(vcpu, gib, DateTime.UtcNow);
                    _logger.LogInformation("AzureUsage cost refreshed: vCPU-seconds={VcpuSeconds}, GiB-seconds={GiBSeconds}", vcpu, gib);
                    return;
                }
                catch (Exception ex)
                {
                    if (i == maxRetries - 1)
                    {
                        _logger.LogWarning(ex, "AzureUsage cost refresh failed after {Retries} retries", maxRetries);
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task RefreshCosmosWithRetryAsync(int maxRetries, CancellationToken ct)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    var cosmosStatus = await _cosmosProvider.GetStatusAsync(ct).ConfigureAwait(false);
                    _state.SetCosmosStatus(cosmosStatus);
                    _logger.LogInformation("Cosmos free-tier status refreshed");
                    return;
                }
                catch (Exception ex)
                {
                    if (i == maxRetries - 1)
                    {
                        _logger.LogWarning(ex, "Cosmos free-tier refresh failed after {Retries} retries", maxRetries);
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                    }
                }
            }
        }
    }
}

