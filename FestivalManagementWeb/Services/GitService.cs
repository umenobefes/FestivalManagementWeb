using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;

namespace FestivalManagementWeb.Services
{
    public class GitService : IGitService
    {
        private readonly string _repoPath;
        private readonly string _authorName;
        private readonly string _authorEmail;
        private readonly string? _gitUsername;
        private readonly string? _gitPassword;

        public GitService(IConfiguration configuration)
        {
            // Find repository path by searching upwards from the current directory
            _repoPath = FindRepoPath(Directory.GetCurrentDirectory()) ?? throw new DirectoryNotFoundException("Git repository not found.");

            _authorName = configuration["GitSettings:AuthorName"] ?? "Default User";
            _authorEmail = configuration["GitSettings:AuthorEmail"] ?? "default@example.com";
            _gitUsername = configuration["GitSettings:Username"];
            _gitPassword = configuration["GitSettings:Password"];
        }

        public Task CommitAndPushChanges(string message)
        {
            return Task.Run(() =>
            {
                using (var repo = new Repository(_repoPath))
                {
                    // Stage all changes
                    Commands.Stage(repo, "*");

                    // Create the commit
                    var author = new Signature(_authorName, _authorEmail, DateTimeOffset.Now);
                    var committer = author;
                    repo.Commit(message, author, committer);

                    // Push the changes to the remote 'origin'
                    var remote = repo.Network.Remotes["origin"];
                    var options = new PushOptions();

                    if (!string.IsNullOrEmpty(_gitUsername) && !string.IsNullOrEmpty(_gitPassword))
                    {
                        options.CredentialsProvider = (_url, _user, _cred) =>
                            new UsernamePasswordCredentials { Username = _gitUsername, Password = _gitPassword };
                    }

                    repo.Network.Push(remote, "refs/heads/master", options);
                }
            });
        }

        private static string? FindRepoPath(string startPath)
        {
            var currentPath = startPath;
            while (currentPath != null)
            {
                if (Repository.IsValid(currentPath))
                {
                    return currentPath;
                }
                currentPath = Directory.GetParent(currentPath)?.FullName;
            }
            return null;
        }
    }
}