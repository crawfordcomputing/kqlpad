using System.Text.RegularExpressions;
using KqlPad.Models;

namespace KqlPad.Services;

/// <summary>
/// Placeholders look like {{upn}}. Square brackets are real KQL syntax so they are not used.
/// Values are escaped so they are safe inside either '...' or "..." KQL string literals,
/// unless the parameter is marked Raw.
/// </summary>
public static partial class Templating
{
    [GeneratedRegex(@"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}")]
    private static partial Regex Token();

    public static IReadOnlyList<string> FindTokens(string kql) =>
        Token().Matches(kql).Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static string Render(QueryDef query, IReadOnlyDictionary<string, string> values)
    {
        var raw = query.Parameters.Where(p => p.Raw).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Token().Replace(query.Kql, m =>
        {
            var name = m.Groups[1].Value;
            if (!values.TryGetValue(name, out var value)) return m.Value;
            value = value.Trim();
            return raw.Contains(name) ? value : Escape(value);
        });
    }

    private static string Escape(string v) =>
        v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("'", "\\'")
         .Replace("\r", "").Replace("\n", " ");
}
