using FestivalManagementWeb.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FestivalManagementWeb.Repositories
{
    public interface ITextKeyValueRepository
    {
        Task<IEnumerable<TextKeyValue>> GetAllAsync();
        Task<TextKeyValue> GetByIdAsync(Guid id);
        Task<TextKeyValue> GetByKeyAsync(string key);
        Task CreateAsync(TextKeyValue textKeyValue);
        Task<bool> UpdateAsync(TextKeyValue textKeyValue);
        Task<bool> DeleteAsync(Guid id);
        Task<IEnumerable<TextKeyValue>> GetDeployedBeforeAsync(DateTime date);
    }
}
