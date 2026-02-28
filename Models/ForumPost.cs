namespace ForoAttritionErrorWeb.Models;

public sealed class ForumPost
{
    public int Id { get; init; }
    public string UserMail { get; init; } = string.Empty;
    public string MachineType { get; init; } = string.Empty;
    public string ErrorCode { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string BodyHtml { get; init; } = string.Empty;
    public string? Disp { get; init; }
    public string? Mc { get; init; }
    public string? Cause { get; init; }
    public string? Remedy { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}
