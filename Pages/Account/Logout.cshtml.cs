using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ForoAttritionErrorWeb.Pages.Account;

public sealed class LogoutModel : PageModel
{
    public IActionResult OnGet()
    {
        return RedirectToPage("/Forum/Index");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Forum/Index");
    }
}
