using FestivalManagementWeb.Models;
using FestivalManagementWeb.Repositories;
using FestivalManagementWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver.GridFS;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;

namespace FestivalManagementWeb.Controllers
{
    [Authorize]
    public class DeployController : Controller
    {
        private readonly ITextKeyValueRepository _textRepo;
        private readonly IImageKeyValueRepository _imageRepo;
        private readonly IGridFSBucket _bucket;
        private readonly IWebHostEnvironment _env;
        private readonly IGitService _git;
        private readonly IConfiguration _config;
        private readonly IYearBranchService _yearBranchService;

        public DeployController(
            ITextKeyValueRepository textRepo,
            IImageKeyValueRepository imageRepo,
            IGridFSBucket bucket,
            IWebHostEnvironment env,
            IGitService git,
            IYearBranchService yearBranchService,
            IConfiguration config)
        {
            _textRepo = textRepo;
            _imageRepo = imageRepo;
            _bucket = bucket;
            _env = env;
            _git = git;
            _yearBranchService = yearBranchService;
            _config = config;
        }

        // POST /Deploy/Run
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Run(string? returnUrl)
        {
            var selectedYear = await _yearBranchService.GetCurrentYearAsync();
            await _yearBranchService.EnsureYearBranchExistsAsync(selectedYear);
            var branchName = selectedYear.ToString(CultureInfo.InvariantCulture);

            var remoteExists = await _git.EnsureRemoteBranchAsync(branchName);

            if (remoteExists)
            {
                await _git.PullLatest(branchName);
            }

            var lastCommitWhen = await _git.GetLastCommitDateAsync(branchName);
            DateTime? lastCommitUtc = lastCommitWhen?.UtcDateTime;

            var textItems = await _textRepo.GetAllAsync(selectedYear);
            var imageItems = await _imageRepo.GetAllAsync(selectedYear);

            var textDto = textItems
                .OrderBy(x => x.Key)
                .Select(x => new { name = x.Key, text = x.Value })
                .ToList();

            var imageList = new List<object>();
            foreach (var img in imageItems.OrderBy(x => x.Key))
            {
                string filename = string.Empty;
                try
                {
                    var filter = MongoDB.Driver.Builders<GridFSFileInfo>.Filter.Eq(x => x.Id, img.GridFSFileId);
                    using var cursor = await _bucket.FindAsync(filter);
                    GridFSFileInfo info = null;
                    while (await cursor.MoveNextAsync())
                    {
                        info = cursor.Current.FirstOrDefault();
                        if (info != null) break;
                    }
                    filename = info?.Filename ?? string.Empty;
                }
                catch
                {
                    filename = string.Empty;
                }
                imageList.Add(new { name = img.Key, filename });
            }

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            };

            var repoRoot = _git.GetOutputRoot(branchName);
            var branchFolderName = SanitizeFolderName(branchName);
            var legacyBranchDir = Path.Combine(repoRoot, branchFolderName);
            if (Directory.Exists(legacyBranchDir) && !string.Equals(legacyBranchDir, repoRoot, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(legacyBranchDir, recursive: true);
            }

            ResetOutputDirectory(repoRoot);

            var textPath = Path.Combine(repoRoot, "TextKeyValues.json");
            var imagePath = Path.Combine(repoRoot, "ImageKeyValues.json");
            var enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            await System.IO.File.WriteAllTextAsync(textPath, JsonSerializer.Serialize(textDto, jsonOptions), enc);
            await System.IO.File.WriteAllTextAsync(imagePath, JsonSerializer.Serialize(imageList, jsonOptions), enc);

            var orderedImages = imageItems.OrderBy(x => x.Key).ToList();

            foreach (var img in orderedImages)
            {
                if (img.GridFSFileId == default) continue;
                try
                {
                    using var stream = await _bucket.OpenDownloadStreamAsync(img.GridFSFileId);
                    var info = stream.FileInfo;
                    var filename = info?.Filename ?? (img.Key + ".bin");
                    var dstPath = Path.Combine(repoRoot, filename);
                    using var fs = System.IO.File.Create(dstPath);
                    await stream.CopyToAsync(fs);
                }
                catch (GridFSFileNotFoundException)
                {
                    // skip if missing in GridFS
                }
            }

            var now = DateTime.UtcNow;
            foreach (var t in textItems)
            {
                t.Deployed = true;
                t.DeployedDate = now;
                await _textRepo.UpdateAsync(t);
            }
            foreach (var image in imageItems)
            {
                image.Deployed = true;
                image.DeployedDate = now;
                await _imageRepo.UpdateAsync(image);
            }

            var commitMessage = $"Publish: export JSON and images ({DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC) [{branchName}]";
            await _git.CommitAndPushChanges(commitMessage, branchName);

            TempData["Message"] = $"Pull後、output配下にJSONと画像を書き出し、Commit & Pushしました。デプロイ済みフラグと日時も更新しました。（ブランチ: {branchName}）";

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction("Index", "Home");
        }

        private static void ResetOutputDirectory(string root)
        {
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
            }

            foreach (var file in Directory.EnumerateFiles(root))
            {
                var name = Path.GetFileName(file);
                if (string.Equals(name, ".gitkeep", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                System.IO.File.Delete(file);
            }

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(directory);
                if (string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (string.Equals(name, "images", StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        private static string SanitizeFolderName(string branchName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(branchName.Where(ch => !invalid.Contains(ch)).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "default" : sanitized;
        }
    }
}


