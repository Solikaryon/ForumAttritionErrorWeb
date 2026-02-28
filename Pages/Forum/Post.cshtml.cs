using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ForoAttritionErrorWeb.Data;
using ForoAttritionErrorWeb.Infrastructure.Security;
using ForoAttritionErrorWeb.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace ForoAttritionErrorWeb.Pages.Forum;

[AllowAnonymous]
public sealed class PostModel : PageModel
{
    private readonly SqlForumPostRepository _posts;
    private readonly SqlForumPostAnswerRepository _answers;

    public PostModel(SqlForumPostRepository posts, SqlForumPostAnswerRepository answers)
    {
        _posts = posts;
        _answers = answers;
    }

    [BindProperty(SupportsGet = true)]
    public int Idp { get; set; }

    public ForumPostDetails? Post { get; private set; }
    public IReadOnlyList<ForumPostAnswer> AnswerList { get; private set; } = Array.Empty<ForumPostAnswer>();

    [BindProperty]
    public NewAnswerInput NewAnswer { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    public sealed class NewAnswerInput
    {
        [Required]
        [Display(Name = "Answer")]
        public string AnswerHtml { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        if (Post is null)
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAnswerAsync(CancellationToken cancellationToken)
    {
        if (Idp <= 0)
        {
            if (int.TryParse(Request.Query["idp"].ToString(), out var idpFromQuery))
            {
                Idp = idpFromQuery;
            }
        }

        if (Idp <= 0)
        {
            ErrorMessage = "Missing post id. Please open the post again and retry.";
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            return RedirectToPage("/Account/Login", new { returnUrl = Url.Page("/Forum/Post", new { idp = Idp }) });
        }

        if (!TryValidateModel(NewAnswer, nameof(NewAnswer)))
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        var userMail = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userMail))
        {
            return RedirectToPage("/Account/Login");
        }

        if (!userMail.EndsWith("@jabil.com", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Only @jabil.com emails are allowed.";
            await LoadAsync(cancellationToken);
            return Page();
        }

        var html = RichTextSanitizer.Sanitize(NewAnswer.AnswerHtml);
        if (string.IsNullOrWhiteSpace(html))
        {
            ModelState.AddModelError("NewAnswer.AnswerHtml", "Answer is required.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            await _answers.CreateAsync(Idp, userMail, html, cancellationToken);
        }
        catch (SqlException ex)
        {
            ErrorMessage = ex.Number switch
            {
                547 => "That post does not exist.",
                4060 => "Database access denied for this login (cannot open the database). Ask the DBA to map the login to the database and grant permissions.",
                _ => "Could not save the answer."
            };

            await LoadAsync(cancellationToken);
            return Page();
        }
        catch
        {
            ErrorMessage = "Could not save the answer.";
            await LoadAsync(cancellationToken);
            return Page();
        }

        return RedirectToPage("/Forum/Post", new { idp = Idp });
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Post = await _posts.GetDetailsByIdAsync(Idp, cancellationToken);
            if (Post is not null)
            {
                AnswerList = await _answers.ListByPostIdAsync(Idp, cancellationToken);
            }
        }
        catch (InvalidOperationException)
        {
            ErrorMessage ??= "Database is not configured. Set the connection string and try again.";
            Post = null;
            AnswerList = Array.Empty<ForumPostAnswer>();
        }
        catch (SqlException ex)
        {
            ErrorMessage ??= ex.Number switch
            {
                4060 => "Database access denied for this login (cannot open the database). Ask the DBA to map the login to the database and grant permissions.",
                _ => "Database connection failed. Please contact the administrator."
            };
            Post = null;
            AnswerList = Array.Empty<ForumPostAnswer>();
        }
    }
}
