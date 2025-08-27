using FestivalManagementWeb.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using SkiaSharp;
using System;
using System.IO;
using System.Threading.Tasks;

namespace FestivalManagementWeb.Controllers
{
    [Authorize]
    public class ImageKeyValuesController : Controller
    {
        private readonly IMongoCollection<ImageKeyValue> _imageCollection;
        private readonly IGridFSBucket _bucket;
        private const int MaxDimension = 1920;

        public ImageKeyValuesController(IMongoDatabase database, IGridFSBucket bucket)
        {
            _imageCollection = database.GetCollection<ImageKeyValue>("ImageKeyValues");
            _bucket = bucket;
        }

        public async Task<IActionResult> Index(Guid? id)
        {
            var allItems = await _imageCollection.Find(_ => true).ToListAsync();
            var viewModel = new ImageKeyValueViewModel
            {
                AllItems = allItems,
                ItemToEdit = new ImageKeyValue()
            };

            if (id != null)
            {
                var item = await _imageCollection.Find(x => x.Id == id).FirstOrDefaultAsync();
                if (item != null)
                {
                    viewModel.ItemToEdit = item;
                }
            }

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upsert(ImageKeyValueViewModel model)
        {
            var imageKeyValue = model.ItemToEdit;

            var existingByKey = await _imageCollection.Find(x => x.Key == imageKeyValue.Key && x.Id != imageKeyValue.Id).FirstOrDefaultAsync();
            if (existingByKey != null)
            {
                ModelState.AddModelError("ItemToEdit.Key", "このキーは既に使用されています。");
            }

            if (model.ImageFile == null && imageKeyValue.Id == Guid.Empty)
            {
                ModelState.AddModelError("ImageFile", "画像をアップロードしてください。");
            }

            if (ModelState.IsValid)
            {
                ObjectId? newGridFSFileId = null;

                if (model.ImageFile != null && model.ImageFile.Length > 0)
                {
                    // If updating, delete the old file from GridFS
                    if (imageKeyValue.Id != Guid.Empty)
                    {
                        var existingItem = await _imageCollection.Find(x => x.Id == imageKeyValue.Id).FirstOrDefaultAsync();
                        if (existingItem != null && existingItem.GridFSFileId != ObjectId.Empty)
                        {
                            await _bucket.DeleteAsync(existingItem.GridFSFileId);
                        }
                    }

                    // Resize and upload the new image to GridFS
                    using (var memoryStream = new MemoryStream())
                    {
                        await model.ImageFile.CopyToAsync(memoryStream);
                        var imageBytes = ResizeImage(memoryStream.ToArray());
                        
                        newGridFSFileId = await _bucket.UploadFromBytesAsync(model.ImageFile.FileName, imageBytes);
                        imageKeyValue.GridFSFileId = newGridFSFileId.Value;
                    }
                }

                if (imageKeyValue.Id == Guid.Empty)
                {
                    imageKeyValue.Id = Guid.NewGuid();
                    await _imageCollection.InsertOneAsync(imageKeyValue);
                }
                else
                {
                    // If no new file was uploaded, keep the existing GridFS file ID
                    if (newGridFSFileId == null)
                    {
                        var existingItem = await _imageCollection.Find(x => x.Id == imageKeyValue.Id).FirstOrDefaultAsync();
                        imageKeyValue.GridFSFileId = existingItem?.GridFSFileId ?? ObjectId.Empty;
                    }
                    await _imageCollection.ReplaceOneAsync(x => x.Id == imageKeyValue.Id, imageKeyValue);
                }
                return RedirectToAction(nameof(Index));
            }

            model.AllItems = await _imageCollection.Find(_ => true).ToListAsync();
            return View("Index", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id)
        {
            var itemToDelete = await _imageCollection.Find(x => x.Id == id).FirstOrDefaultAsync();
            if (itemToDelete != null)
            {
                // Delete from GridFS only if the ID is valid
                if (itemToDelete.GridFSFileId != ObjectId.Empty)
                {
                    try
                    {
                        await _bucket.DeleteAsync(itemToDelete.GridFSFileId);
                    }
                    catch (GridFSFileNotFoundException)
                    {
                        // Ignore if the file is already missing in GridFS for some reason
                    }
                }
                // Always delete the metadata document
                await _imageCollection.DeleteOneAsync(x => x.Id == id);
            }
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> GetImage(Guid id)
        {
            var item = await _imageCollection.Find(x => x.Id == id).FirstOrDefaultAsync();
            if (item == null || item.GridFSFileId == ObjectId.Empty)
            {
                return NotFound();
            }

            try
            {
                var stream = await _bucket.OpenDownloadStreamAsync(item.GridFSFileId);
                // The resized image is always JPEG
                return File(stream, "image/jpeg");
            }
            catch (GridFSFileNotFoundException)
            {
                return NotFound();
            }
        }

        private byte[] ResizeImage(byte[] originalBytes)
        {
            using (var inputStream = new SKMemoryStream(originalBytes))
            using (var originalBitmap = SKBitmap.Decode(inputStream))
            {
                if (originalBitmap.Width <= MaxDimension && originalBitmap.Height <= MaxDimension)
                {
                    using (var image = SKImage.FromBitmap(originalBitmap))
                    using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 90))
                    {
                        return data.ToArray();
                    }
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
    }
}

