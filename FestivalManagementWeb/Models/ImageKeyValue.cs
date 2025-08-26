using System.ComponentModel.DataAnnotations;

namespace FestivalManagementWeb.Models
{
    public class ImageKeyValue : BaseModel
    {
        [Required]
        [Display(Name = "キー")]
        public string Key { get; set; } = string.Empty;

        [Required]
        [Display(Name = "画像データ")]
        public byte[] Value { get; set; } = Array.Empty<byte>();

        [Required]
        [Display(Name = "コンテントタイプ")]
        public string ContentType { get; set; } = string.Empty;
    }
}
