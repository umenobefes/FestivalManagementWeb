using System;
using FestivalManagementWeb.Models;
using MongoDB.Driver;

namespace FestivalManagementWeb.Services
{
    public class AutoUsageState : IAutoUsageState
    {
        private readonly object _lock = new();
        private readonly IMongoCollection<UsageData> _collection;

        public bool Enabled { get; private set; }
        public DateTime? LastMetricsUpdateUtc { get; private set; }
        public DateTime? LastCostUpdateUtc { get; private set; }
        public double? RequestsUsed { get; private set; }
        public double? TxBytesUsed { get; private set; }
        public double? VcpuSecondsUsed { get; private set; }
        public double? GiBSecondsUsed { get; private set; }
        public CosmosFreeTierStatus? CosmosStatus { get; private set; }

        public AutoUsageState(IMongoClient mongoClient, MongoDbSettings settings)
        {
            var database = mongoClient.GetDatabase(settings.DatabaseName);
            _collection = database.GetCollection<UsageData>("UsageData");

            // Load initial data from MongoDB
            lock (_lock)
            {
                var data = _collection.Find(x => x.Id == "global").FirstOrDefault();
                if (data != null)
                {
                    LastMetricsUpdateUtc = data.LastMetricsUpdateUtc;
                    LastCostUpdateUtc = data.LastCostUpdateUtc;
                    RequestsUsed = data.RequestsUsed;
                    TxBytesUsed = data.TxBytesUsed;
                    VcpuSecondsUsed = data.VcpuSecondsUsed;
                    GiBSecondsUsed = data.GiBSecondsUsed;
                }
            }
        }

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
                PersistToDatabase();
            }
        }

        public void SetCost(double vcpuSecondsUsed, double giBSecondsUsed, DateTime asOfUtc)
        {
            lock (_lock)
            {
                VcpuSecondsUsed = vcpuSecondsUsed;
                GiBSecondsUsed = giBSecondsUsed;
                LastCostUpdateUtc = asOfUtc;
                PersistToDatabase();
            }
        }

        public void SetCosmosStatus(CosmosFreeTierStatus status)
        {
            lock (_lock)
            {
                CosmosStatus = status;
            }
        }

        private void PersistToDatabase()
        {
            var data = new UsageData
            {
                Id = "global",
                LastMetricsUpdateUtc = LastMetricsUpdateUtc,
                LastCostUpdateUtc = LastCostUpdateUtc,
                RequestsUsed = RequestsUsed,
                TxBytesUsed = TxBytesUsed,
                VcpuSecondsUsed = VcpuSecondsUsed,
                GiBSecondsUsed = GiBSecondsUsed
            };
            _collection.ReplaceOne(
                x => x.Id == "global",
                data,
                new ReplaceOptions { IsUpsert = true });
        }
    }
}

