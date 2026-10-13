using System.Text.Json;

namespace GitCompass.Terminal.Windows;

internal sealed record TerminalBridgeMessage(string Type, string? Data = null, int Columns = 0, int Rows = 0);

internal static class TerminalBridge
{
    internal const string Origin = "https://terminal.gitcompass.local/";
    internal const string Document = Origin + "index.html";
    internal static bool IsDocument(string? uri) => string.Equals(uri, Document, StringComparison.Ordinal);
    internal static bool IsLocalResource(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return false;
        return parsed.Scheme == "https" && parsed.Host == "terminal.gitcompass.local" && parsed.IsDefaultPort &&
            parsed.UserInfo.Length == 0 && parsed.Query.Length == 0 && parsed.Fragment.Length == 0;
    }

    internal static bool TryParse(string source, string json, out TerminalBridgeMessage? message)
    {
        message = null;
        if (!IsDocument(source) || json.Length > 100000) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return false;
            string[] fields;
            switch (type.GetString())
            {
                case "ready": case "pasteRequest": case "close":
                    fields = ["type"];
                    message = new(type.GetString()!);
                    break;
                case "input":
                    fields = ["type", "data"];
                    if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.String || data.GetString()!.Length > 4096) return false;
                    message = new("input", data.GetString());
                    break;
                case "resize":
                    fields = ["type", "columns", "rows"];
                    if (!root.TryGetProperty("columns", out var columns) || !columns.TryGetInt32(out var c) || c is < 2 or > 500 ||
                        !root.TryGetProperty("rows", out var rows) || !rows.TryGetInt32(out var r) || r is < 1 or > 300) return false;
                    message = new("resize", Columns: c, Rows: r);
                    break;
                default: return false;
            }
            var names = root.EnumerateObject().Select(p => p.Name).ToArray();
            return names.Length == fields.Length && names.Distinct(StringComparer.Ordinal).Count() == fields.Length && fields.All(names.Contains);
        }
        catch (JsonException) { return false; }
    }
}
