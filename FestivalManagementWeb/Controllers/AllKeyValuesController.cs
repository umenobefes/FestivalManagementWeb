using FestivalManagementWeb.Models;
using FestivalManagementWeb.Repositories;
using FestivalManagementWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace FestivalManagementWeb.Controllers
{
    [Authorize]
    public class AllKeyValuesController : Controller
    {
        private readonly ITextKeyValueRepository _textRepository;
        private readonly IImageKeyValueRepository _imageRepository;
        private readonly IYearBranchService _yearBranchService;

        public AllKeyValuesController(
            ITextKeyValueRepository textRepository,
            IImageKeyValueRepository imageRepository,
            IYearBranchService yearBranchService)
        {
            _textRepository = textRepository;
            _imageRepository = imageRepository;
            _yearBranchService = yearBranchService;
        }

        public async Task<IActionResult> Index()
        {
            var selectedYear = await _yearBranchService.GetCurrentYearAsync();
            var textItems = await _textRepository.GetAllAsync(selectedYear);
            var imageItems = await _imageRepository.GetAllAsync(selectedYear);

            var allItems = textItems.Cast<BaseModel>()
                .Concat(imageItems.Cast<BaseModel>())
                .OrderBy(x => x.Id);

            var viewModel = new AllKeyValueViewModel
            {
                AllItems = allItems,
                SelectedYear = selectedYear,
                TreeNodes = BuildTree(allItems)
            };

            ViewData["SelectedYear"] = selectedYear;
            return View(viewModel);
        }

        private List<KeyValueTreeNode> BuildTree(IEnumerable<BaseModel> items)
        {
            var categories = new Dictionary<string, KeyValueTreeNode>();

            foreach (var item in items)
            {
                string? key = item is TextKeyValue textItem ? textItem.Key :
                             item is ImageKeyValue imageItem ? imageItem.Key : null;

                if (string.IsNullOrEmpty(key)) continue;

                var parsed = ParseKey(key);
                if (!parsed.HasValue) continue;

                var (category, number, subcategory) = parsed.Value;

                // カテゴリノードを取得または作成
                if (!categories.TryGetValue(category, out var categoryNode))
                {
                    categoryNode = new KeyValueTreeNode
                    {
                        Name = category,
                        NodeType = "category"
                    };
                    categories[category] = categoryNode;
                }

                // 番号ノードを取得または作成
                var numberNode = categoryNode.Children.FirstOrDefault(n => n.Name == number);
                if (numberNode == null)
                {
                    numberNode = new KeyValueTreeNode
                    {
                        Name = number,
                        NodeType = "number"
                    };
                    categoryNode.Children.Add(numberNode);
                }

                // サブカテゴリノード(リーフ)を作成
                var subcategoryNode = new KeyValueTreeNode
                {
                    Name = subcategory,
                    Item = item,
                    NodeType = "subcategory"
                };
                numberNode.Children.Add(subcategoryNode);
            }

            // カテゴリをソートし、各カテゴリの番号もソート
            var sortedCategories = categories.Values.OrderBy(c => c.Name).ToList();
            foreach (var category in sortedCategories)
            {
                category.Children = category.Children
                    .OrderBy(n => int.TryParse(n.Name, out var num) ? num : int.MaxValue)
                    .ToList();
            }

            return sortedCategories;
        }

        private (string category, string number, string subcategory)? ParseKey(string key)
        {
            var parts = key.Split('_');
            if (parts.Length < 3) return null; // 少なくとも3つの部分が必要

            var number = parts[^1]; // 最後の部分
            var subcategory = parts[^2]; // 最後から2番目の部分
            var category = string.Join("_", parts.Take(parts.Length - 2)); // 最初から最後から2番目の手前まで

            return (category, number, subcategory);
        }
    }
}
