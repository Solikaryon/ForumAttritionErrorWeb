using ForoAttritionErrorWeb.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ForoAttritionErrorWeb.Infrastructure;

public static class DatabaseBootstrapper
{
    public static async Task EnsureCreatedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var repository = scope.ServiceProvider.GetRequiredService<SqlUserRepository>();
        var posts = scope.ServiceProvider.GetRequiredService<SqlForumPostRepository>();
        var answers = scope.ServiceProvider.GetRequiredService<SqlForumPostAnswerRepository>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("DatabaseBootstrapper");
        if (!repository.IsConfigured)
        {
            return;
        }

        try
        {
            var cs = configuration.GetConnectionString("AttritionForo") ?? string.Empty;
            var builder = new SqlConnectionStringBuilder(cs);
            var targetDatabase = builder.InitialCatalog;
            if (string.IsNullOrWhiteSpace(targetDatabase))
            {
                targetDatabase = "AttritionForo";
            }

            // Connect to master first; this avoids startup crashes if the login cannot open the target DB.
            var masterBuilder = new SqlConnectionStringBuilder(cs)
            {
                InitialCatalog = "master"
            };

            await using (var master = new SqlConnection(masterBuilder.ConnectionString))
            {
                await master.OpenAsync(cancellationToken);
                var ensureDbSql = "IF DB_ID(@DbName) IS NULL BEGIN EXEC('CREATE DATABASE [' + @DbName + ']'); END";
                await using var ensureDb = new SqlCommand(ensureDbSql, master);
                ensureDb.Parameters.AddWithValue("@DbName", targetDatabase);
                await ensureDb.ExecuteNonQueryAsync(cancellationToken);
            }

            // Now ensure tables in the target database.
            await repository.EnsureUsersTableAsync(cancellationToken);
            await posts.EnsurePostsTableAsync(cancellationToken);
            await answers.EnsurePostAnswersTableAsync(cancellationToken);

            // Auto-create posts based on fujidb relationships (CodeID join)
            await posts.EnsureSystemPostsFromFujidbAsync(logger, cancellationToken);
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "Database bootstrap failed (SQL error). Verify the SQL login has access to the database and permissions to create tables.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database bootstrap failed. The app will continue without DB bootstrap.");
        }
    }
}
