using FestivalManagementWeb.Services;
using Microsoft.AspNetCore.Mvc;

namespace FestivalManagementWeb.ViewComponents
{
    public class CosmosStatusViewComponent : ViewComponent
    {
        private readonly IFreeTierService _freeTier;

        public CosmosStatusViewComponent(IFreeTierService freeTier)
        {
            _freeTier = freeTier;
        }

        public IViewComponentResult Invoke()
        {
            var info = _freeTier.GetInfo();
            if (info.Cosmos is not { Enabled: true })
            {
                return Content(string.Empty);
            }

            return View(info.Cosmos);
        }
    }
}
