using System.Collections.Generic;
using System.Threading.Tasks;

namespace FestivalManagementWeb.Services
{
    public interface IGitService
    {
        Task CommitAndPushChanges(string message, string? branch = null);
        Task PullLatest(string? branch = null);
        Task<DateTimeOffset?> GetLastCommitDateAsync(string? branch = null);
        Task EnsureBranchExistsAsync(string branchName);
        Task<IReadOnlyList<string>> GetBranchNamesAsync();
        Task<bool> EnsureRemoteBranchAsync(string branchName);
        string RepositoryRootPath { get; }
        string GetOutputRoot(string branchName);
    }
}
