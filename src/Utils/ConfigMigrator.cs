using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Utils;

public static class ConfigMigrator
{
    public static void EnsureVersionedConfigFile<T>(ISwiftlyCore core, string filePath, string fileName, string sectionName, int expectedVersion) where T : class, new()
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var root = JsonNode.Parse(json) as JsonObject;
            if (root == null)
            {
                RecreateConfigFile(core, filePath, fileName, expectedVersion, "root is not a JSON object");
                return;
            }

            if (!TryReadVersionFromNode(root, sectionName, out var currentVersion) || currentVersion != expectedVersion)
            {
                T? migratedObj = null;
                try
                {
                    var sectionNode = string.IsNullOrWhiteSpace(sectionName) ? root : (root[sectionName] as JsonObject ?? root);
                    var sectionJson = sectionNode.ToJsonString();
                    
                    var options = new JsonSerializerOptions 
                    { 
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true,
                        PropertyNameCaseInsensitive = true 
                    };
                    migratedObj = JsonSerializer.Deserialize<T>(sectionJson, options);
                }
                catch
                {
                    // Ignore deserialization errors and rely on defaults
                }

                migratedObj ??= new T();

                var versionProperty = typeof(T).GetProperty("Version");
                if (versionProperty != null && versionProperty.CanWrite)
                {
                    versionProperty.SetValue(migratedObj, expectedVersion);
                }

                var writeOptions = new JsonSerializerOptions 
                { 
                    WriteIndented = true, 
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                };

                string outJson;
                if (!string.IsNullOrWhiteSpace(sectionName))
                {
                    var outRoot = new Dictionary<string, T> { { sectionName, migratedObj } };
                    outJson = JsonSerializer.Serialize(outRoot, writeOptions);
                }
                else
                {
                    outJson = JsonSerializer.Serialize(migratedObj, writeOptions);
                }

                File.WriteAllText(filePath, outJson);
                core.Logger.LogInformationIfEnabled("Migrated {File} to version {Version}.", fileName, expectedVersion);
            }
        }
        catch (Exception ex)
        {
            RecreateConfigFile(core, filePath, fileName, expectedVersion, ex.Message);
        }
    }

    private static void RecreateConfigFile(ISwiftlyCore core, string filePath, string fileName, int expectedVersion, string reason)
    {
        try
        {
            File.Delete(filePath);
            core.Logger.LogWarningIfEnabled(
                "[CS2Admin] {File} version mismatch/corruption ({Reason}). File deleted and will be regenerated with version {Version}.",
                fileName,
                reason,
                expectedVersion);
        }
        catch (Exception ex)
        {
            core.Logger.LogWarningIfEnabled(
                "[CS2Admin] Failed to delete {File} for version reset: {Message}",
                fileName,
                ex.Message);
        }
    }

    private static bool TryReadVersionFromNode(JsonObject root, string sectionName, out int version)
    {
        version = 0;

        // Try root level
        if (TryParseVersionNode(root["Version"], out version))
        {
            return true;
        }

        // Try section level
        if (!string.IsNullOrWhiteSpace(sectionName) && root[sectionName] is JsonObject section && TryParseVersionNode(section["Version"], out version))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseVersionNode(JsonNode? node, out int version)
    {
        version = 0;
        if (node == null)
        {
            return false;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out var intValue))
            {
                version = intValue;
                return true;
            }

            if (value.TryGetValue<string>(out var textValue) &&
                int.TryParse(textValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                version = parsed;
                return true;
            }
        }

        return false;
    }
}
