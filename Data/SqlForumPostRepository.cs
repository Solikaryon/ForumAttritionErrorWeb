using ForoAttritionErrorWeb.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace ForoAttritionErrorWeb.Data;

public sealed class SqlForumPostRepository
{
    private readonly string _connectionString;
    private readonly string? _downtimeCodeIdColumnOverride;
    private string? _downtimeCodeIdColumnName;
    private readonly SemaphoreSlim _downtimeCodeIdColumnLock = new(1, 1);

    public SqlForumPostRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("AttritionForo") ?? string.Empty;
        _downtimeCodeIdColumnOverride = configuration["Fujidb:DowntimeCodeIdColumn"];
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

    public async Task EnsurePostsTableAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Create table if it doesn't exist (new schema).
        const string createSql = """
IF OBJECT_ID(N'dbo.Posts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Posts
    (
        IDP INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Posts PRIMARY KEY,
        [User] NVARCHAR(256) NOT NULL,
        CodeID NVARCHAR(50) NULL,
        MachineType NVARCHAR(100) NOT NULL,
        ErrorCode NVARCHAR(100) NOT NULL,
        Tittle NVARCHAR(200) NOT NULL,
        Body NVARCHAR(MAX) NOT NULL,
        Disp NVARCHAR(200) NULL,
        Mc NVARCHAR(200) NULL,
        Cause NVARCHAR(MAX) NULL,
        Remedy NVARCHAR(MAX) NULL,
        [Date] DATETIME2 NOT NULL CONSTRAINT DF_Posts_Date DEFAULT (SYSUTCDATETIME())
    );
END
""";

