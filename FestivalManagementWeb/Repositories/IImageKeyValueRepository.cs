using FestivalManagementWeb.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FestivalManagementWeb.Repositories
{
    public interface IImageKeyValueRepository
    {
        Task<IEnumerable<ImageKeyValue>> GetAllAsync(int year);
        Task<ImageKeyValue> GetByIdAsync(Guid id);
        Task<ImageKeyValue> GetByKeyAsync(string key, int year);
        Task CreateAsync(ImageKeyValue imageKeyValue);
        Task<bool> UpdateAsync(ImageKeyValue imageKeyValue);
        Task<bool> DeleteAsync(Guid id);
        Task<long> DeleteByYearAsync(int year);

        Task<IReadOnlyList<int>> GetDistinctYearsAsync();
    }
}
