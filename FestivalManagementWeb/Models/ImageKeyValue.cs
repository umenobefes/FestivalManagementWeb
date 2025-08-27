using MongoDB.Bson;
using System.ComponentModel.DataAnnotations;

namespace FestivalManagementWeb.Models
{
    public class ImageKeyValue : BaseModel
    {
        [Required]
        [Display(Name = "キー")]
        public string Key { get; set; } = string.Empty;

        [Display(Name = "GridFSファイルID")]
        public ObjectId GridFSFileId { get; set; }
    }
}
