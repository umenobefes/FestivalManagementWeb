using System.Threading;
using System.Threading.Tasks;

namespace FestivalManagementWeb.Services
{
    public class AzureUsageSnapshot
    {
        public double RequestsUsed { get; set; }
        public double TxBytesUsed { get; set; } // bytes
        public double VcpuSecondsUsed { get; set; }
        public double GiBSecondsUsed { get; set; }
    }

    public interface IAzureUsageProvider
    {
        Task<(double requests, double txBytes)> GetMetricsMonthToDateAsync(CancellationToken ct);
        Task<(double vcpuSeconds, double giBSeconds)> GetCostMonthToDateAsync(CancellationToken ct);
    }
}

