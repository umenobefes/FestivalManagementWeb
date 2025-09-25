using System.Collections.Generic;
using System.Threading.Tasks;

namespace FestivalManagementWeb.Services
{
    public interface IYearBranchService
    {
        Task<int> GetCurrentYearAsync();
        Task SetCurrentYearAsync(int year);
        Task<IReadOnlyList<int>> GetAvailableYearsAsync();
        Task<int> CreateNextYearBranchAsync();
        Task EnsureYearBranchExistsAsync(int year);
    }
}

