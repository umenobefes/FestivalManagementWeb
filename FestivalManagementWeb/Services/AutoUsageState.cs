using System;
using FestivalManagementWeb.Models;

namespace FestivalManagementWeb.Services
{
    public class AutoUsageState : IAutoUsageState
    {
        private readonly object _lock = new();
        public bool Enabled { get; private set; }
        public DateTime? LastMetricsUpdateUtc { get; private set; }
        public DateTime? LastCostUpdateUtc { get; private set; }
        public double? RequestsUsed { get; private set; }
        public double? TxBytesUsed { get; private set; }
        public double? VcpuSecondsUsed { get; private set; }
        public double? GiBSecondsUsed { get; private set; }
        public CosmosFreeTierStatus? CosmosStatus { get; private set; }

        public void SetEnabled(bool enabled)
        {
            lock (_lock)
            {
                Enabled = enabled;
            }
        }

        public void SetMetrics(double requestsUsed, double txBytesUsed, DateTime asOfUtc)
        {
            lock (_lock)
            {
                RequestsUsed = requestsUsed;
                TxBytesUsed = txBytesUsed;
                LastMetricsUpdateUtc = asOfUtc;
            }
        }

        public void SetCost(double vcpuSecondsUsed, double giBSecondsUsed, DateTime asOfUtc)
        {
            lock (_lock)
            {
                VcpuSecondsUsed = vcpuSecondsUsed;
                GiBSecondsUsed = giBSecondsUsed;
                LastCostUpdateUtc = asOfUtc;
            }
        }

        public void SetCosmosStatus(CosmosFreeTierStatus status)
        {
            lock (_lock)
            {
                CosmosStatus = status;
            }
        }
    }
}

