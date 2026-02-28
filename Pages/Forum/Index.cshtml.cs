using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ForoAttritionErrorWeb.Data;
using ForoAttritionErrorWeb.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace ForoAttritionErrorWeb.Pages.Forum;

[AllowAnonymous]
public sealed class IndexModel : PageModel
{
    private readonly SqlForumPostRepository _posts;

    private const int PageSize = 10;

    public IndexModel(SqlForumPostRepository posts)
    {
        _posts = posts;
    }

    public IReadOnlyList<string> MachineTypeList { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<ForumPostListItem> PostList { get; private set; } = Array.Empty<ForumPostListItem>();

    [BindProperty(SupportsGet = true)]
    public string? MachineType { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? MachineTypeOther { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ErrorCode { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? UserMail { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TitleKeywords { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty]
    public NewPostInput NewPost { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    public int TotalCount { get; private set; }
    public int TotalPages { get; private set; } = 1;
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;

    public sealed class NewPostInput
    {
        [Required]
        [StringLength(100, MinimumLength = 1)]
        [Display(Name = "Machine Type")]
        public string MachineTypeSelection { get; set; } = string.Empty;

        [StringLength(100, MinimumLength = 1)]
        [Display(Name = "Machine Type (Other)")]
        public string? MachineTypeOther { get; set; }

        [Required]
        [StringLength(100, MinimumLength = 1)]
        [Display(Name = "Error Code")]
        public string ErrorCode { get; set; } = string.Empty;

        [Required]
        [StringLength(200, MinimumLength = 3)]
        [Display(Name = "Title")]
        public string Title { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Body")]
        public string BodyHtml { get; set; } = string.Empty;
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnGetErrorCodesAsync(string? machineType, string? q, CancellationToken cancellationToken)
    {
        try
        {
            var list = await _posts.ListErrorCodesAsync(machineType, keywords: q, take: 200, cancellationToken);
            return new JsonResult(list);
        }
        catch (InvalidOperationException)
        {
            return new JsonResult(Array.Empty<string>()) { StatusCode = 500 };
        }
        catch (SqlException)
        {
            return new JsonResult(Array.Empty<string>()) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> OnPostCreatePostAsync(CancellationToken cancellationToken)
    {
        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            return RedirectToPage("/Account/Login", new { returnUrl = Url.Page("/Forum/Index") });
        }

        // Validate NewPost only.
        ModelState.Clear();
        if (!TryValidateModel(NewPost, nameof(NewPost)))
        {
            // Help LoadAsync filter ErrorCode list when re-rendering.
            MachineType = string.IsNullOrWhiteSpace(NewPost.MachineTypeSelection) ? MachineType : NewPost.MachineTypeSelection;
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

        var machineType = NewPost.MachineTypeSelection.Trim();
        if (string.Equals(machineType, "__OTHER__", StringComparison.Ordinal))
        {
            machineType = (NewPost.MachineTypeOther ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(machineType))
            {
                ModelState.AddModelError("NewPost.MachineTypeOther", "Machine Type (Other) is required.");
            }
        }

        var errorCode = NewPost.ErrorCode.Trim();

        if (machineType.Length > 100)
        {
            ModelState.AddModelError("NewPost.MachineTypeSelection", "Machine Type must be 100 characters or less.");
        }

        if (errorCode.Length > 100)
        {
            ModelState.AddModelError("NewPost.ErrorCode", "Error Code must be 100 characters or less.");
        }

        if (!ModelState.IsValid)
        {
            MachineType = string.IsNullOrWhiteSpace(machineType) ? MachineType : machineType;
            await LoadAsync(cancellationToken);
            return Page();
        }
        var title = NewPost.Title.Trim();
        var bodyHtml = RichTextSanitizer.Sanitize(NewPost.BodyHtml);

        if (string.IsNullOrWhiteSpace(bodyHtml))
        {
            ModelState.AddModelError("NewPost.BodyHtml", "Body is required.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        int idp;
        try
        {
            idp = await _posts.CreateAsync(userMail, machineType, errorCode, title, bodyHtml, cancellationToken);
        }
        catch (SqlException ex)
        {
            ErrorMessage = ex.Number switch
            {
                4060 => "Database access denied for this login (cannot open the database). Ask the DBA to map the login to the database and grant permissions.",
                _ => "Could not create the post."
            };
            await LoadAsync(cancellationToken);
            return Page();
        }
        catch
        {
            ErrorMessage = "Could not create the post.";
            await LoadAsync(cancellationToken);
            return Page();
        }

        return RedirectToPage("/Forum/Post", new { idp });
    }

    public string Excerpt(string html)
    {
        var plain = RichTextSanitizer.ToPlainText(html);
        if (plain.Length <= 220)
        {
            return plain;
        }

        return plain[..220] + "...";
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            MachineTypeList = await _posts.ListMachineTypesAsync(cancellationToken);

            var effectiveMachineType = MachineType;
            if (string.Equals((MachineType ?? string.Empty).Trim(), "__OTHER__", StringComparison.Ordinal))
            {
                effectiveMachineType = (MachineTypeOther ?? string.Empty).Trim();
            }

            if (PageNumber < 1)
            {
                PageNumber = 1;
            }

            TotalCount = await _posts.CountSearchAsync(effectiveMachineType, ErrorCode, UserMail, TitleKeywords, cancellationToken);
            TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

            if (PageNumber > TotalPages)
            {
                PageNumber = TotalPages;
            }

            var skip = (PageNumber - 1) * PageSize;
            PostList = await _posts.SearchAsync(effectiveMachineType, ErrorCode, UserMail, TitleKeywords, skip, PageSize, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            ErrorMessage ??= "Database is not configured. Set the connection string and try again.";
            MachineTypeList = Array.Empty<string>();
            PostList = Array.Empty<ForumPostListItem>();
            TotalCount = 0;
            TotalPages = 1;
        }
        catch (SqlException ex)
        {
            ErrorMessage ??= ex.Number switch
            {
                4060 => "Database access denied for this login (cannot open the database). Ask the DBA to map the login to the database and grant permissions.",
                _ => "Database connection failed. Please contact the administrator."
            };
            MachineTypeList = Array.Empty<string>();
            PostList = Array.Empty<ForumPostListItem>();
            TotalCount = 0;
            TotalPages = 1;
        }
    }
}
