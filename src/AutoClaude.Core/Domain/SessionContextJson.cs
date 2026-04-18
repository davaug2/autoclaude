using System.Text.Json;
using AutoClaude.Core.Domain.Models;

namespace AutoClaude.Core.Domain;

public static class SessionContextJson
{
    public const string AllowedDirectoriesKey = "allowed_directories";
    public const string CliSessionMapKey = "cli_session_map";

    public static void HydrateAllowedDirectories(Session session)
    {
        try
        {
            using var doc = JsonDocument.Parse(session.ContextJson);
            if (!doc.RootElement.TryGetProperty(AllowedDirectoriesKey, out var dirs) || dirs.ValueKind != JsonValueKind.Array)
                return;
            session.AllowedDirectories = dirs.EnumerateArray()
                .Select(e => e.GetString())
                .Where(s => !string.IsNullOrEmpty(s))
                .Cast<string>()
                .ToList();
        }
        catch (JsonException)
        {
        }
    }

    public static string MergeAllowedDirectories(string contextJson, IReadOnlyList<string> directories)
    {
        var dict = ParseContextDict(contextJson);
        dict[AllowedDirectoriesKey] = JsonSerializer.SerializeToElement(directories.ToList());
        return JsonSerializer.Serialize(dict);
    }

    public static Dictionary<string, string> HydrateCliSessionMap(string contextJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(contextJson);
            if (doc.RootElement.TryGetProperty(CliSessionMapKey, out var map) && map.ValueKind == JsonValueKind.Object)
            {
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in map.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        result[prop.Name] = prop.Value.GetString()!;
                }
                return result;
            }
        }
        catch (JsonException) { }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public static string MergeCliSessionMap(string contextJson, Dictionary<string, string> sessionMap)
    {
        var dict = ParseContextDict(contextJson);
        dict[CliSessionMapKey] = JsonSerializer.SerializeToElement(sessionMap);
        return JsonSerializer.Serialize(dict);
    }

    private static Dictionary<string, JsonElement> ParseContextDict(string contextJson)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(contextJson) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }
}
