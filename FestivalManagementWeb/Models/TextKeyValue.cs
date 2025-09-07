using System.ComponentModel.DataAnnotations;

namespace FestivalManagementWeb.Models
{
    public class TextKeyValue : BaseModel
    {
        [Required]
        [Display(Name = "キー")]
        public string Key { get; set; } = string.Empty;

        [Required]
        [Display(Name = "値")]
        public string Value { get; set; } = string.Empty;
        [Display(Name = "デプロイ済み")]
        public bool? Deployed { get; set; } = false;
        [Display(Name = "更新日時")]
        public DateTime? DeployedDate { get; set; } = null;
    }
}
