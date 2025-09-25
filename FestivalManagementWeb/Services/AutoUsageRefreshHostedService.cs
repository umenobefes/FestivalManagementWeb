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

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;

                if (azureEnabled && now >= nextMetrics)
                {
                    try
                    {
                        var (req, tx) = await _provider.GetMetricsMonthToDateAsync(stoppingToken).ConfigureAwait(false);
                        _state.SetMetrics(req, tx, now);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "AzureUsage metrics refresh failed");
                    }
                    nextMetrics = now + metricsEvery;
                }

                if (azureEnabled && now >= nextCost)
                {
                    try
                    {
                        var (vcpu, gib) = await _provider.GetCostMonthToDateAsync(stoppingToken).ConfigureAwait(false);
                        _state.SetCost(vcpu, gib, now);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "AzureUsage cost refresh failed");
                    }
                    nextCost = now + costEvery;
                }

                if (cosmosEnabled && now >= nextCosmos)
                {
                    try
                    {
                        var cosmosStatus = await _cosmosProvider.GetStatusAsync(stoppingToken).ConfigureAwait(false);
                        _state.SetCosmosStatus(cosmosStatus);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Cosmos free-tier refresh failed");
                    }
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
    }
}

