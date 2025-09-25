using System;
using FestivalManagementWeb.Models;

namespace FestivalManagementWeb.Services
{
    public interface IAutoUsageState
    {
        bool Enabled { get; }
        DateTime? LastMetricsUpdateUtc { get; }
        DateTime? LastCostUpdateUtc { get; }

        double? RequestsUsed { get; }
        double? TxBytesUsed { get; } // bytes
        double? VcpuSecondsUsed { get; }
        double? GiBSecondsUsed { get; }
        CosmosFreeTierStatus? CosmosStatus { get; }

        void SetMetrics(double requestsUsed, double txBytesUsed, DateTime asOfUtc);
        void SetCost(double vcpuSecondsUsed, double giBSecondsUsed, DateTime asOfUtc);
        void SetCosmosStatus(CosmosFreeTierStatus status);
        void SetEnabled(bool enabled);
    }
}

