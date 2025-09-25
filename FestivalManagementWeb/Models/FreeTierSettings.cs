using System;

namespace FestivalManagementWeb.Models
{
    public class FreeTierSettings
    {
        // Monthly free budgets (Container Apps Consumption plan defaults)
        public double BudgetVcpuSeconds { get; set; } = 180_000; // vCPU-seconds per month
        public double BudgetGiBSeconds { get; set; } = 360_000;  // GiB-seconds per month

        public ResourceProfile Resource { get; set; } = new();
        public DataBudget Data { get; set; } = new();
        public RequestsBudget Requests { get; set; } = new();
        public CosmosFreeTierSettings Cosmos { get; set; } = new();

        // Toggle banner display
        public bool EnableBanner { get; set; } = true;

        // Whether to enforce daily Requests cap (middleware)
        public bool EnforceRequestDailyCap { get; set; } = false;
    }

    public class ResourceProfile
    {
        public double VcpuPerReplica { get; set; } = 0.25; // e.g., 0.25
        public double MemoryGiBPerReplica { get; set; } = 0.5; // e.g., 0.5
        public double ReplicaFactor { get; set; } = 1; // average concurrent replicas
    }

    public class FreeTierInfo
    {
        public double RemainingVcpuSeconds { get; set; }
        public double RemainingGiBSeconds { get; set; }
        public double UsedVcpuSeconds { get; set; }
        public double UsedGiBSeconds { get; set; }
        public double HoursRemainingTotal { get; set; }
        public double HoursPerDayRemaining { get; set; }
        public double HoursUsedEstimated { get; set; }
        public int DaysRemainingInMonth { get; set; }
        public DateTime AsOfUtc { get; set; }
        public bool Enabled { get; set; }
        public string? Note { get; set; }

        // Data egress (GB) free budget
        public double DataRemainingGb { get; set; }
        public double DataPerDayRemainingGb { get; set; }
        public double DataUsedGb { get; set; }

        // Requests free budget
        public double RequestsRemaining { get; set; }
        public double RequestsPerDayRemaining { get; set; }
        public double RequestsUsed { get; set; }

        // Per-day raw budgets for each meter
        public double VcpuSecondsPerDay { get; set; }
        public double GiBSecondsPerDay { get; set; }

        // Usage percentages
        public double CpuUsedPercent { get; set; }
        public double MemUsedPercent { get; set; }
        public double RequestsUsedPercent { get; set; }
        public double DataUsedPercent { get; set; }

        // Cosmos DB free-tier status (API for MongoDB)
        public CosmosFreeTierStatus? Cosmos { get; set; }
    }

    public class DataBudget
    {
        // Monthly free outbound data transfer budget in GB (usage comes from auto-collector)
        public double BudgetGb { get; set; } = 100; // default; override per subscription/offer
    }

    public class RequestsBudget
    {
        public double Budget { get; set; } = 2_000_000; // monthly free requests
    }
}

