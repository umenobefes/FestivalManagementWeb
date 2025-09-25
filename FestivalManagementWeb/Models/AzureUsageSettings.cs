namespace FestivalManagementWeb.Models
{
    public class AzureUsageSettings
    {
        public bool Enabled { get; set; } = false;
        public string? SubscriptionId { get; set; }
        public string? ResourceGroup { get; set; }
        public string? ContainerAppName { get; set; }

        // Refresh cadences
        public int MetricsRefreshMinutes { get; set; } = 10; // Requests/TxBytes
        public int CostRefreshMinutes { get; set; } = 360;      // vCPU/GiB seconds

        // Optional: full resource ID override (if set, RG/Name not required)
        public string? ContainerAppResourceId { get; set; }
    }
}

