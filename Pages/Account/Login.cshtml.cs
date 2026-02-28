using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ForoAttritionErrorWeb.Data;
using ForoAttritionErrorWeb.Models;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ForoAttritionErrorWeb.Pages.Account;

public sealed class LoginModel : PageModel
{
    private readonly SqlUserRepository _repository;
    private readonly IPasswordHasher<ForumUser> _passwordHasher;

    public LoginModel(SqlUserRepository repository, IPasswordHasher<ForumUser> passwordHasher)
    {
        _repository = repository;
        _passwordHasher = passwordHasher;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; private set; }

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Mail { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        ForumUser? user;
        var mail = Input.Mail.Trim().ToLowerInvariant();
        try
        {
            user = await _repository.GetByMailAsync(mail, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            ErrorMessage = "Database is not configured. Set the connection string and try again.";
            return Page();
        }
        catch (SqlException ex)
        {
            ErrorMessage = ex.Number switch
            {
                4060 => "Database access denied for this login (cannot open the database). Ask the DBA to map the login to the database and grant permissions.",
                _ => "Database connection failed. Please contact the administrator."
            };
            return Page();
        }

        if (user is null)
        {
            ErrorMessage = "Invalid credentials.";
            return Page();
        }

        var verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, Input.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            ErrorMessage = "Invalid credentials.";
            return Page();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Email, user.Mail)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
        {
            IsPersistent = true,
            RedirectUri = Url.Page("/Forum/Index")
        });

        if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
            return LocalRedirect(ReturnUrl);
        }

        return RedirectToPage("/Forum/Index");
    }
}
