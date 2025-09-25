using System;

namespace FestivalManagementWeb.Services
{
    public class RequestQuotaSnapshot
    {
        public DateTime DateUtc { get; set; }
        public int UsedToday { get; set; }
    }

    public interface IRequestQuotaService
    {
        // Returns true if consumption succeeded and within cap
        bool TryConsume(int permits, int dailyCap, out int usedAfter, out int remaining);
        RequestQuotaSnapshot GetSnapshot();
    }
}

