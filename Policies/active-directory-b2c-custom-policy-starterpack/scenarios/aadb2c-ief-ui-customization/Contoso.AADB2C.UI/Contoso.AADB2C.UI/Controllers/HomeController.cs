using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Contoso.AADB2C.UI.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult unified(string campaignId)
        {
            SetPageBackground(campaignId);
            return View();
        }

        public IActionResult selfAsserted(string campaignId)
        {
            SetPageBackground(campaignId);
            return View();
        }

        private void SetPageBackground(string campaignId)
        {
            // If campaign ID is Hawaii, show Hawaii backgound
            if (campaignId != null && campaignId.ToLower() == "hawaii")
            {
                ViewData["background"] = "https://kbdevstorage1.blob.core.windows.net/asset-blobs/19889_en_1";
            }
            // If campaign ID is Tokyo, show Tokyo backgound
            else if (campaignId != null && campaignId.ToLower() == "tokyo")
            {
                ViewData["background"] = "https://kbdevstorage1.blob.core.windows.net/asset-blobs/19666_en_1";
            }
            // Default background
            else
            {
                ViewData["background"] = "https://kbdevstorage1.blob.core.windows.net/asset-blobs/18983_en_1";
            }
        }


        public IActionResult Index()
        {
            return View();
        }

        public IActionResult About()
        {
            ViewData["Message"] = "Your application description page.";

            return View();
        }

        public IActionResult Contact()
        {
            ViewData["Message"] = "Your contact page.";

            return View();
        }

        public IActionResult Error()
        {
            return View();
        }
    }
}
