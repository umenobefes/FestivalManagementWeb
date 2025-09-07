using System.Threading.Tasks;

namespace FestivalManagementWeb.Services
{
    public interface IGitService
    {
        Task CommitAndPushChanges(string message);
    }
}
