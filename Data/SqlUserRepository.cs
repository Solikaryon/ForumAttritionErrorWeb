using ForoAttritionErrorWeb.Models;
using Microsoft.Data.SqlClient;

namespace ForoAttritionErrorWeb.Data;

public sealed class SqlUserRepository
{
    private readonly string _connectionString;

    public SqlUserRepository(IConfiguration configuration)
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

    public async Task EnsureUsersTableAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        const string sql = """
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users
    (
        ID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
        UserName NVARCHAR(100) NOT NULL,
        Mail NVARCHAR(256) NOT NULL,
        Password NVARCHAR(450) NOT NULL
    );

    CREATE UNIQUE INDEX UX_Users_Mail ON dbo.Users(Mail);
END
""";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ForumUser?> GetByMailAsync(string mail, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        const string sql = """
SELECT TOP (1)
    ID,
    UserName,
    Mail,
    Password
FROM dbo.Users
WHERE Mail = @Mail;
""";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Mail", mail);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ForumUser
        {
            Id = reader.GetInt32(0),
            UserName = reader.GetString(1),
            Mail = reader.GetString(2),
            PasswordHash = reader.GetString(3)
        };
    }

    public async Task<int> CreateAsync(string userName, string mail, string passwordHash, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        const string sql = """
INSERT INTO dbo.Users (UserName, Mail, Password)
VALUES (@UserName, @Mail, @Password);

SELECT CAST(SCOPE_IDENTITY() AS INT);
""";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@UserName", userName);
        command.Parameters.AddWithValue("@Mail", mail);
        command.Parameters.AddWithValue("@Password", passwordHash);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }
}
