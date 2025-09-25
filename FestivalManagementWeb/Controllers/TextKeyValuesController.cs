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
        public async Task<IActionResult> Upsert([Bind("ItemToEdit")] TextKeyValueViewModel model)
        {
            var selectedYear = await _yearBranchService.GetCurrentYearAsync();
            var textKeyValue = model.ItemToEdit;
            textKeyValue.Year = selectedYear;

            if (textKeyValue.Id != Guid.Empty)
            {
                var existingById = await _textRepository.GetByIdAsync(textKeyValue.Id);
                if (existingById == null || existingById.Year != selectedYear)
                {
                    ModelState.AddModelError(string.Empty, "The requested item is not available for the current year.");
                }
            }

            var existingByKey = await _textRepository.GetByKeyAsync(textKeyValue.Key, selectedYear);
            if (existingByKey != null && existingByKey.Id != textKeyValue.Id)
            {
                ModelState.AddModelError("ItemToEdit.Key", "The provided key already exists for the selected year.");
            }

            if (ModelState.IsValid)
            {
                if (textKeyValue.Id == Guid.Empty)
                {
                    textKeyValue.Id = Guid.NewGuid();
                    await _textRepository.CreateAsync(textKeyValue);
                }
                else
                {
                    await _textRepository.UpdateAsync(textKeyValue);
                }
                return RedirectToAction(nameof(Index));
            }

            model.AllItems = await _textRepository.GetAllAsync(selectedYear);
            model.SelectedYear = selectedYear;
            ViewData["SelectedYear"] = selectedYear;
            return View("Index", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id)
        {
            var selectedYear = await _yearBranchService.GetCurrentYearAsync();
            var item = await _textRepository.GetByIdAsync(id);
            if (item != null && item.Year == selectedYear)
            {
                await _textRepository.DeleteAsync(id);
            }
            else
            {
                TempData["Error"] = "Unable to delete the requested item.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}














