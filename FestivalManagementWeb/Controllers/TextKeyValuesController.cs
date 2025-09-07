using FestivalManagementWeb.Models;
using FestivalManagementWeb.Repositories;
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

        public TextKeyValuesController(ITextKeyValueRepository textRepository)
        {
            _textRepository = textRepository;
        }

        public async Task<IActionResult> Index(Guid? id)
        {
            var allItems = await _textRepository.GetAllAsync();
            var viewModel = new TextKeyValueViewModel
            {
                AllItems = allItems,
                ItemToEdit = new TextKeyValue()
            };

            if (id != null)
            {
                var item = await _textRepository.GetByIdAsync(id.Value);
                if (item != null)
                {
                    viewModel.ItemToEdit = item;
                }
            }

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upsert([Bind("ItemToEdit")] TextKeyValueViewModel model)
        {
            var textKeyValue = model.ItemToEdit;
            var existingByKey = await _textRepository.GetByKeyAsync(textKeyValue.Key);
            if (existingByKey != null && existingByKey.Id != textKeyValue.Id)
            {
                ModelState.AddModelError("ItemToEdit.Key", "このキーは既に使用されています。");
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

            model.AllItems = await _textRepository.GetAllAsync();
            return View("Index", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _textRepository.DeleteAsync(id);
            return RedirectToAction(nameof(Index));
        }
    }
}
