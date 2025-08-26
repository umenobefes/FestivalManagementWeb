using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FestivalManagementWeb.Models
{
    public class UserViewModel
    {
        public IEnumerable<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();

        [Required(ErrorMessage = "メールアドレスは必須です。")]
        [EmailAddress(ErrorMessage = "有効なメールアドレスを入力してください。")]
        [Display(Name = "追加するユーザーのメールアドレス")]
        public string NewUserEmail { get; set; } = string.Empty;
    }
}
