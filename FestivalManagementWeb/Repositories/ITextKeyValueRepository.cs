using FestivalManagementWeb.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FestivalManagementWeb.Repositories
{
    public interface ITextKeyValueRepository
    {
        Task<IEnumerable<TextKeyValue>> GetAllAsync(int year);
        Task<TextKeyValue> GetByIdAsync(Guid id);
        Task<TextKeyValue> GetByKeyAsync(string key, int year);
        Task CreateAsync(TextKeyValue textKeyValue);
        Task<bool> UpdateAsync(TextKeyValue textKeyValue);
        Task<bool> DeleteAsync(Guid id);
        Task<long> DeleteByYearAsync(int year);

        Task<IReadOnlyList<int>> GetDistinctYearsAsync();
    }
}
