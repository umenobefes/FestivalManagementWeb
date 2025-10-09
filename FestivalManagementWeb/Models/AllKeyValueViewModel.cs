using System.Collections.Generic;

namespace FestivalManagementWeb.Models
{
    public class AllKeyValueViewModel
    {
        public IEnumerable<BaseModel> AllItems { get; set; } = new List<BaseModel>();
        public int SelectedYear { get; set; }
        public List<KeyValueTreeNode> TreeNodes { get; set; } = new List<KeyValueTreeNode>();
    }

    public class KeyValueTreeNode
    {
        public string Name { get; set; } = string.Empty;
        public List<KeyValueTreeNode> Children { get; set; } = new List<KeyValueTreeNode>();
        public BaseModel? Item { get; set; }
        public bool IsLeaf => Item != null;
        public string NodeType { get; set; } = string.Empty; // "category", "number", "subcategory"
    }
}
