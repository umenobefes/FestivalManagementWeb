using FestivalManagementWeb.Models;
using FestivalManagementWeb.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
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
        private readonly IImageKeyValueRepository _imageRepository;
        private readonly IGridFSBucket _bucket;
        private const int MaxDimension = 1920;

        public ImageKeyValuesController(IImageKeyValueRepository imageRepository, IGridFSBucket bucket)
        {
            _imageRepository = imageRepository;
            _bucket = bucket;
        }

        public async Task<IActionResult> Index(Guid? id)
        {
            var allItems = await _imageRepository.GetAllAsync();
            var viewModel = new ImageKeyValueViewModel
            {
                AllItems = allItems,
                ItemToEdit = new ImageKeyValue()
            };

            if (id != null)
            {
                var item = await _imageRepository.GetByIdAsync(id.Value);
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

            var existingByKey = await _imageRepository.GetByKeyAsync(imageKeyValue.Key);
            if (existingByKey != null && existingByKey.Id != imageKeyValue.Id)
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
                    if (imageKeyValue.Id != Guid.Empty)
                    {
                        var existingItem = await _imageRepository.GetByIdAsync(imageKeyValue.Id);
                        if (existingItem != null && existingItem.GridFSFileId != ObjectId.Empty)
                        {
                            await _bucket.DeleteAsync(existingItem.GridFSFileId);
                        }
                    }

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
                    await _imageRepository.CreateAsync(imageKeyValue);
                }
                else
                {
                    if (newGridFSFileId == null)
                    {
                        var existingItem = await _imageRepository.GetByIdAsync(imageKeyValue.Id);
                        imageKeyValue.GridFSFileId = existingItem?.GridFSFileId ?? ObjectId.Empty;
                    }
                    await _imageRepository.UpdateAsync(imageKeyValue);
                }
                return RedirectToAction(nameof(Index));
            }

            model.AllItems = await _imageRepository.GetAllAsync();
            return View("Index", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id)
        {
            var itemToDelete = await _imageRepository.GetByIdAsync(id);
            if (itemToDelete != null)
            {
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
                await _imageRepository.DeleteAsync(id);
            }
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> GetImage(Guid id)
        {
            var item = await _imageRepository.GetByIdAsync(id);
            if (item == null || item.GridFSFileId == ObjectId.Empty)
            {
                return NotFound();
            }

            try
            {
                var stream = await _bucket.OpenDownloadStreamAsync(item.GridFSFileId);
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


