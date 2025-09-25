using System.Collections.Generic;

namespace FestivalManagementWeb.Models
{
    public class TextKeyValueViewModel
    {
        public IEnumerable<TextKeyValue> AllItems { get; set; } = new List<TextKeyValue>();
        public TextKeyValue ItemToEdit { get; set; } = new TextKeyValue();
        public int SelectedYear { get; set; }
    }
}

