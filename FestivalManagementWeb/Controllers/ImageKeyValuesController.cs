using FestivalManagementWeb.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using SkiaSharp;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FestivalManagementWeb.Controllers
{
    [Authorize]
    public class ImageKeyValuesController : Controller
    {
        private readonly IMongoCollection<ImageKeyValue> _imageCollection;
        private const int MaxDimension = 1920;

        public ImageKeyValuesController(IMongoDatabase database)
        {
            _imageCollection = database.GetCollection<ImageKeyValue>("ImageKeyValues");
        }

        public async Task<IActionResult> Index()
        {
            var viewModel = new ImageKeyValueViewModel
            {
                AllItems = await _imageCollection.Find(_ => true).ToListAsync()
            };
            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upsert(string key, IFormFile imageFile)
        {
            if (string.IsNullOrEmpty(key) || imageFile == null || imageFile.Length == 0)
            {
                ModelState.AddModelError("", "キーと画像ファイルの両方を指定してください。");
            }
            else
            {
                using (var memoryStream = new MemoryStream())
                {
                    await imageFile.CopyToAsync(memoryStream);
                    var imageBytes = ResizeImage(memoryStream.ToArray());

                    var existing = await _imageCollection.Find(x => x.Key == key).FirstOrDefaultAsync();
                    if (existing != null)
                    {
                        existing.Value = imageBytes;
                        existing.ContentType = imageFile.ContentType;
                        await _imageCollection.ReplaceOneAsync(x => x.Id == existing.Id, existing);
                    }
                    else
                    {
                        var newImage = new ImageKeyValue
                        {
                            Key = key,
                            Value = imageBytes,
                            ContentType = imageFile.ContentType
                        };
                        await _imageCollection.InsertOneAsync(newImage);
                    }
                    return RedirectToAction(nameof(Index));
                }
            }

            var viewModel = new ImageKeyValueViewModel
            {
                AllItems = await _imageCollection.Find(_ => true).ToListAsync()
            };
            return View("Index", viewModel);
        }

        private byte[] ResizeImage(byte[] originalBytes)
        {
            using (var inputStream = new SKMemoryStream(originalBytes))
            using (var originalBitmap = SKBitmap.Decode(inputStream))
            {
                if (originalBitmap.Width <= MaxDimension && originalBitmap.Height <= MaxDimension)
                {
                    return originalBytes;
                }

                int newWidth, newHeight;
                if (originalBitmap.Width > originalBitmap.Height)
                {
                    newWidth = MaxDimension;
                    newHeight = (int)(originalBitmap.Height * ((float)MaxDimension / originalBitmap.Width));
                }
                else
                {
                    newHeight = MaxDimension;
                    newWidth = (int)(originalBitmap.Width * ((float)MaxDimension / originalBitmap.Height));
                }

                var imageInfo = new SKImageInfo(newWidth, newHeight);
                using (var resizedBitmap = new SKBitmap(imageInfo))
                {
                    originalBitmap.ScalePixels(resizedBitmap, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                    using (var image = SKImage.FromBitmap(resizedBitmap))
                    using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 90))
                    {
                        return data.ToArray();
                    }
                }
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _imageCollection.DeleteOneAsync(x => x.Id == id);
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> GetImage(Guid id)
        {
            var image = await _imageCollection.Find(x => x.Id == id).FirstOrDefaultAsync();
            if (image == null)
            {
                return NotFound();
            }
            return File(image.Value, image.ContentType);
        }
    }
}

