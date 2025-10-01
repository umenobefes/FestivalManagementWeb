using System;
using System.Threading;
using FestivalManagementWeb.Models;
using MongoDB.Driver;

namespace FestivalManagementWeb.Services
{
    public class RequestQuotaService : IRequestQuotaService
    {
        private DateTime _dateUtc;
        private int _usedToday;
        private readonly object _lock = new();
        private readonly IMongoCollection<RequestQuotaData> _collection;

        public RequestQuotaService(IMongoClient mongoClient, MongoDbSettings settings)
        {
            var database = mongoClient.GetDatabase(settings.DatabaseName);
            _collection = database.GetCollection<RequestQuotaData>("RequestQuotaData");

            // Load initial data from MongoDB
            lock (_lock)
            {
                var data = _collection.Find(x => x.Id == "global").FirstOrDefault();
                if (data != null)
                {
                    _dateUtc = data.DateUtc;
                    _usedToday = data.UsedToday;
                }
                else
                {
                    _dateUtc = DateTime.UtcNow.Date;
                    _usedToday = 0;
                }
            }
        }

        public bool TryConsume(int permits, int dailyCap, out int usedAfter, out int remaining)
        {
            if (permits <= 0) permits = 1;

            lock (_lock)
            {
                var today = DateTime.UtcNow.Date;
                if (today > _dateUtc)
                {
                    _dateUtc = today;
                    _usedToday = 0;
                }

                var cap = Math.Max(0, dailyCap);
                // If cap is 0, immediately reject
                if (cap == 0)
                {
                    usedAfter = _usedToday;
                    remaining = 0;
                    return false;
                }

                if (_usedToday + permits > cap)
                {
                    usedAfter = _usedToday;
                    remaining = Math.Max(0, cap - _usedToday);
                    return false;
                }

                _usedToday += permits;
                usedAfter = _usedToday;
                remaining = Math.Max(0, cap - _usedToday);

                // Persist to MongoDB
                var data = new RequestQuotaData
                {
                    Id = "global",
                    DateUtc = _dateUtc,
                    UsedToday = _usedToday
                };
                _collection.ReplaceOne(
                    x => x.Id == "global",
                    data,
                    new ReplaceOptions { IsUpsert = true });

                return true;
            }
        }

        public RequestQuotaSnapshot GetSnapshot()
        {
            lock (_lock)
            {
                return new RequestQuotaSnapshot
                {
                    DateUtc = _dateUtc,
                    UsedToday = _usedToday
                };
            }
        }
    }
}

