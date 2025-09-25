using System;
using System.Collections.Generic;

namespace FestivalManagementWeb.Models
{
    public sealed class CosmosCapacityViewModel
    {
        public CosmosFreeTierStatus Cosmos { get; set; } = new();
        public bool BannerEnabled { get; set; }
        public IReadOnlyList<int> AvailableYears { get; set; } = Array.Empty<int>();
        public string? CleanupMessage { get; set; }
    }
}
