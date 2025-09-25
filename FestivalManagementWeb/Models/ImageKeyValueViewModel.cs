using Microsoft.AspNetCore.Http;
using System.Collections.Generic;

namespace FestivalManagementWeb.Models
{
    public class ImageKeyValueViewModel
    {
        public IEnumerable<ImageKeyValue> AllItems { get; set; } = new List<ImageKeyValue>();
        public ImageKeyValue ItemToEdit { get; set; } = new ImageKeyValue();
        public int SelectedYear { get; set; }
        public IFormFile? ImageFile { get; set; }
    }
}


