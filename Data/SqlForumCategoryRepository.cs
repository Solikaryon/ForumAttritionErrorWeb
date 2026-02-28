using ForoAttritionErrorWeb.Models;
using Microsoft.Data.SqlClient;

namespace ForoAttritionErrorWeb.Data;

public sealed class SqlForumCategoryRepository
{
    private readonly string _connectionString;

    public SqlForumCategoryRepository(IConfiguration configuration)
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

    public async Task EnsureCategoriesTableAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        const string sql = """
IF OBJECT_ID(N'dbo.Categories', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Categories
    (
        IDC INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Categories PRIMARY KEY,
        Category NVARCHAR(100) NOT NULL
    );

    CREATE UNIQUE INDEX UX_Categories_Category ON dbo.Categories(Category);
END
""";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ForumCategory>> ListAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        const string sql = """
SELECT IDC, Category
FROM dbo.Categories
ORDER BY Category ASC;
""";

        var results = new List<ForumCategory>();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ForumCategory
            {
                Id = reader.GetInt32(0),
                Category = reader.GetString(1)
            });
        }

        return results;
    }

    public async Task<int> CreateAsync(string category, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        const string sql = """
INSERT INTO dbo.Categories (Category)
VALUES (@Category);

SELECT CAST(SCOPE_IDENTITY() AS INT);
""";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Category", category);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }
}
