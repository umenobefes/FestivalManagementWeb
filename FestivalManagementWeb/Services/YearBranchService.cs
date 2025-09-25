using FestivalManagementWeb.Models;
using FestivalManagementWeb.Repositories;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Driver.GridFS;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FestivalManagementWeb.Services
{
    public class YearBranchService : IYearBranchService
    {
        private const string YearCookieName = "FestivalManagement.SelectedYear";
        private readonly IGitService _gitService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ITextKeyValueRepository _textRepository;
        private readonly IImageKeyValueRepository _imageRepository;
        private readonly IGridFSBucket _gridFsBucket;

        public YearBranchService(
            IGitService gitService,
            IHttpContextAccessor httpContextAccessor,
            ITextKeyValueRepository textRepository,
            IImageKeyValueRepository imageRepository,
            IGridFSBucket gridFsBucket)
        {
            _gitService = gitService;
            _httpContextAccessor = httpContextAccessor;
            _textRepository = textRepository;
            _imageRepository = imageRepository;
            _gridFsBucket = gridFsBucket;
        }

        public async Task<int> GetCurrentYearAsync()
        {
            var context = RequireHttpContext();
            var availableYears = await GetAvailableYearsInternalAsync(ensureCurrentIfMissing: true);

            if (TryGetYearFromCookie(context, availableYears, out var cookieYear))
            {
                return cookieYear;
            }

            var currentYear = DateTime.UtcNow.Year;
            await SetYearCookieAsync(context, currentYear);
            return currentYear;
        }

        public async Task SetCurrentYearAsync(int year)
        {
            ValidateYear(year);
            var context = RequireHttpContext();
            await SetYearCookieAsync(context, year);
        }

        public async Task<IReadOnlyList<int>> GetAvailableYearsAsync()
        {
            return await GetAvailableYearsInternalAsync(ensureCurrentIfMissing: true);
        }

        public async Task<int> CreateNextYearBranchAsync()
        {
            var currentYear = await GetCurrentYearAsync();
            var nextYear = checked(currentYear + 1);
            ValidateYear(nextYear);
            await CopyPreviousYearContentAsync(currentYear, nextYear);

            return nextYear;
        }

        public async Task EnsureYearBranchExistsAsync(int year)
        {
            ValidateYear(year);
            await _gitService.EnsureBranchExistsAsync(year.ToString(CultureInfo.InvariantCulture));
        }

        private async Task<List<int>> GetAvailableYearsInternalAsync(bool ensureCurrentIfMissing)
        {
            var gitBranchTask = _gitService.GetBranchNamesAsync();
            var textYearsTask = _textRepository.GetDistinctYearsAsync();
            var imageYearsTask = _imageRepository.GetDistinctYearsAsync();

            await Task.WhenAll(gitBranchTask, textYearsTask, imageYearsTask);

            var years = new HashSet<int>();

            foreach (var branch in gitBranchTask.Result)
            {
                var parsed = TryParseYear(branch);
                if (parsed.HasValue)
                {
                    years.Add(parsed.Value);
                }
            }

            foreach (var y in textYearsTask.Result ?? Array.Empty<int>())
            {
                if (IsSupportedYear(y))
                {
                    years.Add(y);
                }
            }

            foreach (var y in imageYearsTask.Result ?? Array.Empty<int>())
            {
                if (IsSupportedYear(y))
                {
                    years.Add(y);
                }
            }

            if (ensureCurrentIfMissing)
            {
                years.Add(DateTime.UtcNow.Year);
            }

            return years
                .Where(IsSupportedYear)
                .OrderByDescending(y => y)
                .ToList();
        }

        private bool TryGetYearFromCookie(HttpContext context, IReadOnlyCollection<int> availableYears, out int year)
        {
            year = default;
            if (context.Request.Cookies.TryGetValue(YearCookieName, out var raw)
                && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                && availableYears.Contains(parsed))
            {
                year = parsed;
                return true;
            }

            return false;
        }

        private Task SetYearCookieAsync(HttpContext context, int year)
        {
            context.Response.Cookies.Append(
                YearCookieName,
                year.ToString(CultureInfo.InvariantCulture),
                new CookieOptions
                {
                    HttpOnly = false,
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = context.Request.IsHttps,
                    Expires = DateTimeOffset.UtcNow.AddYears(1)
                });

            return Task.CompletedTask;
        }

        private HttpContext RequireHttpContext()
        {
            return _httpContextAccessor.HttpContext
                   ?? throw new InvalidOperationException("HTTP context is not available.");
        }

        private async Task CopyPreviousYearContentAsync(int sourceYear, int targetYear)
        {
            if (sourceYear == targetYear)
            {
                return;
            }

            var targetHasText = (await _textRepository.GetAllAsync(targetYear)).Any();
            var targetHasImages = (await _imageRepository.GetAllAsync(targetYear)).Any();
            if (targetHasText || targetHasImages)
            {
                return;
            }

            var sourceTexts = (await _textRepository.GetAllAsync(sourceYear)).ToList();
            foreach (var text in sourceTexts)
            {
                var clone = new TextKeyValue
                {
                    Id = Guid.NewGuid(),
                    Year = targetYear,
                    Key = text.Key,
                    Value = text.Value,
                    Deployed = false,
                    DeployedDate = null
                };

                await _textRepository.CreateAsync(clone);
            }

            var sourceImages = (await _imageRepository.GetAllAsync(sourceYear)).ToList();
            foreach (var image in sourceImages)
            {
                var clone = new ImageKeyValue
                {
                    Id = Guid.NewGuid(),
                    Year = targetYear,
                    Key = image.Key,
                    Deployed = false,
                    DeployedDate = null,
                    GridFSFileId = ObjectId.Empty
                };

                if (image.GridFSFileId != ObjectId.Empty)
                {
                    clone.GridFSFileId = await DuplicateGridFsFileAsync(image.GridFSFileId);
                }

                await _imageRepository.CreateAsync(clone);
            }
        }

        private async Task<ObjectId> DuplicateGridFsFileAsync(ObjectId sourceFileId)
        {
            try
            {
                var downloadStream = await _gridFsBucket.OpenDownloadStreamAsync(sourceFileId);
                using (downloadStream)
                {
                    using var buffer = new MemoryStream();
                    await downloadStream.CopyToAsync(buffer);
                    buffer.Position = 0;

                    var originalName = downloadStream.FileInfo?.Filename;
                    var newFileName = string.IsNullOrWhiteSpace(originalName)
                        ? $"{Guid.NewGuid():N}"
                        : $"{Path.GetFileNameWithoutExtension(originalName)}-{Guid.NewGuid():N}{Path.GetExtension(originalName)}";

                    var newId = await _gridFsBucket.UploadFromStreamAsync(newFileName, buffer);
                    return newId;
                }
            }
            catch (GridFSFileNotFoundException)
            {
                return ObjectId.Empty;
            }
        }

        private static int? TryParseYear(string? branchName)
        {
            if (string.IsNullOrWhiteSpace(branchName))
            {
                return null;
            }

            if (!int.TryParse(branchName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
            {
                return null;
            }

            return IsSupportedYear(year) ? year : null;
        }

        private static void ValidateYear(int year)
        {
            if (!IsSupportedYear(year))
            {
                throw new ArgumentOutOfRangeException(nameof(year), $"Year '{year}' is outside the supported range (2000-2100).");
            }
        }

        private static bool IsSupportedYear(int year) => year >= 2000 && year <= 2100;
    }
}








