using System;
using System.Collections.Generic;

namespace FestivalManagementWeb.Models
{
    public enum CosmosProvisioningModel
    {
        RequestUnits,
        VCore
    }

    public sealed class CosmosRuComponent
    {
        public CosmosRuComponent(string scope, double throughput)
        {
            Scope = scope;
            Throughput = throughput;
        }

        public string Scope { get; }
        public double Throughput { get; }
    }

    public class CosmosFreeTierSettings
    {
        public bool Enabled { get; set; }
        public string? SubscriptionId { get; set; }
        public string? ResourceGroup { get; set; }
        public string? AccountName { get; set; }
        public string? AccountResourceId { get; set; }
        public string? SubscriptionOverride { get; set; }
        public string? DatabaseName { get; set; }
        public string[] CollectionNames { get; set; } = Array.Empty<string>();
        public CosmosProvisioningModel Provisioning { get; set; } = CosmosProvisioningModel.RequestUnits;
        public double FreeTierRuLimit { get; set; } = 1000;
        public double FreeTierStorageGb { get; set; } = 25;
        public double FreeTierVCoreStorageGb { get; set; } = 32;
        public double? WarnRuPercent { get; set; } = 90;
        public double? WarnStoragePercent { get; set; } = 90;
        public int RefreshMinutes { get; set; } = 60;
        public string? MetricNamespace { get; set; }
    }

    public class CosmosFreeTierStatus
    {
        private readonly List<string> _issues = new();
        private readonly List<CosmosRuComponent> _ruBreakdown = new();

        public bool Enabled { get; set; }
        public string? AccountName { get; set; }
        public string? DatabaseName { get; set; }
        public string? AccountResourceId { get; set; }
        public CosmosProvisioningModel Provisioning { get; set; }
        public bool? FreeTierActive { get; set; }
        public bool? ApiIsMongo { get; set; }
        public double FreeTierRuLimit { get; set; }
        public double FreeTierStorageLimitGb { get; set; }
        public double? ProvisionedRu { get; set; }
        public double? StorageGb { get; set; }
        public double? RuPercentOfLimit { get; set; }
        public double? StoragePercentOfLimit { get; set; }
        public bool? WithinRuLimit { get; set; }
        public bool? WithinStorageLimit { get; set; }
        public bool? OverallWithinFreeTier { get; set; }
        public bool ShouldWarn { get; set; }
        public bool ShouldStop { get; set; }
        public string? Error { get; set; }
        public string? Warning { get; set; }
        public DateTime? CheckedAtUtc { get; set; }

        public IReadOnlyList<string> Issues => _issues;
        public IReadOnlyList<CosmosRuComponent> RuBreakdown => _ruBreakdown;

        public void AddIssue(string issue)
        {
            if (!string.IsNullOrWhiteSpace(issue))
            {
                _issues.Add(issue);
            }
        }

        public void AddRuComponent(string scope, double throughput)
        {
            _ruBreakdown.Add(new CosmosRuComponent(scope, throughput));
        }

        public static CosmosFreeTierStatus Disabled() => new()
        {
            Enabled = false,
            Warning = "Cosmos DB free-tier check is disabled."
        };
    }
}
