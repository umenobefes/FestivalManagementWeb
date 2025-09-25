using System;
using System.Threading;

namespace FestivalManagementWeb.Services
{
    public class RequestQuotaService : IRequestQuotaService
    {
        private DateTime _dateUtc = DateTime.UtcNow.Date;
        private int _usedToday = 0;
        private readonly object _lock = new();

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

