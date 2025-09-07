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
        [Display(Name = "デプロイ済み")]
        public bool? Deployed { get; set; } = false;
        [Display(Name = "更新日時")]
        public DateTime? DeployedDate { get; set; } = null;
    }
}
