using System;
using FestivalManagementWeb.Models;
using Microsoft.Extensions.Options;

namespace FestivalManagementWeb.Services
{
    public class FreeTierService : IFreeTierService
    {
        private readonly IOptionsMonitor<FreeTierSettings> _options;
        private readonly IAutoUsageState? _auto;

        public FreeTierService(IOptionsMonitor<FreeTierSettings> options, IAutoUsageState? auto = null)
        {
            _options = options;
            _auto = auto;
        }

        public FreeTierInfo GetInfo()
        {
            var s = _options.CurrentValue;
            if (s == null)
            {
                return new FreeTierInfo { Enabled = false, Note = "No settings" };
            }

            var asOf = DateTime.UtcNow;
            var today = asOf.Date;
            var endOfMonth = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
            var daysRemaining = (endOfMonth - today).Days + 1; // include today
            if (daysRemaining < 1) daysRemaining = 1;

            // Prefer auto-collected usage when available
            var usedVcpu = _auto?.Enabled == true && _auto.VcpuSecondsUsed.HasValue ? _auto.VcpuSecondsUsed.Value : 0;
            var usedGiB = _auto?.Enabled == true && _auto.GiBSecondsUsed.HasValue ? _auto.GiBSecondsUsed.Value : 0;

            var remainingVcpu = Math.Max(0, s.BudgetVcpuSeconds - usedVcpu);
            var remainingGiB = Math.Max(0, s.BudgetGiBSeconds - usedGiB);

            var replica = s.Resource.ReplicaFactor > 0 ? s.Resource.ReplicaFactor : 1;
            var denomCpu = Math.Max(1e-9, s.Resource.VcpuPerReplica * replica);
            var denomMem = Math.Max(1e-9, s.Resource.MemoryGiBPerReplica * replica);

            var hoursCpu = remainingVcpu / denomCpu / 3600.0;
            var hoursMem = remainingGiB / denomMem / 3600.0;
            var totalHours = Math.Min(hoursCpu, hoursMem);
            if (double.IsInfinity(totalHours) || double.IsNaN(totalHours)) totalHours = 0;

            var perDay = totalHours / daysRemaining;

            // Data egress (GB)
            var dataBudgetGb = s.Data?.BudgetGb ?? 0;
            var dataUsedGb = (_auto?.Enabled == true && _auto.TxBytesUsed.HasValue)
                ? _auto.TxBytesUsed.Value / 1e9
                : 0;
            var dataRemainGb = Math.Max(0, dataBudgetGb - dataUsedGb);
            var dataPerDayGb = dataRemainGb / daysRemaining;

            // Per-day raw meters
            var vcpuPerDay = remainingVcpu / daysRemaining;
            var gibPerDay = remainingGiB / daysRemaining;

            // Requests
            var reqBudget = s.Requests?.Budget ?? 0;
            var reqUsed = (_auto?.Enabled == true && _auto.RequestsUsed.HasValue)
                ? _auto.RequestsUsed.Value
                : 0;
            var reqRemain = Math.Max(0, reqBudget - reqUsed);
            var reqPerDay = reqRemain / daysRemaining;

            // Estimated hours used (from meters)
            var hoursUsedCpu = usedVcpu / denomCpu / 3600.0;
            var hoursUsedMem = usedGiB / denomMem / 3600.0;
            var hoursUsedEst = Math.Min(hoursUsedCpu, hoursUsedMem);

            // Percentages
            double pct(double used, double budget) => budget <= 0 ? 0 : Math.Min(100, Math.Max(0, used / budget * 100));
            var cpuPct = pct(usedVcpu, s.BudgetVcpuSeconds);
            var memPct = pct(usedGiB, s.BudgetGiBSeconds);
            var reqPct = pct(reqUsed, reqBudget);
            var dataPct = pct(dataUsedGb, dataBudgetGb);

            var cosmosStatus = ResolveCosmosStatus(s);

            return new FreeTierInfo
            {
                Enabled = s.EnableBanner,
                RemainingVcpuSeconds = remainingVcpu,
                RemainingGiBSeconds = remainingGiB,
                UsedVcpuSeconds = usedVcpu,
                UsedGiBSeconds = usedGiB,
                HoursRemainingTotal = totalHours,
                HoursPerDayRemaining = perDay,
                HoursUsedEstimated = hoursUsedEst,
                DaysRemainingInMonth = daysRemaining,
                AsOfUtc = asOf,
                Note = null,
                DataRemainingGb = dataRemainGb,
                DataPerDayRemainingGb = dataPerDayGb,
                DataUsedGb = dataUsedGb,
                RequestsRemaining = reqRemain,
                RequestsPerDayRemaining = reqPerDay,
                RequestsUsed = reqUsed,
                VcpuSecondsPerDay = vcpuPerDay,
                GiBSecondsPerDay = gibPerDay,
                CpuUsedPercent = cpuPct,
                MemUsedPercent = memPct,
                RequestsUsedPercent = reqPct,
                DataUsedPercent = dataPct,
                Cosmos = cosmosStatus
            };
        }

        private CosmosFreeTierStatus? ResolveCosmosStatus(FreeTierSettings settings)
        {
            if (settings.Cosmos == null || !settings.Cosmos.Enabled)
            {
                return null;
            }

            var latest = _auto?.CosmosStatus;
            if (latest != null)
            {
                return latest;
            }

            return new CosmosFreeTierStatus
            {
                Enabled = true,
                AccountName = settings.Cosmos.AccountName,
                DatabaseName = settings.Cosmos.DatabaseName,
                Provisioning = settings.Cosmos.Provisioning,
                FreeTierStorageLimitGb = settings.Cosmos.Provisioning == CosmosProvisioningModel.VCore
                    ? settings.Cosmos.FreeTierVCoreStorageGb
                    : settings.Cosmos.FreeTierStorageGb,
                Warning = "Cosmos DB free-tier status is being refreshed..."
            };
        }
    }
}



