namespace ForoAttritionErrorWeb.Models;

public sealed class ForumUser
{
    public int Id { get; init; }
    public required string UserName { get; init; }
    public required string Mail { get; init; }
    public required string PasswordHash { get; init; }
}
