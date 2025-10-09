using FestivalManagementWeb.Models;
using FestivalManagementWeb.Repositories;
using FestivalManagementWeb.Filters;
using FestivalManagementWeb.Services;
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
        private readonly IYearBranchService _yearBranchService;
        private const int MaxDimension = 1920;
        private const int DeliveryMaxDimension = 854;

        public ImageKeyValuesController(IImageKeyValueRepository imageRepository, IGridFSBucket bucket, IYearBranchService yearBranchService)
        {
            _imageRepository = imageRepository;
            _bucket = bucket;
            _yearBranchService = yearBranchService;
        }

        public async Task<IActionResult> Index(Guid? id)
        {
            var selectedYear = await _yearBranchService.GetCurrentYearAsync();
            var allItems = await _imageRepository.GetAllAsync(selectedYear);
            var viewModel = new ImageKeyValueViewModel
            {
                AllItems = allItems,
                ItemToEdit = new ImageKeyValue { Year = selectedYear },
                SelectedYear = selectedYear
            };

            if (id.HasValue)
            {
                var item = await _imageRepository.GetByIdAsync(id.Value);
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
        public async Task<IActionResult> Upsert(ImageKeyValueViewModel model, string? returnUrl)
        {
            var selectedYear = await _yearBranchService.GetCurrentYearAsync();
            var imageKeyValue = model.ItemToEdit;
            imageKeyValue.Year = selectedYear;

            if (imageKeyValue.Id != Guid.Empty)
            {
                var existingById = await _imageRepository.GetByIdAsync(imageKeyValue.Id);
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

            var existingByKey = await _imageRepository.GetByKeyAsync(imageKeyValue.Key, selectedYear);
            if (existingByKey != null && existingByKey.Id != imageKeyValue.Id)
            {
                TempData["Error"] = $"キー「{imageKeyValue.Key}」は既に存在します。";
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }
                return RedirectToAction(nameof(Index));
            }

            if (model.ImageFile == null && imageKeyValue.Id == Guid.Empty)
            {
                TempData["Error"] = "画像ファイルをアップロードしてください。";
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }
                return RedirectToAction(nameof(Index));
            }

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
                    var imageBytes = ResizeImage(memoryStream.ToArray(), MaxDimension);
                    var guidFileName = $"{Guid.NewGuid():N}.png";
                    newGridFSFileId = await _bucket.UploadFromBytesAsync(guidFileName, imageBytes);
                    imageKeyValue.GridFSFileId = newGridFSFileId.Value;
                }
            }

            if (imageKeyValue.Id == Guid.Empty)
            {
                imageKeyValue.Id = Guid.NewGuid();
                await _imageRepository.CreateAsync(imageKeyValue);
                TempData["Message"] = $"「{imageKeyValue.Key}」を追加しました。";
            }
            else
            {
                if (newGridFSFileId == null)
                {
                    var existingItem = await _imageRepository.GetByIdAsync(imageKeyValue.Id);
                    imageKeyValue.GridFSFileId = existingItem?.GridFSFileId ?? ObjectId.Empty;
                }
                await _imageRepository.UpdateAsync(imageKeyValue);
                TempData["Message"] = $"「{imageKeyValue.Key}」を更新しました。";
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
            var itemToDelete = await _imageRepository.GetByIdAsync(id);
            if (itemToDelete != null && itemToDelete.Year == selectedYear)
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
                TempData["Message"] = $"「{itemToDelete.Key}」を削除しました。";
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


        public async Task<IActionResult> GetImage(Guid id)
        {
            var item = await _imageRepository.GetByIdAsync(id);
            if (item == null || item.GridFSFileId == ObjectId.Empty)
            {
                return NotFound();
            }

            var selectedYear = await _yearBranchService.GetCurrentYearAsync();
            if (item.Year != selectedYear)
            {
                return Forbid();
            }

            try
            {
                var imageBytes = await _bucket.DownloadAsBytesAsync(item.GridFSFileId);
                var resizedBytes = ResizeImage(imageBytes, DeliveryMaxDimension);
                return File(resizedBytes, "image/png");
            }
            catch (GridFSFileNotFoundException)
            {
                return NotFound();
            }
        }

        private byte[] ResizeImage(byte[] originalBytes, int maxDimension)
        {
            using (var inputStream = new SKMemoryStream(originalBytes))
            using (var originalBitmap = SKBitmap.Decode(inputStream))
            {
                if (originalBitmap == null)
                {
                    return originalBytes;
                }

                if (originalBitmap.Width <= maxDimension && originalBitmap.Height <= maxDimension)
                {
                    using (var image = SKImage.FromBitmap(originalBitmap))
                    using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                    {
                        return data.ToArray();
                    }
                }

                int newWidth, newHeight;
                if (originalBitmap.Width > originalBitmap.Height)
                {
                    newWidth = maxDimension;
                    newHeight = (int)(originalBitmap.Height * ((float)maxDimension / originalBitmap.Width));
                }
                else
                {
                    newHeight = maxDimension;
                    newWidth = (int)(originalBitmap.Width * ((float)maxDimension / originalBitmap.Height));
                }

                var imageInfo = new SKImageInfo(newWidth, newHeight);
                using (var resizedBitmap = new SKBitmap(imageInfo))
                {
                    originalBitmap.ScalePixels(resizedBitmap, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                    using (var image = SKImage.FromBitmap(resizedBitmap))
                    using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                    {
                        return data.ToArray();
                    }
                }
            }
        }
    }
}




