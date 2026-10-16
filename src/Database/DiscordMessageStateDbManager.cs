using CS2_Admin.Models;
using CS2_Admin.Utils;
using Dommel;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Database;

public class DiscordMessageStateDbManager
{
    private readonly ISwiftlyCore _core;

    public DiscordMessageStateDbManager(ISwiftlyCore core)
    {
        _core = core;
    }

    public Task InitializeAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            MigrationRunner.RunMigrations(connection);
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("Discord message state initialization warning: {Message}", ex.Message);
        }

        return Task.CompletedTask;
    }

    public Task<string?> GetMessageIdAsync(string messageKey)
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var row = connection.FirstOrDefault<DiscordSharedMessageState>(x => x.MessageKey == messageKey);
            return Task.FromResult(string.IsNullOrWhiteSpace(row?.MessageId) ? null : row.MessageId);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error reading discord message state: {Message}", ex.Message);
            return Task.FromResult<string?>(null);
        }
    }

    public Task UpsertMessageIdAsync(string messageKey, string channelId, string messageId)
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var now = DateTime.UtcNow;
            var row = connection.FirstOrDefault<DiscordSharedMessageState>(x => x.MessageKey == messageKey);
            if (row != null)
            {
                row.ChannelId = channelId;
                row.MessageId = messageId;
                row.UpdatedAt = now;
                connection.Update(row);
                return Task.CompletedTask;
            }

            connection.Insert(new DiscordSharedMessageState
            {
                MessageKey = messageKey,
                ChannelId = channelId,
                MessageId = messageId,
                UpdatedAt = now
            });
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error writing discord message state: {Message}", ex.Message);
        }

        return Task.CompletedTask;
    }
}