        await using (var command = new SqlCommand(createSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Drop legacy FK/index/column if present.
        const string dropFkSql = """
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Posts_Categories' AND parent_object_id = OBJECT_ID(N'dbo.Posts'))
BEGIN
    ALTER TABLE dbo.Posts DROP CONSTRAINT FK_Posts_Categories;
END
""";
        await using (var command = new SqlCommand(dropFkSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string dropIxCategorySql = """
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Posts_Category' AND object_id = OBJECT_ID(N'dbo.Posts'))
BEGIN
    DROP INDEX IX_Posts_Category ON dbo.Posts;
END
""";
        await using (var command = new SqlCommand(dropIxCategorySql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string dropCategoryColumnSql = """
IF COL_LENGTH('dbo.Posts', 'Category') IS NOT NULL
BEGIN
    ALTER TABLE dbo.Posts DROP COLUMN Category;
END
""";
        await using (var command = new SqlCommand(dropCategoryColumnSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Add new columns if missing.
        const string addCodeIdSql = """
IF COL_LENGTH('dbo.Posts', 'CodeID') IS NULL
BEGIN
    ALTER TABLE dbo.Posts ADD CodeID NVARCHAR(50) NULL;
END
""";
        await using (var command = new SqlCommand(addCodeIdSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // If CodeID exists but isn't NVARCHAR, convert it (drop/recreate index as needed).
        const string ensureCodeIdNvarcharSql = """
IF EXISTS (
    SELECT 1
    FROM sys.columns c
    INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.Posts')
      AND c.name = N'CodeID'
      AND t.name <> N'nvarchar'
)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Posts_System_CodeID' AND object_id = OBJECT_ID(N'dbo.Posts'))
    BEGIN
        DROP INDEX UX_Posts_System_CodeID ON dbo.Posts;
    END

    ALTER TABLE dbo.Posts ALTER COLUMN CodeID NVARCHAR(50) NULL;
END
""";
        await using (var command = new SqlCommand(ensureCodeIdNvarcharSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string addMachineTypeSql = """
IF COL_LENGTH('dbo.Posts', 'MachineType') IS NULL
BEGIN
    ALTER TABLE dbo.Posts ADD MachineType NVARCHAR(100) NULL;
END
""";
        await using (var command = new SqlCommand(addMachineTypeSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string addErrorCodeSql = """
IF COL_LENGTH('dbo.Posts', 'ErrorCode') IS NULL
BEGIN
    ALTER TABLE dbo.Posts ADD ErrorCode NVARCHAR(100) NULL;
END
""";
        await using (var command = new SqlCommand(addErrorCodeSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string addDispSql = """
IF COL_LENGTH('dbo.Posts', 'Disp') IS NULL
BEGIN
    ALTER TABLE dbo.Posts ADD Disp NVARCHAR(200) NULL;
END
""";
        await using (var command = new SqlCommand(addDispSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string addMcSql = """
IF COL_LENGTH('dbo.Posts', 'Mc') IS NULL
BEGIN
    ALTER TABLE dbo.Posts ADD Mc NVARCHAR(200) NULL;
END
""";
        await using (var command = new SqlCommand(addMcSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string addCauseSql = """
IF COL_LENGTH('dbo.Posts', 'Cause') IS NULL
BEGIN
    ALTER TABLE dbo.Posts ADD Cause NVARCHAR(MAX) NULL;
END
""";
        await using (var command = new SqlCommand(addCauseSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string addRemedySql = """
IF COL_LENGTH('dbo.Posts', 'Remedy') IS NULL
BEGIN
    ALTER TABLE dbo.Posts ADD Remedy NVARCHAR(MAX) NULL;
END
""";
        await using (var command = new SqlCommand(addRemedySql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Backfill + enforce NOT NULL on the new key columns.
        const string backfillMachineTypeSql = """
UPDATE dbo.Posts SET MachineType = N'Unknown' WHERE MachineType IS NULL;
""";
        await using (var command = new SqlCommand(backfillMachineTypeSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string backfillErrorCodeSql = """
UPDATE dbo.Posts SET ErrorCode = N'Unknown' WHERE ErrorCode IS NULL;
""";
        await using (var command = new SqlCommand(backfillErrorCodeSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string alterMachineTypeNotNullSql = """
ALTER TABLE dbo.Posts ALTER COLUMN MachineType NVARCHAR(100) NOT NULL;
""";
        await using (var command = new SqlCommand(alterMachineTypeNotNullSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string alterErrorCodeNotNullSql = """
ALTER TABLE dbo.Posts ALTER COLUMN ErrorCode NVARCHAR(100) NOT NULL;
""";
        await using (var command = new SqlCommand(alterErrorCodeNotNullSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Ensure indexes.
        const string uxSystemCodeIdSql = """
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Posts_System_CodeID' AND object_id = OBJECT_ID(N'dbo.Posts'))
BEGIN
    CREATE UNIQUE INDEX UX_Posts_System_CodeID ON dbo.Posts(CodeID)
    WHERE CodeID IS NOT NULL AND [User] = N'System';
END
""";
        await using (var command = new SqlCommand(uxSystemCodeIdSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string ixMachineTypeSql = """
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Posts_MachineType' AND object_id = OBJECT_ID(N'dbo.Posts'))
BEGIN
    CREATE INDEX IX_Posts_MachineType ON dbo.Posts(MachineType);
END
""";
        await using (var command = new SqlCommand(ixMachineTypeSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string ixErrorCodeSql = """
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Posts_ErrorCode' AND object_id = OBJECT_ID(N'dbo.Posts'))
BEGIN
    CREATE INDEX IX_Posts_ErrorCode ON dbo.Posts(ErrorCode);
END
""";
        await using (var command = new SqlCommand(ixErrorCodeSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string ixUserSql = """
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Posts_User' AND object_id = OBJECT_ID(N'dbo.Posts'))
BEGIN
    CREATE INDEX IX_Posts_User ON dbo.Posts([User]);
END
""";
        await using (var command = new SqlCommand(ixUserSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string ixDateSql = """
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Posts_Date' AND object_id = OBJECT_ID(N'dbo.Posts'))
BEGIN
    CREATE INDEX IX_Posts_Date ON dbo.Posts([Date]);
END
""";
        await using (var command = new SqlCommand(ixDateSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<int> CreateAsync(
        string userMail,
        string machineType,
        string errorCode,
        string title,
        string bodyHtml,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var resolved = await TryResolveErrorAsync(machineType, errorCode, cancellationToken);
        var combinedBody = AppendAutoInfo(bodyHtml, resolved);

        const string sql = """
INSERT INTO dbo.Posts ([User], CodeID, MachineType, ErrorCode, Tittle, Body, Disp, Mc, Cause, Remedy, [Date])
VALUES (@User, @CodeID, @MachineType, @ErrorCode, @Tittle, @Body, @Disp, @Mc, @Cause, @Remedy, SYSUTCDATETIME());

SELECT CAST(SCOPE_IDENTITY() AS INT);
""";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@User", userMail);
        command.Parameters.AddWithValue("@CodeID", (object?)resolved?.CodeId ?? DBNull.Value);
        command.Parameters.AddWithValue("@MachineType", machineType);
        command.Parameters.AddWithValue("@ErrorCode", errorCode);
        command.Parameters.AddWithValue("@Tittle", title);
        command.Parameters.AddWithValue("@Body", combinedBody);
        command.Parameters.AddWithValue("@Disp", (object?)resolved?.Disp ?? DBNull.Value);
        command.Parameters.AddWithValue("@Mc", (object?)resolved?.Mc ?? DBNull.Value);
        command.Parameters.AddWithValue("@Cause", (object?)resolved?.Cause ?? DBNull.Value);
        command.Parameters.AddWithValue("@Remedy", (object?)resolved?.Remedy ?? DBNull.Value);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task<ForumPost?> GetByIdAsync(int idp, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        const string sql = """
SELECT TOP (1)
    IDP,
    [User],
    MachineType,
    ErrorCode,
    Tittle,
    Body,
    Disp,
    Mc,
    Cause,
    Remedy,
    [Date]
FROM dbo.Posts
WHERE IDP = @IDP;
""";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@IDP", idp);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ForumPost
        {
            Id = reader.GetInt32(0),
            UserMail = reader.GetString(1),
            MachineType = reader.GetString(2),
            ErrorCode = reader.GetString(3),
            Title = reader.GetString(4),
            BodyHtml = reader.GetString(5),
            Disp = reader.IsDBNull(6) ? null : reader.GetString(6),
            Mc = reader.IsDBNull(7) ? null : reader.GetString(7),
            Cause = reader.IsDBNull(8) ? null : reader.GetString(8),
            Remedy = reader.IsDBNull(9) ? null : reader.GetString(9),
            CreatedAtUtc = reader.GetDateTime(10)
        };
    }

    public async Task<ForumPostDetails?> GetDetailsByIdAsync(int idp, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        const string sql = """
SELECT TOP (1)
    p.IDP,
    p.[User],
    p.MachineType,
    p.ErrorCode,
    p.Tittle,
    p.Body,
    p.Disp,
    p.Mc,
    p.Cause,
    p.Remedy,
    p.[Date]
FROM dbo.Posts p
WHERE p.IDP = @IDP;
""";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@IDP", idp);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ForumPostDetails
        {
            Id = reader.GetInt32(0),
            UserMail = reader.GetString(1),
            MachineType = reader.GetString(2),
            ErrorCode = reader.GetString(3),
            Title = reader.GetString(4),
            BodyHtml = reader.GetString(5),
            Disp = reader.IsDBNull(6) ? null : reader.GetString(6),
            Mc = reader.IsDBNull(7) ? null : reader.GetString(7),
            Cause = reader.IsDBNull(8) ? null : reader.GetString(8),
            Remedy = reader.IsDBNull(9) ? null : reader.GetString(9),
            CreatedAtUtc = reader.GetDateTime(10)
        };
    }

    private static (string whereSql, SqlParameter[] parameters) BuildPostSearchWhere(
        string? machineType,
        string? errorCode,
        string? userMail,
        string? titleKeywords)
    {
        // Simple keyword search (LIKE) over Title. Split words and AND them.
        var keywords = (titleKeywords ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(8)
            .ToArray();

        var where = new List<string>();
        var parameters = new List<SqlParameter>();

        if (!string.IsNullOrWhiteSpace(machineType))
        {
            where.Add("p.MachineType = @MachineType");
            parameters.Add(new SqlParameter("@MachineType", machineType.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            where.Add("p.ErrorCode LIKE @ErrorCode");
            parameters.Add(new SqlParameter("@ErrorCode", $"%{errorCode.Trim()}%"));
        }

        if (!string.IsNullOrWhiteSpace(userMail))
        {
            where.Add("p.[User] LIKE @User");
            parameters.Add(new SqlParameter("@User", $"%{userMail.Trim()}%"));
        }

        for (var i = 0; i < keywords.Length; i++)
        {
            var name = $"@K{i}";
            where.Add($"p.Tittle LIKE {name}");
            parameters.Add(new SqlParameter(name, $"%{keywords[i]}%"));
        }

        var whereSql = where.Count == 0 ? string.Empty : ("WHERE " + string.Join(" AND ", where));
        return (whereSql, parameters.ToArray());
    }

    public async Task<int> CountSearchAsync(
        string? machineType,
        string? errorCode,
        string? userMail,
        string? titleKeywords,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var (whereSql, parameters) = BuildPostSearchWhere(machineType, errorCode, userMail, titleKeywords);

        var sql = $"""
SELECT COUNT_BIG(1)
FROM dbo.Posts p
{whereSql};
""";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        foreach (var p in parameters)
        {
            command.Parameters.Add(p);
        }

        var total = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
        if (total <= 0)
        {
            return 0;
        }

        return total > int.MaxValue ? int.MaxValue : (int)total;
    }

    public async Task<IReadOnlyList<ForumPostListItem>> SearchAsync(
        string? machineType,
        string? errorCode,
        string? userMail,
        string? titleKeywords,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (skip < 0)
        {
            skip = 0;
        }

        if (take <= 0)
        {
            take = 10;
        }

        var (whereSql, parameters) = BuildPostSearchWhere(machineType, errorCode, userMail, titleKeywords);

        var sql = $"""
SELECT
    p.IDP,
    p.[User],
    p.MachineType,
    p.ErrorCode,
    p.Tittle,
    p.Body,
    p.Disp,
    p.Mc,
    p.Cause,
    p.Remedy,
    p.[Date]
FROM dbo.Posts p
{whereSql}
ORDER BY p.[Date] DESC
OFFSET @Skip ROWS
FETCH NEXT @Take ROWS ONLY;
""";

        var results = new List<ForumPostListItem>();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Skip", skip);
        command.Parameters.AddWithValue("@Take", take);
        foreach (var p in parameters)
        {
            command.Parameters.Add(p);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ForumPostListItem
            {
                Id = reader.GetInt32(0),
                UserMail = reader.GetString(1),
                MachineType = reader.GetString(2),
                ErrorCode = reader.GetString(3),
                Title = reader.GetString(4),
                BodyHtml = reader.GetString(5),
                Disp = reader.IsDBNull(6) ? null : reader.GetString(6),
                Mc = reader.IsDBNull(7) ? null : reader.GetString(7),
                Cause = reader.IsDBNull(8) ? null : reader.GetString(8),
                Remedy = reader.IsDBNull(9) ? null : reader.GetString(9),
                CreatedAtUtc = reader.GetDateTime(10)
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<string>> ListMachineTypesAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        const string sql = """
SELECT DISTINCT
    CAST(MACHINETYPE AS NVARCHAR(100)) AS MachineType
FROM [fujidb].[dbo].[DOWNTIME_D]
WHERE MACHINETYPE IS NOT NULL AND LTRIM(RTRIM(CAST(MACHINETYPE AS NVARCHAR(100)))) <> N''
ORDER BY CAST(MACHINETYPE AS NVARCHAR(100)) ASC;
""";

        var results = new List<string>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    public async Task<IReadOnlyList<string>> ListErrorCodesAsync(
        string? machineType = null,
        string? keywords = null,
        int take = 200,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (take <= 0)
        {
            take = 200;
        }

        if (take > 2000)
        {
            take = 2000;
        }

        var sql = $"""
SELECT DISTINCT TOP ({take})
    CAST(ERRORCODE AS NVARCHAR(100)) AS ErrorCode
FROM [fujidb].[dbo].[DOWNTIME_D]
WHERE ERRORCODE IS NOT NULL
  AND LTRIM(RTRIM(CAST(ERRORCODE AS NVARCHAR(100)))) <> N''
""";

        var parameters = new List<SqlParameter>();
        if (!string.IsNullOrWhiteSpace(machineType))
        {
            sql += "\n  AND CAST(MACHINETYPE AS NVARCHAR(100)) = @MachineType";
            parameters.Add(new SqlParameter("@MachineType", machineType.Trim()));
        }

        var words = (keywords ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(4)
            .ToArray();

        for (var i = 0; i < words.Length; i++)
        {
            var p = $"@K{i}";
            sql += $"\n  AND CAST(ERRORCODE AS NVARCHAR(100)) LIKE {p}";
            parameters.Add(new SqlParameter(p, $"%{words[i]}%"));
        }

        sql += "\nORDER BY CAST(ERRORCODE AS NVARCHAR(100)) ASC;";

        var results = new List<string>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        foreach (var p in parameters)
        {
            command.Parameters.Add(p);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    public async Task<int> EnsureSystemPostsFromFujidbAsync(CancellationToken cancellationToken = default)
        => await EnsureSystemPostsFromFujidbAsync(logger: null, cancellationToken);

    public async Task<int> EnsureSystemPostsFromFujidbAsync(ILogger? logger, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        // In this fujidb schema, DOWNTIME_D does not expose CodeID. The relationship to Codigosde_error is by value
        // (Codigosde_error.CodeID) matching ERRORCODE and/or SUBERRORCODE.
        var overrideName = (_downtimeCodeIdColumnOverride ?? string.Empty).Trim();
        var joinMode = string.IsNullOrWhiteSpace(overrideName)
            ? "ERRORCODE+SUBERRORCODE"
            : overrideName.ToUpperInvariant();

        logger?.LogInformation("System-post sync join mode: {JoinMode}.", joinMode);

        var useErrorCode = string.IsNullOrWhiteSpace(overrideName)
            || overrideName.Equals("ERRORCODE", StringComparison.OrdinalIgnoreCase);
        var useSubErrorCode = string.IsNullOrWhiteSpace(overrideName)
            || overrideName.Equals("SUBERRORCODE", StringComparison.OrdinalIgnoreCase);

        if (!useErrorCode && !useSubErrorCode)
        {
            logger?.LogWarning("System-post sync skipped: Fujidb:DowntimeCodeIdColumn must be ERRORCODE or SUBERRORCODE. Value={Value}", overrideName);
            return 0;
        }

        // Set-based insert:
        // - Normalize CodeID/ERRORCODE/SUBERRORCODE as NVARCHAR strings.
        // - Allow a code to match either ERRORCODE or SUBERRORCODE.
        // - 1 post per CodeID, picking the most frequent (MachineType, ErrorCode).
        // - Basic HTML encoding done in SQL to avoid injecting HTML.
        var sql = $"""
DECLARE @useErrorCode bit = {(useErrorCode ? 1 : 0)};
DECLARE @useSubErrorCode bit = {(useSubErrorCode ? 1 : 0)};

WITH
ceRaw AS
(
    SELECT
        LTRIM(RTRIM(CONVERT(NVARCHAR(200), ce.CodeID))) AS CodeID,
        CAST(ce.Disp AS NVARCHAR(200)) AS Disp,
        CAST(ce.Mc AS NVARCHAR(200)) AS Mc,
        CAST(ce.Cause AS NVARCHAR(MAX)) AS Cause,
        CAST(ce.Remedy AS NVARCHAR(MAX)) AS Remedy
    FROM [fujidb].[dbo].[Codigosde_error] ce WITH (NOLOCK)
    WHERE ce.CodeID IS NOT NULL
      AND LTRIM(RTRIM(CONVERT(NVARCHAR(200), ce.CodeID))) <> N''
),
ceNorm AS
(
    SELECT CodeID, Disp, Mc, Cause, Remedy
    FROM
    (
        SELECT
            r.CodeID,
            r.Disp,
            r.Mc,
            r.Cause,
            r.Remedy,
            ROW_NUMBER() OVER (
                PARTITION BY r.CodeID
                ORDER BY
                    CASE WHEN NULLIF(LTRIM(RTRIM(CONVERT(NVARCHAR(MAX), r.Cause))), N'') IS NULL THEN 1 ELSE 0 END,
                    LEN(LTRIM(RTRIM(CONVERT(NVARCHAR(MAX), r.Cause)))) DESC,
                    LEN(LTRIM(RTRIM(CONVERT(NVARCHAR(MAX), r.Remedy)))) DESC
            ) AS rn
        FROM ceRaw r
    ) x
    WHERE x.rn = 1
),
dtRaw AS
(
    SELECT
        CAST(d.MACHINETYPE AS NVARCHAR(100)) AS MachineType,
        CAST(d.ERRORCODE AS NVARCHAR(100)) AS ErrorCode,
        CAST(d.SUBERRORCODE AS NVARCHAR(100)) AS SubErrorCode
    FROM [fujidb].[dbo].[DOWNTIME_D] d WITH (NOLOCK)
    WHERE d.MACHINETYPE IS NOT NULL
      AND LTRIM(RTRIM(CAST(d.MACHINETYPE AS NVARCHAR(100)))) <> N''
      AND (
            (@useErrorCode = 1 AND d.ERRORCODE IS NOT NULL AND LTRIM(RTRIM(CAST(d.ERRORCODE AS NVARCHAR(100)))) <> N'')
         OR (@useSubErrorCode = 1 AND d.SUBERRORCODE IS NOT NULL AND LTRIM(RTRIM(CAST(d.SUBERRORCODE AS NVARCHAR(100)))) <> N'')
      )
),
dtMatches AS
(
    SELECT
        LTRIM(RTRIM(CONVERT(NVARCHAR(200), r.ErrorCode))) AS CodeID,
        r.MachineType,
        r.ErrorCode
    FROM dtRaw r
    WHERE @useErrorCode = 1
      AND r.ErrorCode IS NOT NULL

    UNION ALL

    SELECT
        LTRIM(RTRIM(CONVERT(NVARCHAR(200), r.SubErrorCode))) AS CodeID,
        r.MachineType,
        r.ErrorCode
    FROM dtRaw r
    WHERE @useSubErrorCode = 1
      AND r.SubErrorCode IS NOT NULL
),
dtAgg AS
(
    SELECT
        m.CodeID,
        m.MachineType,
        m.ErrorCode,
        COUNT_BIG(1) AS Cnt
    FROM dtMatches m
    WHERE m.CodeID IS NOT NULL
      AND m.CodeID <> N''
    GROUP BY m.CodeID, m.MachineType, m.ErrorCode
),
dtTop AS
(
    SELECT
        a.CodeID,
        a.MachineType,
        a.ErrorCode,
        ROW_NUMBER() OVER (PARTITION BY a.CodeID ORDER BY a.Cnt DESC, a.MachineType ASC, a.ErrorCode ASC) AS rn
    FROM dtAgg a
),
finalRows AS
(
    SELECT
        t.CodeID,
        t.MachineType,
        t.ErrorCode,
        ce.Disp,
        ce.Mc,
        ce.Cause,
        ce.Remedy
    FROM dtTop t
    INNER JOIN ceNorm ce ON ce.CodeID = t.CodeID
    WHERE t.rn = 1
)
INSERT INTO dbo.Posts ([User], CodeID, MachineType, ErrorCode, Tittle, Body, Disp, Mc, Cause, Remedy, [Date])
SELECT
    N'System' AS [User],
    f.CodeID,
    f.MachineType,
    f.ErrorCode,
    LEFT(
        CASE
            WHEN NULLIF(LTRIM(RTRIM(CONVERT(NVARCHAR(200), f.Cause))), N'') IS NULL THEN LTRIM(RTRIM(CONVERT(NVARCHAR(100), f.ErrorCode)))
            ELSE LTRIM(RTRIM(CONVERT(NVARCHAR(100), f.ErrorCode))) + N' - ' + LEFT(LTRIM(RTRIM(CONVERT(NVARCHAR(500), f.Cause))), 500)
        END,
        200
    ) AS Tittle,
    (
        CASE WHEN NULLIF(LTRIM(RTRIM(CONVERT(NVARCHAR(200), f.Disp))), N'') IS NULL THEN N''
             ELSE N'<p><b>Disp:</b> ' +
                 REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(CONVERT(NVARCHAR(200), f.Disp))), N'&', N'&amp;'), N'<', N'&lt;'), N'>', N'&gt;'), N'"', N'&quot;'), N'''', N'&#39;')
                 + N'</p>'
        END
        +
        CASE WHEN NULLIF(LTRIM(RTRIM(CONVERT(NVARCHAR(200), f.Mc))), N'') IS NULL THEN N''
             ELSE N'<p><b>Mc:</b> ' +
                 REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(CONVERT(NVARCHAR(200), f.Mc))), N'&', N'&amp;'), N'<', N'&lt;'), N'>', N'&gt;'), N'"', N'&quot;'), N'''', N'&#39;')
                 + N'</p>'
        END
        +
        CASE WHEN NULLIF(LTRIM(RTRIM(CONVERT(NVARCHAR(MAX), f.Cause))), N'') IS NULL THEN N''
             ELSE N'<p><b>Cause:</b> ' +
                 REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(CONVERT(NVARCHAR(MAX), f.Cause))), N'&', N'&amp;'), N'<', N'&lt;'), N'>', N'&gt;'), N'"', N'&quot;'), N'''', N'&#39;')
                 + N'</p>'
        END
        +
        CASE WHEN NULLIF(LTRIM(RTRIM(CONVERT(NVARCHAR(MAX), f.Remedy))), N'') IS NULL THEN N''
             ELSE N'<p><b>Remedy:</b> ' +
                 REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(CONVERT(NVARCHAR(MAX), f.Remedy))), N'&', N'&amp;'), N'<', N'&lt;'), N'>', N'&gt;'), N'"', N'&quot;'), N'''', N'&#39;')
                 + N'</p>'
        END
    ) AS Body,
    f.Disp,
    f.Mc,
    f.Cause,
    f.Remedy,
    SYSUTCDATETIME() AS [Date]
FROM finalRows f
WHERE NOT EXISTS (SELECT 1 FROM dbo.Posts p WHERE p.CodeID = f.CodeID AND p.[User] = N'System');

SELECT @@ROWCOUNT;
""";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 300 };
        var insertedObj = await command.ExecuteScalarAsync(cancellationToken);
        var inserted = insertedObj is null || insertedObj == DBNull.Value ? 0 : Convert.ToInt32(insertedObj);

        logger?.LogInformation("System-post sync done. Created={Created}.", inserted);
        return inserted;
    }

    private static string BuildSystemTitle(string errorCode, string? cause)
    {
        var c = (cause ?? string.Empty).Trim();
        var baseTitle = string.IsNullOrWhiteSpace(c) ? errorCode.Trim() : $"{errorCode.Trim()} - {c}";
        if (baseTitle.Length <= 200)
        {
            return baseTitle;
        }

        return baseTitle[..200];
    }

    private static string AppendAutoInfo(string bodyHtml, FujidbErrorInfo? info)
    {
        var sanitizedBody = bodyHtml ?? string.Empty;
        if (info is null)
        {
            return sanitizedBody;
        }

        var auto = BuildAutoBodyHtml(info.Disp, info.Mc, info.Cause, info.Remedy);
        if (string.IsNullOrWhiteSpace(auto))
        {
            return sanitizedBody;
        }

        if (string.IsNullOrWhiteSpace(sanitizedBody))
        {
            return auto;
        }

        return sanitizedBody + "<hr/>" + auto;
    }

    private static string BuildAutoBodyHtml(string? disp, string? mc, string? cause, string? remedy)
    {
        static string Encode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return System.Net.WebUtility.HtmlEncode(value.Trim());
        }

        var dispSafe = Encode(disp);
        var mcSafe = Encode(mc);
        var causeSafe = Encode(cause);
        var remedySafe = Encode(remedy);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(dispSafe)) parts.Add($"<p><b>Disp:</b> {dispSafe}</p>");
        if (!string.IsNullOrWhiteSpace(mcSafe)) parts.Add($"<p><b>Mc:</b> {mcSafe}</p>");
        if (!string.IsNullOrWhiteSpace(causeSafe)) parts.Add($"<p><b>Cause:</b> {causeSafe}</p>");
        if (!string.IsNullOrWhiteSpace(remedySafe)) parts.Add($"<p><b>Remedy:</b> {remedySafe}</p>");

        return string.Join(string.Empty, parts);
    }

    private async Task<bool> TryCreateSystemPostIfMissingAsync(
        SqlConnection connection,
        string machineType,
        string errorCode,
        string title,
        string bodyHtml,
        FujidbCandidate candidate,
        CancellationToken cancellationToken)
    {
        const string sql = """
IF NOT EXISTS (SELECT 1 FROM dbo.Posts WHERE CodeID = @CodeID AND [User] = N'System')
BEGIN
    INSERT INTO dbo.Posts ([User], CodeID, MachineType, ErrorCode, Tittle, Body, Disp, Mc, Cause, Remedy, [Date])
    VALUES (N'System', @CodeID, @MachineType, @ErrorCode, @Tittle, @Body, @Disp, @Mc, @Cause, @Remedy, SYSUTCDATETIME());
    SELECT CAST(1 AS BIT);
END
ELSE
BEGIN
    SELECT CAST(0 AS BIT);
END
""";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@CodeID", candidate.CodeId);
        command.Parameters.AddWithValue("@MachineType", machineType);
        command.Parameters.AddWithValue("@ErrorCode", errorCode);
        command.Parameters.AddWithValue("@Tittle", title);
        command.Parameters.AddWithValue("@Body", bodyHtml);
        command.Parameters.AddWithValue("@Disp", (object?)candidate.Disp ?? DBNull.Value);
        command.Parameters.AddWithValue("@Mc", (object?)candidate.Mc ?? DBNull.Value);
        command.Parameters.AddWithValue("@Cause", (object?)candidate.Cause ?? DBNull.Value);
        command.Parameters.AddWithValue("@Remedy", (object?)candidate.Remedy ?? DBNull.Value);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToBoolean(result);
    }

    private async Task<FujidbErrorInfo?> TryResolveErrorAsync(string machineType, string errorCode, CancellationToken cancellationToken)
    {
        var codeIdColumn = await ResolveDowntimeCodeIdColumnNameAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(codeIdColumn))
        {
            return null;
        }

        // Find CodeID via DOWNTIME_D and then bring Disp/Mc/Cause/Remedy from Codigosde_error.
        var sql = $"""
SELECT TOP (1)
    x.CodeID,
    CAST(ce.Disp AS NVARCHAR(200)) AS Disp,
    CAST(ce.Mc AS NVARCHAR(200)) AS Mc,
    CAST(ce.Cause AS NVARCHAR(MAX)) AS Cause,
    CAST(ce.Remedy AS NVARCHAR(MAX)) AS Remedy
FROM [fujidb].[dbo].[DOWNTIME_D] d
OUTER APPLY
(
    SELECT
        COALESCE(
            CONVERT(NVARCHAR(50), TRY_CAST(CONVERT(NVARCHAR(50), d.{codeIdColumn}) AS INT)),
            NULLIF(LTRIM(RTRIM(CONVERT(NVARCHAR(50), d.{codeIdColumn}))), N'')
        ) AS CodeID
) x
INNER JOIN [fujidb].[dbo].[Codigosde_error] ce
    ON COALESCE(
        CONVERT(NVARCHAR(50), TRY_CAST(CONVERT(NVARCHAR(50), ce.CodeID) AS INT)),
        NULLIF(LTRIM(RTRIM(CONVERT(NVARCHAR(50), ce.CodeID))), N'')
    ) = x.CodeID
WHERE CAST(d.MACHINETYPE AS NVARCHAR(100)) = @MachineType
  AND CAST(d.ERRORCODE AS NVARCHAR(100)) = @ErrorCode
    AND x.CodeID IS NOT NULL;
""";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@MachineType", machineType);
        command.Parameters.AddWithValue("@ErrorCode", errorCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new FujidbErrorInfo(
            CodeId: reader.GetString(0),
            Disp: reader.IsDBNull(1) ? null : reader.GetString(1),
            Mc: reader.IsDBNull(2) ? null : reader.GetString(2),
            Cause: reader.IsDBNull(3) ? null : reader.GetString(3),
            Remedy: reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    private sealed record FujidbErrorInfo(string CodeId, string? Disp, string? Mc, string? Cause, string? Remedy);

    private sealed record FujidbCandidate(string CodeId, string MachineType, string ErrorCode, string? Disp, string? Mc, string? Cause, string? Remedy);

    private async Task<string?> ResolveDowntimeCodeIdColumnNameAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_downtimeCodeIdColumnName))
        {
            return _downtimeCodeIdColumnName;
        }

        await _downtimeCodeIdColumnLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_downtimeCodeIdColumnName))
            {
                return _downtimeCodeIdColumnName;
            }

            var overrideName = (_downtimeCodeIdColumnOverride ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(overrideName))
            {
                const string overrideSql = "SELECT COL_LENGTH('fujidb.dbo.DOWNTIME_D', @Name);";
                await using var overrideConnection = new SqlConnection(_connectionString);
                await overrideConnection.OpenAsync(cancellationToken);
                await using var overrideCommand = new SqlCommand(overrideSql, overrideConnection);
                overrideCommand.Parameters.AddWithValue("@Name", overrideName);
                var len = await overrideCommand.ExecuteScalarAsync(cancellationToken);
                if (len is not null && len != DBNull.Value)
                {
                    _downtimeCodeIdColumnName = "[" + overrideName.Replace("]", "]]", StringComparison.Ordinal) + "]";
                    return _downtimeCodeIdColumnName;
                }
            }

            // Prefer COL_LENGTH checks (works cross-db in most setups, avoids needing sys.* permissions).
            const string sql = """
SELECT
    CASE
        WHEN COL_LENGTH('fujidb.dbo.DOWNTIME_D', 'CodeID') IS NOT NULL THEN 'CodeID'
        WHEN COL_LENGTH('fujidb.dbo.DOWNTIME_D', 'CodeId') IS NOT NULL THEN 'CodeId'
        WHEN COL_LENGTH('fujidb.dbo.DOWNTIME_D', 'CODEID') IS NOT NULL THEN 'CODEID'
        WHEN COL_LENGTH('fujidb.dbo.DOWNTIME_D', 'CODE_ID') IS NOT NULL THEN 'CODE_ID'
        WHEN COL_LENGTH('fujidb.dbo.DOWNTIME_D', 'Code_ID') IS NOT NULL THEN 'Code_ID'
        WHEN COL_LENGTH('fujidb.dbo.DOWNTIME_D', 'CODEID_D') IS NOT NULL THEN 'CODEID_D'
        ELSE NULL
    END;
""";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(sql, connection);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            var name = result as string;

            // If the common names don't exist, fall back to probing other columns.
            if (string.IsNullOrWhiteSpace(name))
            {
                name = await TryResolveDowntimeCodeIdColumnByProbingAsync(connection, cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                _downtimeCodeIdColumnName = null;
                return null;
            }

            _downtimeCodeIdColumnName = "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
            return _downtimeCodeIdColumnName;
        }
        finally
        {
            _downtimeCodeIdColumnLock.Release();
        }
    }

    private static int ScorePossibleCodeIdColumn(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return 0;
        }

        var score = 0;
        if (columnName.Equals("DKEY", StringComparison.OrdinalIgnoreCase)) score += 90;
        if (columnName.Equals("DID", StringComparison.OrdinalIgnoreCase)) score += 80;
        if (columnName.Equals("ERRORID", StringComparison.OrdinalIgnoreCase)) score += 70;
        if (columnName.Equals("CodeID", StringComparison.OrdinalIgnoreCase)) score += 100;
        if (columnName.Equals("SUBERRORCODE", StringComparison.OrdinalIgnoreCase)) score += 95;
        if (columnName.Equals("ERRORCODE", StringComparison.OrdinalIgnoreCase)) score += 85;
        if (columnName.Contains("code", StringComparison.OrdinalIgnoreCase)) score += 30;
        if (columnName.EndsWith("id", StringComparison.OrdinalIgnoreCase)) score += 25;
        if (columnName.Contains("id", StringComparison.OrdinalIgnoreCase)) score += 10;
        if (columnName.Contains("error", StringComparison.OrdinalIgnoreCase)) score += 5;
        if (columnName.Contains("downtime", StringComparison.OrdinalIgnoreCase)) score += 5;
        if (columnName.Contains("key", StringComparison.OrdinalIgnoreCase)) score += 8;
        return score;
    }

    private async Task<IReadOnlyList<string>> TryListDowntimeColumnsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string schemaSql = "SELECT TOP (0) * FROM [fujidb].[dbo].[DOWNTIME_D];";
            await using var schemaCmd = new SqlCommand(schemaSql, connection)
            {
                CommandTimeout = 5
            };
            await using var reader = await schemaCmd.ExecuteReaderAsync(cancellationToken);

            var cols = new List<string>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    cols.Add(name);
                }
            }

            return cols;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private async Task<IReadOnlyList<string>> TryListCodigosDeErrorColumnsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string schemaSql = "SELECT TOP (0) * FROM [fujidb].[dbo].[Codigosde_error];";
            await using var schemaCmd = new SqlCommand(schemaSql, connection)
            {
                CommandTimeout = 5
            };
            await using var reader = await schemaCmd.ExecuteReaderAsync(cancellationToken);

            var cols = new List<string>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    cols.Add(name);
                }
            }

            return cols;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private async Task LogJoinDiagnosticsAsync(ILogger logger, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        try
        {
            const string countSql = "SELECT COUNT_BIG(1) FROM [fujidb].[dbo].[Codigosde_error] WITH (NOLOCK);";
            await using var countCmd = new SqlCommand(countSql, connection) { CommandTimeout = 5 };
            var count = await countCmd.ExecuteScalarAsync(cancellationToken);
            logger.LogWarning("Codigosde_error row count = {Count}.", count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not count Codigosde_error.");
        }

        // Log data types for likely join columns.
        try
        {
            const string downtimeSchemaSql = "SELECT TOP (0) ERRORCODE, SUBERRORCODE FROM [fujidb].[dbo].[DOWNTIME_D];";
            await using var dtSchemaCmd = new SqlCommand(downtimeSchemaSql, connection) { CommandTimeout = 5 };
            await using var dtReader = await dtSchemaCmd.ExecuteReaderAsync(cancellationToken);
            var dtSchema = dtReader.GetColumnSchema();
            foreach (var col in dtSchema)
            {
                logger.LogWarning("DOWNTIME_D.{Column} type = {Type}", col.ColumnName, col.DataTypeName);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read DOWNTIME_D schema for ERRORCODE/SUBERRORCODE.");
        }

        try
        {
            const string ceSchemaSql = "SELECT TOP (0) CodeID FROM [fujidb].[dbo].[Codigosde_error];";
            await using var ceSchemaCmd = new SqlCommand(ceSchemaSql, connection) { CommandTimeout = 5 };
            await using var ceReader = await ceSchemaCmd.ExecuteReaderAsync(cancellationToken);
            var ceSchema = ceReader.GetColumnSchema();
            var codeId = ceSchema.FirstOrDefault();
            if (codeId is not null)
            {
                logger.LogWarning("Codigosde_error.CodeID type = {Type}", codeId.DataTypeName);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read Codigosde_error schema for CodeID.");
        }

        // Cheap sampled existence checks (avoid scanning full tables).
        async Task<bool?> SampledJoinExistsAsync(string downtimeColumn)
        {
            try
            {
                var sql = $"""
WITH dSample AS
(
    SELECT TOP (5000) d.{downtimeColumn} AS Val
    FROM [fujidb].[dbo].[DOWNTIME_D] d WITH (NOLOCK)
    WHERE d.{downtimeColumn} IS NOT NULL
)
SELECT TOP (1) 1
FROM dSample d
INNER JOIN [fujidb].[dbo].[Codigosde_error] ce WITH (NOLOCK)
    ON LTRIM(RTRIM(CONVERT(NVARCHAR(200), ce.CodeID))) = LTRIM(RTRIM(CONVERT(NVARCHAR(200), d.Val)));
""";

                await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 5 };
                var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
                return scalar is not null && scalar != DBNull.Value;
            }
            catch
            {
                return null;
            }
        }

        var matchError = await SampledJoinExistsAsync("ERRORCODE");
        var matchSubError = await SampledJoinExistsAsync("SUBERRORCODE");
        logger.LogWarning("Sample join exists? CodeID<->ERRORCODE={MatchError} CodeID<->SUBERRORCODE={MatchSubError} (null=timeout/failed).", matchError, matchSubError);
    }

    private static bool IsObviouslyNotCodeIdColumn(string columnName)
    {
        return columnName.Equals("MACHINETYPE", StringComparison.OrdinalIgnoreCase)
            || columnName.Equals("MC", StringComparison.OrdinalIgnoreCase)
            || columnName.Equals("DISP", StringComparison.OrdinalIgnoreCase)
            || columnName.Equals("CAUSE", StringComparison.OrdinalIgnoreCase)
            || columnName.Equals("REMEDY", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string?> TryResolveDowntimeCodeIdColumnByProbingAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        // Pull column names from information schema (usually accessible cross-db).
        const string listSql = """
SELECT c.COLUMN_NAME
FROM [fujidb].INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_SCHEMA = N'dbo'
  AND c.TABLE_NAME = N'DOWNTIME_D';
""";

        var columnNames = new List<string>();
        await using (var listCmd = new SqlCommand(listSql, connection))
        await using (var reader = await listCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                {
                    columnNames.Add(reader.GetString(0));
                }
            }
        }

        // If metadata visibility is restricted, INFORMATION_SCHEMA may come back empty.
        // In that case, infer columns from an empty resultset schema (requires only SELECT permission).
        if (columnNames.Count == 0)
        {
            const string schemaSql = "SELECT TOP (0) * FROM [fujidb].[dbo].[DOWNTIME_D];";
            await using var schemaCmd = new SqlCommand(schemaSql, connection)
            {
                CommandTimeout = 5
            };
            await using var schemaReader = await schemaCmd.ExecuteReaderAsync(cancellationToken);
            for (var i = 0; i < schemaReader.FieldCount; i++)
            {
                var name = schemaReader.GetName(i);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    columnNames.Add(name);
                }
            }
        }

        // Heuristic: only probe columns likely to be an ID.
        var candidates = columnNames
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Where(c => !IsObviouslyNotCodeIdColumn(c))
            .Where(c => c.Contains("id", StringComparison.OrdinalIgnoreCase)
                || c.Contains("code", StringComparison.OrdinalIgnoreCase)
                || c.Contains("key", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(ScorePossibleCodeIdColumn)
            .Take(25)
            .ToArray();

        if (candidates.Length == 0)
        {
            candidates = columnNames
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Where(c => !IsObviouslyNotCodeIdColumn(c))
                .OrderByDescending(ScorePossibleCodeIdColumn)
                .Take(25)
                .ToArray();
        }

        foreach (var candidate in candidates)
        {
            if (await ColumnJoinsToCodigosDeErrorAsync(connection, candidate, cancellationToken))
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task<bool> ColumnJoinsToCodigosDeErrorAsync(SqlConnection connection, string columnName, CancellationToken cancellationToken)
    {
        // Probe using dynamic SQL so we can reference the identifier safely via QUOTENAME.
        const string probeSql = """
DECLARE @col sysname = @ColumnName;
DECLARE @sql nvarchar(max) = N'
SELECT TOP (1) 1
FROM (
    SELECT TOP (1000) d.' + QUOTENAME(@col) + N' AS Candidate
    FROM [fujidb].[dbo].[DOWNTIME_D] d WITH (NOLOCK)
    WHERE d.' + QUOTENAME(@col) + N' IS NOT NULL
) d
INNER JOIN [fujidb].[dbo].[Codigosde_error] ce WITH (NOLOCK)
    ON COALESCE(
        CONVERT(NVARCHAR(50), TRY_CAST(CONVERT(NVARCHAR(50), ce.CodeID) AS INT)),
        NULLIF(LTRIM(RTRIM(CONVERT(NVARCHAR(50), ce.CodeID))), N'')
    ) = COALESCE(
        CONVERT(NVARCHAR(50), TRY_CAST(CONVERT(NVARCHAR(50), d.Candidate) AS INT)),
        NULLIF(LTRIM(RTRIM(CONVERT(NVARCHAR(50), d.Candidate))), N'')
    )
WHERE d.Candidate IS NOT NULL;';

EXEC sp_executesql @sql;
""";

        try
        {
            await using var cmd = new SqlCommand(probeSql, connection)
            {
                CommandTimeout = 5
            };
            cmd.Parameters.AddWithValue("@ColumnName", columnName);
            var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
            return scalar is not null && scalar != DBNull.Value;
        }
        catch (SqlException)
        {
            // If probing fails (permissions, timeouts, incompatible data types, etc.), treat as non-match.
            return false;
        }
    }
}

public sealed class ForumPostDetails
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

public sealed class ForumPostListItem
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
