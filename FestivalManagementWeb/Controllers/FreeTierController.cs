using System;
using System.Linq;
using FestivalManagementWeb.Models;
using System.Threading.Tasks;
using FestivalManagementWeb.Repositories;
using FestivalManagementWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FestivalManagementWeb.Controllers
{
    [Authorize]
    public class FreeTierController : Controller
    {
        private readonly IFreeTierService _freeTierService;
        private readonly ITextKeyValueRepository _textRepository;
        private readonly IImageKeyValueRepository _imageRepository;
        private readonly ILogger<FreeTierController> _logger;

        public FreeTierController(
            IFreeTierService freeTierService,
            ITextKeyValueRepository textRepository,
            IImageKeyValueRepository imageRepository,
            ILogger<FreeTierController> logger)
        {
            _freeTierService = freeTierService;
            _textRepository = textRepository;
            _imageRepository = imageRepository;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> CapacityLimit()
        {
            var info = _freeTierService.GetInfo();
            var cosmos = info?.Cosmos;

            if (cosmos == null || cosmos.Enabled != true)
            {
                return RedirectToAction("Index", "Home");
            }

            var textYears = await _textRepository.GetDistinctYearsAsync();
            var imageYears = await _imageRepository.GetDistinctYearsAsync();
            var availableYears = textYears
                .Concat(imageYears)
                .Distinct()
                .OrderByDescending(y => y)
                .ToList();

            var viewModel = new CosmosCapacityViewModel
            {
                Cosmos = cosmos,
                BannerEnabled = info?.Enabled == true,
                AvailableYears = availableYears,
                CleanupMessage = TempData["CleanupMessage"] as string
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTextYear(int year)
        {
            if (year <= 0)
            {
                TempData["CleanupMessage"] = "年度を選択してください";
                return RedirectToAction(nameof(CapacityLimit));
            }

            try
            {
                var deleted = await _textRepository.DeleteByYearAsync(year);
                TempData["CleanupMessage"] = deleted > 0
                    ? $"{year} 年度のテキストデータを {deleted} 件削除しました。"
                    : $"{year} 年度のテキストデータは見つかりませんでした。";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete text data for year {Year}", year);
                TempData["CleanupMessage"] = $"{year} 年度のテキストデータの削除に失敗しました。";
            }

            return RedirectToAction(nameof(CapacityLimit));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteImageYear(int year)
        {
            if (year <= 0)
            {
                TempData["CleanupMessage"] = "年度を選択してください";
                return RedirectToAction(nameof(CapacityLimit));
            }

            try
            {
                var deleted = await _imageRepository.DeleteByYearAsync(year);
                TempData["CleanupMessage"] = deleted > 0
                    ? $"{year} 年度の画像データを {deleted} 件削除しました。関連するバイナリも削除されています。"
                    : $"{year} 年度の画像データは見つかりませんでした。";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete image data for year {Year}", year);
                TempData["CleanupMessage"] = $"{year} 年度の画像データの削除に失敗しました。";
            }

            return RedirectToAction(nameof(CapacityLimit));
        }
    }
}
