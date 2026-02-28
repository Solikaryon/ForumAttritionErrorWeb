using System.ComponentModel.DataAnnotations;
using ForoAttritionErrorWeb.Data;
using ForoAttritionErrorWeb.Models;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ForoAttritionErrorWeb.Pages.Account;

public sealed class RegisterModel : PageModel
{
    private readonly SqlUserRepository _repository;
    private readonly IPasswordHasher<ForumUser> _passwordHasher;

    public RegisterModel(
        SqlUserRepository repository,
        IPasswordHasher<ForumUser> passwordHasher)
    {
        _repository = repository;
        _passwordHasher = passwordHasher;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    public sealed class InputModel
    {
        [Required]
        [StringLength(100, MinimumLength = 2)]
        [Display(Name = "Username")]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [Display(Name = "Email (@jabil.com)")]
        public string Mail { get; set; } = string.Empty;

        [Required]
        [MinLength(8)]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
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

        var mail = Input.Mail.Trim().ToLowerInvariant();
        if (!mail.EndsWith("@jabil.com", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("Input.Mail", "Only @jabil.com emails are allowed.");
            return Page();
        }

        ForumUser? existing;
        try
        {
            existing = await _repository.GetByMailAsync(mail, cancellationToken);
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

        if (existing is not null)
        {
            ErrorMessage = "That email is already registered.";
            return Page();
        }

        var newUser = new ForumUser
        {
            Id = 0,
            UserName = Input.UserName.Trim(),
            Mail = mail,
            PasswordHash = string.Empty
        };

        var passwordHash = _passwordHasher.HashPassword(newUser, Input.Password);

        try
        {
            await _repository.CreateAsync(newUser.UserName, newUser.Mail, passwordHash, cancellationToken);
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
                2627 => "That email is already registered.",
                2601 => "That email is already registered.",
                _ => "Database connection failed. Please contact the administrator."
            };
            return Page();
        }

        return RedirectToPage("/Account/Login");
    }
}
