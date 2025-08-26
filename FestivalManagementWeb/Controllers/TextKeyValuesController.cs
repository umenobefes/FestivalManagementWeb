using FestivalManagementWeb.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace FestivalManagementWeb.Controllers
{
    [Authorize]
    public class TextKeyValuesController : Controller
    {
        private readonly IMongoCollection<TextKeyValue> _textCollection;

        public TextKeyValuesController(IMongoDatabase database)
        {
            _textCollection = database.GetCollection<TextKeyValue>("TextKeyValues");
        }

        public async Task<IActionResult> Index(Guid? id)
        {
            var viewModel = new TextKeyValueViewModel
            {
                AllItems = await _textCollection.Find(_ => true).ToListAsync(),
                ItemToEdit = new TextKeyValue()
            };

            if (id != null)
            {
                var item = await _textCollection.Find(x => x.Id == id).FirstOrDefaultAsync();
                if (item != null)
                {
                    viewModel.ItemToEdit = item;
                }
            }

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upsert(TextKeyValue textKeyValue)
        {
            var existingByKey = await _textCollection.Find(x => x.Key == textKeyValue.Key && x.Id != textKeyValue.Id).FirstOrDefaultAsync();
            if (existingByKey != null)
            {
                ModelState.AddModelError("ItemToEdit.Key", "このキーは既に使用されています。");
            }

            if (ModelState.IsValid)
            {
                if (textKeyValue.Id == Guid.Empty)
                {
                    textKeyValue.Id = Guid.NewGuid();
                    await _textCollection.InsertOneAsync(textKeyValue);
                }
                else
                {
                    await _textCollection.ReplaceOneAsync(x => x.Id == textKeyValue.Id, textKeyValue);
                }
                return RedirectToAction(nameof(Index));
            }

            var viewModel = new TextKeyValueViewModel
            {
                AllItems = await _textCollection.Find(_ => true).ToListAsync(),
                ItemToEdit = textKeyValue
            };
            return View("Index", viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _textCollection.DeleteOneAsync(x => x.Id == id);
            return RedirectToAction(nameof(Index));
        }
    }
}
