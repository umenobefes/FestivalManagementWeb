using MongoDB.Bson.Serialization.Attributes;
using System;

namespace FestivalManagementWeb.Models
{
    public class RequestQuotaData
    {
        [BsonId]
        public string Id { get; set; } = "global";

        [BsonElement("dateUtc")]
        public DateTime DateUtc { get; set; }

        [BsonElement("usedToday")]
        public int UsedToday { get; set; }
    }
}
