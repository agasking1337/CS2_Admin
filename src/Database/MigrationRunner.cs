using System.Data;
using System.Data.SQLite;
using CS2_Admin.Utils;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Npgsql;
using SwiftlyS2.Shared;

namespace CS2_Admin.Database;

public class MigrationRunner
{
    public static void RunMigrations(IDbConnection dbConnection, ISwiftlyCore? core = null)
    {
        try
        {
            core?.Logger.LogInformationIfEnabled("Applying database migrations...");

            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddProvider(new SwiftlyLoggerProvider(core));
            });

            var serviceProvider = new ServiceCollection()
                .AddFluentMigratorCore()
                .ConfigureRunner(rb =>
                {
                    ConfigureDatabase(rb, dbConnection);
                    rb.WithGlobalConnectionString(dbConnection.ConnectionString)
                        .ScanIn(typeof(MigrationRunner).Assembly)
                        .For.Migrations();
                })
                .AddLogging(lb => lb.AddProvider(new SwiftlyLoggerProvider(core)))
                .BuildServiceProvider(false);

            using (var scope = serviceProvider.CreateScope())
            {
                var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
                runner.MigrateUp();
            }

            core?.Logger.LogInformationIfEnabled("Database migrations applied successfully.");
        }
        catch (Exception ex)
        {
            core?.Logger.LogErrorIfEnabled(ex, "Database migrations failed");
            throw;
        }
    }

    private static void ConfigureDatabase(IMigrationRunnerBuilder rb, IDbConnection dbConnection)
    {
        switch (dbConnection)
        {
            case MySqlConnection:
                rb.AddMySql5();
                break;
            case NpgsqlConnection:
                rb.AddPostgres();
                break;
            case SQLiteConnection:
                rb.AddSQLite();
                break;
            default:
                throw new NotSupportedException($"Unsupported database connection type: {dbConnection.GetType().Name}");
        }

        rb.WithGlobalConnectionString(dbConnection.ConnectionString);
    }
}

internal sealed class SwiftlyLoggerProvider : ILoggerProvider
{
    private readonly ISwiftlyCore? _core;

    public SwiftlyLoggerProvider(ISwiftlyCore? core)
    {
        _core = core;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new SwiftlyLogger(_core, categoryName);
    }

    public void Dispose() { }
}

internal sealed class SwiftlyLogger : ILogger
{
    private readonly ISwiftlyCore? _core;
    private readonly string _category;

    public SwiftlyLogger(ISwiftlyCore? core, string category)
    {
        _core = core;
        _category = category;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var message = formatter(state, exception);
        var line = $"[CS2Admin][Migration] [{_category}] {message}";
        if (_core == null) return;
        if (logLevel == LogLevel.Warning)
        {
            _core.Logger.LogWarningIfEnabled(line);
        }
        else if (logLevel == LogLevel.Error || logLevel == LogLevel.Critical)
        {
            if (exception != null) _core.Logger.LogErrorIfEnabled(exception, line);
            else _core.Logger.LogErrorIfEnabled(line);
        }
        else
        {
            _core.Logger.LogInformationIfEnabled(line);
        }
    }
}
