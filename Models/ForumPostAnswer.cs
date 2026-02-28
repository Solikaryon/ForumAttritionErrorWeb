namespace ForoAttritionErrorWeb.Models;

public sealed class ForumPostAnswer
{
    public int Id { get; init; }
    public int PostId { get; init; }
    public string UserMail { get; init; } = string.Empty;
    public string AnswerHtml { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
}
