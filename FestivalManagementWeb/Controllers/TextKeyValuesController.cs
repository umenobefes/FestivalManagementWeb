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
            var allItems = await _textCollection.Find(_ => true).ToListAsync();
            System.Diagnostics.Debug.WriteLine($"Found {allItems.Count} documents in TextKeyValues collection.");

            var viewModel = new TextKeyValueViewModel
            {
                AllItems = allItems,
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
        public async Task<IActionResult> Upsert([Bind("ItemToEdit")] TextKeyValueViewModel model)
        {
            var textKeyValue = model.ItemToEdit;
            System.Diagnostics.Debug.WriteLine("Upsert method called.");
            var existingByKey = await _textCollection.Find(x => x.Key == textKeyValue.Key && x.Id != textKeyValue.Id).FirstOrDefaultAsync();
            if (existingByKey != null)
            {
                ModelState.AddModelError("ItemToEdit.Key", "このキーは既に使用されています。");
            }

            if (!ModelState.IsValid)
            {
                System.Diagnostics.Debug.WriteLine("ModelState is INVALID.");
                foreach (var modelState in ModelState.Values)
                {
                    foreach (var error in modelState.Errors)
                    {
                        System.Diagnostics.Debug.WriteLine(error.ErrorMessage);
                    }
                }
            }

            if (ModelState.IsValid)
            {
                System.Diagnostics.Debug.WriteLine("ModelState is VALID.");
                try
                {
                    if (textKeyValue.Id == Guid.Empty)
                    {
                        textKeyValue.Id = Guid.NewGuid();
                        System.Diagnostics.Debug.WriteLine($"Inserting new document with Id: {textKeyValue.Id}");
                        await _textCollection.InsertOneAsync(textKeyValue);
                        System.Diagnostics.Debug.WriteLine("Insert operation completed.");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Replacing document with Id: {textKeyValue.Id}");
                        var result = await _textCollection.ReplaceOneAsync(x => x.Id == textKeyValue.Id, textKeyValue);
                        System.Diagnostics.Debug.WriteLine($"Replace result: Matched={result.MatchedCount}, Modified={result.ModifiedCount}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"DATABASE OPERATION FAILED: {ex.Message}");
                    // Optionally log the full stack trace:
                    // System.Diagnostics.Debug.WriteLine(ex.ToString());
                    ModelState.AddModelError(string.Empty, "データベース操作中にエラーが発生しました。");
                    // Re-populate the view model for returning to the view
                    model.AllItems = await _textCollection.Find(_ => true).ToListAsync();
                    return View("Index", model);
                }
                return RedirectToAction(nameof(Index));
            }

            // If ModelState is invalid, repopulate the view model and return to the view
            // If ModelState is invalid, repopulate the AllItems list and return to the view
            model.AllItems = await _textCollection.Find(_ => true).ToListAsync();
            return View("Index", model);
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
