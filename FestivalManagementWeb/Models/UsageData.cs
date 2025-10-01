using MongoDB.Bson.Serialization.Attributes;
using System;

namespace FestivalManagementWeb.Models
{
    public class UsageData
    {
        [BsonId]
        public string Id { get; set; } = "global";

        [BsonElement("lastMetricsUpdateUtc")]
        public DateTime? LastMetricsUpdateUtc { get; set; }

        [BsonElement("lastCostUpdateUtc")]
        public DateTime? LastCostUpdateUtc { get; set; }

        [BsonElement("requestsUsed")]
        public double? RequestsUsed { get; set; }

        [BsonElement("txBytesUsed")]
        public double? TxBytesUsed { get; set; }

        [BsonElement("vcpuSecondsUsed")]
        public double? VcpuSecondsUsed { get; set; }

        [BsonElement("giBSecondsUsed")]
        public double? GiBSecondsUsed { get; set; }
    }
}
