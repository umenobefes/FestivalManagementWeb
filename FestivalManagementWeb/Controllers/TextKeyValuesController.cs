using FestivalManagementWeb.Models;
using FestivalManagementWeb.Repositories;
using FestivalManagementWeb.Filters;
using FestivalManagementWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace FestivalManagementWeb.Controllers
{
    [Authorize]
    public class TextKeyValuesController : Controller
    {
        private readonly ITextKeyValueRepository _textRepository;
        private readonly IYearBranchService _yearBranchService;

        public TextKeyValuesController(ITextKeyValueRepository textRepository, IYearBranchService yearBranchService)
        {
            _textRepository = textRepository;
            _yearBranchService = yearBranchService;
        }

        public async Task<IActionResult> Index(Guid? id)
        {
            var selectedYear = await _yearBranchService.GetCurrentYearAsync();
            var allItems = await _textRepository.GetAllAsync(selectedYear);

            var viewModel = new TextKeyValueViewModel
            {
                AllItems = allItems,
                ItemToEdit = new TextKeyValue { Year = selectedYear },
                SelectedYear = selectedYear
            };

            if (id.HasValue)
            {
                var item = await _textRepository.GetByIdAsync(id.Value);
                if (item != null && item.Year == selectedYear)
                {
                    viewModel.ItemToEdit = item;
                }
            }

            ViewData["SelectedYear"] = selectedYear;
            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [CosmosCapacityGuard]
        public async Task<IActionResult> Upsert(TextKeyValue model, string? returnUrl)
        {
            var selectedYear = await _yearBranchService.GetCurrentYearAsync();
            model.Year = selectedYear;

            if (model.Id != Guid.Empty)
            {
                var existingById = await _textRepository.GetByIdAsync(model.Id);
                if (existingById == null || existingById.Year != selectedYear)
                {
                    TempData["Error"] = "指定されたアイテムは現在の年度で利用できません。";
                    if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    {
                        return Redirect(returnUrl);
                    }
                    return RedirectToAction(nameof(Index));
                }
            }

            var existingByKey = await _textRepository.GetByKeyAsync(model.Key, selectedYear);
            if (existingByKey != null && existingByKey.Id != model.Id)
            {
                TempData["Error"] = $"キー「{model.Key}」は既に存在します。";
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }
                return RedirectToAction(nameof(Index));
            }

            if (model.Id == Guid.Empty)
            {
                model.Id = Guid.NewGuid();
                await _textRepository.CreateAsync(model);
                TempData["Message"] = $"「{model.Key}」を追加しました。";
            }
            else
            {
                await _textRepository.UpdateAsync(model);
                TempData["Message"] = $"「{model.Key}」を更新しました。";
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id, string? returnUrl)
        {
            var selectedYear = await _yearBranchService.GetCurrentYearAsync();
            var item = await _textRepository.GetByIdAsync(id);
            if (item != null && item.Year == selectedYear)
            {
                await _textRepository.DeleteAsync(id);
                TempData["Message"] = $"「{item.Key}」を削除しました。";
            }
            else
            {
                TempData["Error"] = "削除対象のアイテムが見つかりません。";
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(Index));
        }

    }
}














