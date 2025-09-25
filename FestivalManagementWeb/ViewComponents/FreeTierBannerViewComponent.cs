using System.Threading.Tasks;
using FestivalManagementWeb.Services;
using Microsoft.AspNetCore.Mvc;

namespace FestivalManagementWeb.ViewComponents
{
    public class FreeTierBannerViewComponent : ViewComponent
    {
        private readonly IFreeTierService _service;
        private readonly IRequestQuotaService _quota;

        public FreeTierBannerViewComponent(IFreeTierService service, IRequestQuotaService quota)
        {
            _service = service;
            _quota = quota;
        }

        public IViewComponentResult Invoke()
        {
            var info = _service.GetInfo();
            if (!info.Enabled)
            {
                return Content(string.Empty);
            }
            var snap = _quota.GetSnapshot();
            // augment temp values using ViewData (avoid changing model contract now)
            ViewData["RequestsDailyUsed"] = snap.UsedToday;
            ViewData["RequestsDailyCap"] = (int)System.Math.Max(0, System.Math.Floor(info.RequestsPerDayRemaining));
            return View(info);
        }
    }
}
