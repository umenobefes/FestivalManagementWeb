using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FestivalManagementWeb.Services
{
    public class GitService : IGitService
    {
        private readonly string _repoPath;
        private readonly string _authorName;
        private readonly string _authorEmail;
        private readonly string? _gitToken;
        private readonly string? _gitUsername;
        private readonly string? _gitPassword;
        private readonly string _remoteName;
        private readonly string? _cloneUrl;
        private readonly string? _configuredRepoPath;

        public GitService(IConfiguration configuration)
        {
            _authorName = configuration["GitSettings:AuthorName"] ?? "Default User";
            _authorEmail = configuration["GitSettings:AuthorEmail"] ?? "default@example.com";

            _gitToken = configuration["GitSettings:Token"];
            if (string.IsNullOrWhiteSpace(_gitToken))
            {
                _gitToken = Environment.GetEnvironmentVariable("GIT_TOKEN");
            }

            _gitUsername = configuration["GitSettings:Username"];
            if (string.IsNullOrWhiteSpace(_gitUsername))
            {
                _gitUsername = Environment.GetEnvironmentVariable("GIT_USERNAME");
            }

            _gitPassword = configuration["GitSettings:Password"];
            if (string.IsNullOrWhiteSpace(_gitPassword))
            {
                _gitPassword = Environment.GetEnvironmentVariable("GIT_PASSWORD");
            }

            _remoteName = configuration["GitSettings:RemoteName"] ?? "origin";
            _cloneUrl = configuration["GitSettings:CloneUrl"];

            var cfgLocal = configuration["GitSettings:LocalRepoPath"];
            var cfgLegacy = configuration["GitSetting:RemoteRepositoryPath"];
            _configuredRepoPath = !string.IsNullOrWhiteSpace(cfgLocal)
                ? cfgLocal
                : (!string.IsNullOrWhiteSpace(cfgLegacy) ? cfgLegacy : null);

            _repoPath = EnsureRepositoryReady(_configuredRepoPath, _cloneUrl)
                        ?? throw new DirectoryNotFoundException("Git repository not found or could not be prepared.");
        }

        public string RepositoryRootPath => _repoPath;

        public string GetOutputRoot(string branchName)
        {
            var gitRoot = Path.Combine(Directory.GetCurrentDirectory(), "git");
            Directory.CreateDirectory(gitRoot);
            var repoName = new DirectoryInfo(_repoPath).Name;
            var target = Path.Combine(gitRoot, repoName);
            Directory.CreateDirectory(target);
            return target;
        }

        public Task CommitAndPushChanges(string message, string? branch = null)
        {
            return Task.Run(() =>
            {
                using var repo = new Repository(_repoPath);

                var branchName = ResolveBranchName(repo, branch);
                EnsureRemoteBranchPrepared(repo, branchName);
                var localBranch = EnsureLocalBranchCheckedOut(repo, branchName);

                var staged = StageAllChanges(repo);
                if (staged)
                {
                    var author = new Signature(_authorName, _authorEmail, DateTimeOffset.Now);
                    repo.Commit(message, author, author);
                }

                if (repo.Head?.Tip == null)
                {
                    return;
                }

                var remote = EnsureRemote(repo);
                PushBranch(repo, remote, branchName);
            });
        }

        public Task PullLatest(string? branch = null)
        {
            return Task.Run(() =>
            {
                using var repo = new Repository(_repoPath);

                var branchName = ResolveBranchName(repo, branch);
                EnsureRemoteBranchPrepared(repo, branchName);
                var localBranch = EnsureLocalBranchCheckedOut(repo, branchName);
                var remote = EnsureRemote(repo);

                if (localBranch.TrackedBranch == null)
                {
                    TryFetchAll(repo);
                    var remoteBranch = repo.Branches[$"refs/remotes/{_remoteName}/{branchName}"];
                    if (remoteBranch != null)
                    {
                        repo.Branches.Update(localBranch, b =>
                        {
                            b.Remote = _remoteName;
                            b.UpstreamBranch = remoteBranch.CanonicalName;
                        });
                    }
                    else
                    {
                        return;
                    }
                }

                var sig = new Signature(_authorName, _authorEmail, DateTimeOffset.Now);
                Commands.Pull(repo, sig, CreatePullOptions());
            });
        }

        public Task<DateTimeOffset?> GetLastCommitDateAsync(string? branch = null)
        {
            return Task.Run(() =>
            {
                using var repo = new Repository(_repoPath);
                var branchName = ResolveBranchName(repo, branch);
                var localBranch = repo.Branches[branchName] ?? repo.Branches[$"refs/heads/{branchName}"];
                var commit = (localBranch ?? repo.Head)?.Tip;
                return commit?.Committer.When;
            });
        }

        public Task EnsureBranchExistsAsync(string branchName)
        {
            if (string.IsNullOrWhiteSpace(branchName))
            {
                throw new ArgumentException("Branch name is required.", nameof(branchName));
            }

            return Task.Run(() =>
            {
                using var repo = new Repository(_repoPath);
                EnsureBranchExistsInternal(repo, branchName);
            });
        }

        public Task<IReadOnlyList<string>> GetBranchNamesAsync()
        {
            return Task.Run(() =>
            {
                using var repo = new Repository(_repoPath);
                TryFetchAll(repo);
                var names = repo.Branches
                    .Select(NormalizeBranchName)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                return (IReadOnlyList<string>)names;
            });
        }

        public Task<bool> EnsureRemoteBranchAsync(string branchName)
        {
            return Task.Run(() =>
            {
                using var repo = new Repository(_repoPath);
                try
                {
                    EnsureRemoteBranchPrepared(repo, branchName);
                    return repo.Branches[$"refs/remotes/{_remoteName}/{branchName}"] != null;
                }
                catch
                {
                    return false;
                }
            });
        }

        private string ResolveBranchName(Repository repo, string? requested)
        {
            if (!string.IsNullOrWhiteSpace(requested))
            {
                return requested;
            }

            if (repo.Head != null && !repo.Info.IsHeadDetached && !repo.Head.IsRemote)
            {
                return repo.Head.FriendlyName;
            }

            return "main";
        }

        private Branch EnsureLocalBranchCheckedOut(Repository repo, string branchName)
        {
            var localBranch = repo.Branches[branchName] ?? repo.Branches[$"refs/heads/{branchName}"];
            if (localBranch == null)
            {
                EnsureBranchExistsInternal(repo, branchName);
                localBranch = repo.Branches[branchName] ?? repo.Branches[$"refs/heads/{branchName}"];
                if (localBranch == null)
                {
                    throw new InvalidOperationException($"Failed to create branch '{branchName}'.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(_remoteName) && localBranch.TrackedBranch == null)
            {
                TryFetchAll(repo);
                var remoteBranch = repo.Branches[$"refs/remotes/{_remoteName}/{branchName}"];
                if (remoteBranch != null)
                {
                    repo.Branches.Update(localBranch, b => b.TrackedBranch = remoteBranch.CanonicalName);
                }
            }

            if (repo.Head == null || repo.Info.IsHeadDetached || !string.Equals(repo.Head.FriendlyName, branchName, StringComparison.Ordinal))
            {
                Commands.Checkout(repo, localBranch);
            }

            return localBranch;
        }

        private Remote EnsureRemote(Repository repo)
        {
            var remote = repo.Network.Remotes[_remoteName];
            if (remote == null)
            {
                if (!string.IsNullOrWhiteSpace(_cloneUrl))
                {
                    remote = repo.Network.Remotes.Add(_remoteName, _cloneUrl);
                }
                else
                {
                    throw new InvalidOperationException($"Remote '{_remoteName}' not found and no CloneUrl provided.");
                }
            }

            return remote;
        }

        private bool StageAllChanges(Repository repo)
        {
            var options = new StatusOptions
            {
                IncludeUntracked = true,
                RecurseUntrackedDirs = true
            };

            var status = repo.RetrieveStatus(options);
            var entries = status.Where(s => s.State != FileStatus.Ignored).ToList();
            if (entries.Count == 0)
            {
                return false;
            }

            foreach (var entry in entries)
            {
                Commands.Stage(repo, entry.FilePath);
            }

            return true;
        }

        private CredentialsHandler? CreateCredentialsHandler()
        {
            if (!string.IsNullOrWhiteSpace(_gitToken))
            {
                var tokenUser = !string.IsNullOrWhiteSpace(_gitUsername) ? _gitUsername! : "git";
                return (_url, _user, _cred) => new UsernamePasswordCredentials
                {
                    Username = tokenUser,
                    Password = _gitToken!
                };
            }

            if (!string.IsNullOrWhiteSpace(_gitUsername) && !string.IsNullOrWhiteSpace(_gitPassword))
            {
                return (_url, _user, _cred) => new UsernamePasswordCredentials
                {
                    Username = _gitUsername,
                    Password = _gitPassword
                };
            }

            return null;
        }

        private PushOptions CreatePushOptions()
        {
            var options = new PushOptions();
            var handler = CreateCredentialsHandler();
            if (handler != null)
            {
                options.CredentialsProvider = handler;
            }
            return options;
        }

        private FetchOptions CreateFetchOptions()
        {
            var options = new FetchOptions();
            var handler = CreateCredentialsHandler();
            if (handler != null)
            {
                options.CredentialsProvider = handler;
            }
            return options;
        }

        private PullOptions CreatePullOptions()
        {
            return new PullOptions
            {
                FetchOptions = CreateFetchOptions(),
                MergeOptions = new MergeOptions { FastForwardStrategy = FastForwardStrategy.Default }
            };
        }

        private void PushBranch(Repository repo, Remote remote, string branchName, string? refSpecOverride = null)
        {
            var pushOptions = CreatePushOptions();
            var refSpec = string.IsNullOrWhiteSpace(refSpecOverride)
                ? $"refs/heads/{branchName}"
                : refSpecOverride;

            try
            {
                repo.Network.Push(remote, refSpec, pushOptions);
            }
            catch (NonFastForwardException ex)
            {
                throw new InvalidOperationException($"Push rejected for branch '{branchName}'. Fetch and merge the latest changes, then retry.", ex);
            }
            catch (LibGit2SharpException ex)
            {
                throw new InvalidOperationException($"Failed to push branch '{branchName}' to remote '{_remoteName}'. {ex.Message}", ex);
            }
        }

        private void EnsureRemoteBranchPrepared(Repository repo, string branchName)
        {
            var remoteBranch = repo.Branches[$"refs/remotes/{_remoteName}/{branchName}"];
            if (remoteBranch != null)
            {
                return;
            }

            EnsureBranchExistsInternal(repo, branchName);
            TryFetchAll(repo);
        }

        private void EnsureBranchExistsInternal(Repository repo, string branchName)
        {
            var existing = repo.Branches[branchName] ?? repo.Branches[$"refs/heads/{branchName}"];
            if (existing != null)
            {
                return;
            }

            TryFetchAll(repo);

            Branch? remoteBranch = null;
            if (!string.IsNullOrWhiteSpace(_remoteName))
            {
                remoteBranch = repo.Branches[$"refs/remotes/{_remoteName}/{branchName}"];
            }

            if (remoteBranch != null)
            {
                var local = repo.CreateBranch(branchName, remoteBranch.Tip);
                repo.Branches.Update(local, b => b.TrackedBranch = remoteBranch.CanonicalName);
                return;
            }

            var newBranch = CreateEmptyBranch(repo, branchName);

            if (!string.IsNullOrWhiteSpace(_remoteName))
            {
                repo.Branches.Update(newBranch, updater =>
                {
                    updater.Remote = _remoteName;
                    updater.UpstreamBranch = $"refs/heads/{branchName}";
                });

                var remote = EnsureRemote(repo);
                var refSpec = $"refs/heads/{branchName}:refs/heads/{branchName}";
                PushBranch(repo, remote, branchName, refSpec);
                TryFetchAll(repo);
                var remoteCreated = repo.Branches[$"refs/remotes/{_remoteName}/{branchName}"];
                if (remoteCreated != null)
                {
                    repo.Branches.Update(newBranch, b => b.TrackedBranch = remoteCreated.CanonicalName);
                }
            }
        }

        private Branch CreateEmptyBranch(Repository repo, string branchName)
        {
            var author = new Signature(_authorName, _authorEmail, DateTimeOffset.Now);
            var tree = repo.ObjectDatabase.CreateTree(new TreeDefinition());
            var commit = repo.ObjectDatabase.CreateCommit(author, author, $"Initialize branch {branchName}", tree, Array.Empty<Commit>(), false);
            return repo.Branches.Add(branchName, commit);
        }

        private void TryFetchAll(Repository repo)
        {
            var remote = repo.Network.Remotes[_remoteName];
            if (remote == null)
            {
                return;
            }

            try
            {
                Commands.Fetch(repo, _remoteName, Array.Empty<string>(), CreateFetchOptions(), $"fetch {_remoteName}");
            }
            catch (LibGit2SharpException)
            {
                // metadata fetch only; ignore errors
            }
        }

        private static string NormalizeBranchName(Branch branch)
        {
            if (branch.IsRemote)
            {
                return branch.FriendlyName;
            }

            return branch.FriendlyName ?? branch.CanonicalName?.Replace("refs/heads/", string.Empty) ?? string.Empty;
        }

        private string? EnsureRepositoryReady(string? configuredRepoPath, string? cloneUrl)
        {
            if (!string.IsNullOrWhiteSpace(configuredRepoPath))
            {
                Directory.CreateDirectory(configuredRepoPath);
                if (!Repository.IsValid(configuredRepoPath))
                {
                    if (!string.IsNullOrWhiteSpace(cloneUrl))
                    {
                        PrepareCloneTargetDirectory(configuredRepoPath);
                        var fetch = CreateFetchOptions();
                        var cloneOptions = new CloneOptions(fetch);
                        Repository.Clone(cloneUrl, configuredRepoPath, cloneOptions);
                    }
                    else
                    {
                        Repository.Init(configuredRepoPath);
                        using var repo = new Repository(configuredRepoPath);
                        if (!repo.Commits.Any())
                        {
                            var keep = Path.Combine(configuredRepoPath, ".gitkeep");
                            if (!File.Exists(keep))
                            {
                                File.WriteAllText(keep, string.Empty);
                            }
                            Commands.Stage(repo, ".gitkeep");
                            var author = new Signature(_authorName, _authorEmail, DateTimeOffset.Now);
                            repo.Commit("Initial commit", author, author);
                        }
                    }
                }
                return configuredRepoPath;
            }

            if (string.IsNullOrWhiteSpace(cloneUrl))
            {
                return null;
            }

            var repoName = TryGetRepoNameFromUrl(cloneUrl) ?? "gitrepo";
            var defaultPath = DetermineDefaultRepoPath(repoName);
            Directory.CreateDirectory(defaultPath);
            if (!Repository.IsValid(defaultPath))
            {
                PrepareCloneTargetDirectory(defaultPath);
                var fetchDefault = CreateFetchOptions();
                var cloneOptionsDefault = new CloneOptions(fetchDefault);
                Repository.Clone(cloneUrl, defaultPath, cloneOptionsDefault);
            }
            return defaultPath;
        }

        private static void PrepareCloneTargetDirectory(string path)
        {
            Directory.CreateDirectory(path);
            if (Repository.IsValid(path))
            {
                return;
            }

            if (!Directory.EnumerateFileSystemEntries(path).Any())
            {
                return;
            }

            var fullPath = Path.GetFullPath(path);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
            var backupPath = $"{fullPath}.{timestamp}.bak";
            Directory.Move(fullPath, backupPath);
            Directory.CreateDirectory(fullPath);
        }

        private static string? FindRepoPath(string startPath)
        {
            var current = startPath;
            while (!string.IsNullOrEmpty(current))
            {
                if (Repository.IsValid(current))
                {
                    return current;
                }

                current = Directory.GetParent(current)?.FullName;
            }

            return null;
        }

        private static string DetermineDefaultRepoPath(string repoName)
        {
            var gitRoot = Path.Combine(Directory.GetCurrentDirectory(), "git");
            Directory.CreateDirectory(gitRoot);
            var target = Path.Combine(gitRoot, repoName);
            Directory.CreateDirectory(target);
            return target;
        }

        private static string? TryGetRepoNameFromUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            try
            {
                var trimmed = url.Split('?')[0].TrimEnd('/');
                var last = trimmed.Split('/').LastOrDefault();
                if (string.IsNullOrWhiteSpace(last))
                {
                    return null;
                }

                if (last.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                {
                    last = last.Substring(0, last.Length - 4);
                }

                return string.IsNullOrWhiteSpace(last) ? null : last;
            }
            catch
            {
                return null;
            }
        }
    }
}
