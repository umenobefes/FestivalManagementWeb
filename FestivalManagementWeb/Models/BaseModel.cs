using MongoDB.Bson.Serialization.Attributes;
using System;
using System.ComponentModel.DataAnnotations;

namespace FestivalManagementWeb.Models
{
    public abstract class BaseModel
    {
        [BsonId]
        public Guid Id { get; set; }
    }
}
