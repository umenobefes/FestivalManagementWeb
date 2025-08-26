using System.Collections.Generic;

namespace FestivalManagementWeb.Models
{
    public class ImageKeyValueViewModel
    {
        public IEnumerable<ImageKeyValue> AllItems { get; set; } = new List<ImageKeyValue>();
        public string? KeyToEdit { get; set; }
    }
}
