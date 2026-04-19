using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoClaude.Core.Domain;

/// <summary>
/// Standard response format from Claude CLI agent.
/// The agent writes free-form text (narration) and embeds structured data in ```json blocks.
/// </summary>
public class AgentResponse
{
    /// <summary>Free-form text outside of JSON blocks.</summary>
    public string Narrative { get; init; } = "";

    /// <summary>Memories the agent discovered (auto-save, no user interaction needed).</summary>
    public List<AgentMemory> Memories { get; init; } = new();

    /// <summary>Questions for the user.</summary>
    public List<AgentQuestion> Questions { get; init; } = new();

    /// <summary>Main result text (specification, task list, etc.).</summary>
    public string? Result { get; init; }

    /// <summary>
    /// Parse the agent's full text output.
    /// Extracts the last ```json block and parses it as structured data.
    /// Everything else is narrative.
    /// </summary>
    public static AgentResponse Parse(string text)
        => Parse(text, jsonFileContent: null);

    /// <summary>
    /// Parse the agent's output, preferring a separate JSON file when provided.
    /// When <paramref name="jsonFileContent"/> is non-empty and parses as a JSON object,
    /// it is used as the canonical structured payload and <paramref name="text"/> is
    /// treated entirely as narrative. Falls back to text parsing otherwise.
    /// </summary>
    public static AgentResponse Parse(string text, string? jsonFileContent)
    {
        if (!string.IsNullOrWhiteSpace(jsonFileContent))
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonFileContent);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    return ParseJsonBlock(jsonFileContent, (text ?? string.Empty).Trim());
            }
            catch (JsonException) { }
        }

        if (string.IsNullOrWhiteSpace(text))
            return new AgentResponse();

        // Extract ```json ... ``` blocks
        var jsonBlockPattern = new Regex(@"```json\s*\n([\s\S]*?)\n\s*```", RegexOptions.Multiline);
        var matches = jsonBlockPattern.Matches(text);

        // Use the last JSON block (agent may write multiple, last is the final answer)
        string? jsonContent = null;
        if (matches.Count > 0)
        {
            jsonContent = matches[^1].Groups[1].Value.Trim();
        }

        // Narrative = everything outside JSON blocks
        var narrative = jsonBlockPattern.Replace(text, "").Trim();

        if (jsonContent == null)
        {
            // No JSON block — try to find raw JSON in the text
            return ParseFallback(text, narrative);
        }

        return ParseJsonBlock(jsonContent, narrative);
    }

    private static AgentResponse ParseJsonBlock(string json, string narrative)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var memories = new List<AgentMemory>();
            var questions = new List<AgentQuestion>();
            string? result = null;

            // Parse "memories" array
            if (root.TryGetProperty("memories", out var memArr) && memArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in memArr.EnumerateArray())
                {
                    var t = m.TryGetProperty("text", out var tp) ? tp.GetString() ?? "" : "";
                    var a = m.TryGetProperty("answer", out var ap) ? ap.GetString() ?? "" : "";
                    var mem = m.TryGetProperty("memory", out var mp) ? mp.GetString() ?? "persistent" : "persistent";
                    if (!string.IsNullOrWhiteSpace(t))
                        memories.Add(new AgentMemory(t, a, mem == "persistent"));
                }
            }

            // Parse "questions" array
            if (root.TryGetProperty("questions", out var qArr) && qArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var q in qArr.EnumerateArray())
                {
                    if (q.ValueKind == JsonValueKind.String)
                    {
                        var t = q.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(t))
                            questions.Add(new AgentQuestion(t, true));
                    }
                    else if (q.ValueKind == JsonValueKind.Object)
                    {
                        var t = q.TryGetProperty("text", out var tp) ? tp.GetString() ?? "" : "";
                        var mem = q.TryGetProperty("memory", out var mp) ? mp.GetString() ?? "persistent" : "persistent";
                        if (!string.IsNullOrWhiteSpace(t))
                            questions.Add(new AgentQuestion(t, mem == "persistent"));
                    }
                }
            }

            // Parse "result" string
            if (root.TryGetProperty("result", out var resProp) && resProp.ValueKind == JsonValueKind.String)
                result = resProp.GetString();

            // Backward compat: if no memories/questions but has items with "answer" in questions
            // (old format where questions had optional answer field)
            if (memories.Count == 0 && root.TryGetProperty("questions", out var qArr2) && qArr2.ValueKind == JsonValueKind.Array)
            {
                foreach (var q in qArr2.EnumerateArray())
                {
                    if (q.ValueKind == JsonValueKind.Object
                        && q.TryGetProperty("answer", out var ansP)
                        && ansP.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(ansP.GetString()))
                    {
                        var t = q.TryGetProperty("text", out var tp) ? tp.GetString() ?? "" : "";
                        var a = ansP.GetString() ?? "";
                        var mem = q.TryGetProperty("memory", out var mp) ? mp.GetString() ?? "persistent" : "persistent";
                        if (!string.IsNullOrWhiteSpace(t))
                        {
                            memories.Add(new AgentMemory(t, a, mem == "persistent"));
                            // Remove from questions list
                            questions.RemoveAll(x => x.Text == t);
                        }
                    }
                }
            }

            return new AgentResponse
            {
                Narrative = narrative,
                Memories = memories,
                Questions = questions,
                Result = result
            };
        }
        catch (JsonException)
        {
            return new AgentResponse { Narrative = narrative };
        }
    }

    private static AgentResponse ParseFallback(string text, string narrative)
    {
        // Try each JSON block extracted from the text
        foreach (var json in ExtractJsonBlocks(text))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    return ParseJsonBlock(json, narrative);
            }
            catch (JsonException) { }
        }

        return new AgentResponse { Narrative = text };
    }

    /// <summary>
    /// Extracts JSON payloads, preferring the file-based output when provided.
    /// Yields <paramref name="jsonFileContent"/> first if non-empty and valid JSON,
    /// then falls back to fenced/raw blocks in <paramref name="text"/>.
    /// </summary>
    public static IEnumerable<string> ExtractJsonBlocks(string text, string? jsonFileContent)
    {
        if (!string.IsNullOrWhiteSpace(jsonFileContent))
        {
            var trimmed = jsonFileContent.Trim();
            if (TryReadJsonToken(System.Text.Encoding.UTF8.GetBytes(trimmed), 0) > 0)
            {
                yield return trimmed;
                yield break;
            }
        }

        foreach (var block in ExtractJsonBlocks(text))
            yield return block;
    }

    /// <summary>
    /// Extracts all ```json ... ``` fenced blocks from model output.
    /// Falls back to finding raw JSON tokens (arrays/objects) via Utf8JsonReader
    /// when no fenced blocks are found.
    /// </summary>
    public static IEnumerable<string> ExtractJsonBlocks(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        // First: extract fenced ```json blocks
        var fencePattern = new Regex(@"```json\s*\n([\s\S]*?)\n\s*```", RegexOptions.Multiline);
        var matches = fencePattern.Matches(text);

        if (matches.Count > 0)
        {
            foreach (Match match in matches)
            {
                var content = match.Groups[1].Value.Trim();
                if (content.Length > 0)
                    yield return content;
            }
            yield break;
        }

        // Fallback: find raw JSON tokens using Utf8JsonReader
        foreach (var block in ExtractRawJsonTokens(text))
            yield return block;
    }

    private static IEnumerable<string> ExtractRawJsonTokens(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != (byte)'[' && bytes[i] != (byte)'{') continue;

            var length = TryReadJsonToken(bytes, i);
            if (length > 0)
            {
                yield return System.Text.Encoding.UTF8.GetString(bytes, i, length);
                i += length - 1;
            }
        }
    }

    private static int TryReadJsonToken(byte[] bytes, int offset)
    {
        try
        {
            var reader = new Utf8JsonReader(
                bytes.AsSpan(offset),
                new JsonReaderOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

            if (reader.Read() && reader.TrySkip())
                return (int)reader.BytesConsumed;
        }
        catch (JsonException) { }

        return 0;
    }

    /// <summary>
    /// Convenience: extract just the result text from raw output.
    /// Falls back to narrative if no result field.
    /// </summary>
    public static string ExtractResult(string rawOutput)
        => ExtractResult(rawOutput, jsonFileContent: null);

    /// <summary>
    /// Convenience: extract just the result text, preferring the file-based JSON
    /// payload when provided. Falls back to narrative if no result field.
    /// </summary>
    public static string ExtractResult(string rawOutput, string? jsonFileContent)
    {
        var parsed = Parse(rawOutput, jsonFileContent);
        return parsed.Result ?? parsed.Narrative;
    }
}

public record AgentMemory(string Text, string Answer, bool Persistent);
public record AgentQuestion(string Text, bool Persistent);
