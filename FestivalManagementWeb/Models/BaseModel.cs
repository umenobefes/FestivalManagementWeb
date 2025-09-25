using MongoDB.Bson.Serialization.Attributes;
using System;
using System.ComponentModel.DataAnnotations;

namespace FestivalManagementWeb.Models
{
    public abstract class BaseModel
    {
        [BsonId]
        public Guid Id { get; set; }
        [BsonElement("year")]
        [Display(Name = "年度")]
        public int Year { get; set; } = DateTime.UtcNow.Year;
    }
}

