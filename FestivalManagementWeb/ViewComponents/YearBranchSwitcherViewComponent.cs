using System.Threading.Tasks;
using FestivalManagementWeb.Models;
using FestivalManagementWeb.Services;
using Microsoft.AspNetCore.Mvc;

namespace FestivalManagementWeb.ViewComponents
{
    public class YearBranchSwitcherViewComponent : ViewComponent
    {
        private readonly IYearBranchService _yearBranchService;

        public YearBranchSwitcherViewComponent(IYearBranchService yearBranchService)
        {
            _yearBranchService = yearBranchService;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var years = await _yearBranchService.GetAvailableYearsAsync();
            var current = await _yearBranchService.GetCurrentYearAsync();

            var model = new YearBranchSwitcherModel
            {
                AvailableYears = years,
                CurrentYear = current
            };

            return View(model);
        }
    }
}

