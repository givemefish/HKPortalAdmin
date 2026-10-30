using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace HKPortalAdmin.Controllers;

public sealed class CultureController : Controller
{
    [HttpPost]
    public IActionResult Set(string culture, string? returnUrl)
    {
        if (culture is not ("zh-TW" or "zh-CN" or "en-US")) return BadRequest();
        Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Lax });
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Content("~/"));
    }
}
