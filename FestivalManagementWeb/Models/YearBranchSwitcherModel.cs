using System.Collections.Generic;
using System.Linq;

namespace FestivalManagementWeb.Models
{
    public class YearBranchSwitcherModel
    {
        public IReadOnlyList<int> AvailableYears { get; set; } = new List<int>();
        public int CurrentYear { get; set; }
        public int NextYear => CurrentYear + 1;
        public bool NextYearExists => AvailableYears.Contains(NextYear);
    }
}

