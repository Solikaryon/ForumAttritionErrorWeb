using ForoAttritionErrorWeb.Models;
using Microsoft.Data.SqlClient;

namespace ForoAttritionErrorWeb.Data;

public sealed class SqlForumPostAnswerRepository
{
    private readonly string _connectionString;

    public SqlForumPostAnswerRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("AttritionForo") ?? string.Empty;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_connectionString);

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Database not configured. Set 'ConnectionStrings:AttritionForo' in appsettings.Development.json or env var ConnectionStrings__AttritionForo.");
        }
    }

    public async Task EnsurePostAnswersTableAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        const string sql = """
IF OBJECT_ID(N'dbo.PostAnswer', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PostAnswer
    (
        IDPA INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PostAnswer PRIMARY KEY,
        [User] NVARCHAR(256) NOT NULL,
        Answer NVARCHAR(MAX) NOT NULL,
        [Date] DATETIME2 NOT NULL CONSTRAINT DF_PostAnswer_Date DEFAULT (SYSUTCDATETIME()),
        IDP INT NOT NULL,
        CONSTRAINT FK_PostAnswer_Posts FOREIGN KEY (IDP) REFERENCES dbo.Posts(IDP)
    );

    CREATE INDEX IX_PostAnswer_IDP ON dbo.PostAnswer(IDP);
    CREATE INDEX IX_PostAnswer_Date ON dbo.PostAnswer([Date]);
END
""";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> CreateAsync(int postId, string userMail, string answerHtml, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        const string sql = """
INSERT INTO dbo.PostAnswer ([User], Answer, [Date], IDP)
VALUES (@User, @Answer, SYSUTCDATETIME(), @IDP);

SELECT CAST(SCOPE_IDENTITY() AS INT);
""";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@User", userMail);
        command.Parameters.AddWithValue("@Answer", answerHtml);
        command.Parameters.AddWithValue("@IDP", postId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task<IReadOnlyList<ForumPostAnswer>> ListByPostIdAsync(int postId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        const string sql = """
SELECT
    IDPA,
    IDP,
    [User],
    Answer,
    [Date]
FROM dbo.PostAnswer
WHERE IDP = @IDP
ORDER BY [Date] ASC;
""";

        var results = new List<ForumPostAnswer>();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@IDP", postId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ForumPostAnswer
            {
                Id = reader.GetInt32(0),
                PostId = reader.GetInt32(1),
                UserMail = reader.GetString(2),
                AnswerHtml = reader.GetString(3),
                CreatedAtUtc = reader.GetDateTime(4)
            });
        }

        return results;
    }
}
