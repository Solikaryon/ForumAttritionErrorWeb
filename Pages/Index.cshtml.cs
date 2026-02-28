using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ForoAttritionErrorWeb.Pages;

public class IndexModel : PageModel
{
    public IActionResult OnGet()
    {
        if (!User.Identity?.IsAuthenticated ?? true)
        {
            return RedirectToPage("/Account/Login");
        }

        return RedirectToPage("/Forum/Index");
    }
}
