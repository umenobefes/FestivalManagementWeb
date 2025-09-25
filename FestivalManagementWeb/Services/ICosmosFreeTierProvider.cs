using System.Threading;
using System.Threading.Tasks;
using FestivalManagementWeb.Models;

namespace FestivalManagementWeb.Services
{
    public interface ICosmosFreeTierProvider
    {
        Task<CosmosFreeTierStatus> GetStatusAsync(CancellationToken ct);
    }
}

