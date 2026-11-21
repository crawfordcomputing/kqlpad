using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using KqlPad.Infrastructure;
using KqlPad.Models;

namespace KqlPad.Services;

public sealed class QueryResult
{
    public required string[] ColumnNames { get; init; }

    /// <summary>Columns are named c0, c1, ... so odd KQL column names never break WPF bindings.</summary>
    public required DataTable Table { get; init; }

    public TimeSpan Elapsed { get; init; }
    public int RowCount => Table.Rows.Count;
}

public sealed class QueryException(string message) : Exception(message);

public sealed class LogAnalyticsClient
{
    private static readonly string[] Scopes = ["https://api.loganalytics.io/.default"];
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly Dictionary<string, InteractiveBrowserCredential> _credentials = new();

    public async Task<QueryResult> RunAsync(ClientProfile profile, string kql, CancellationToken ct)
    {
        Validate(profile);
        var started = DateTime.UtcNow;

        var credential = await GetCredentialAsync(profile, ct);
        var token = await credential.GetTokenAsync(new TokenRequestContext(Scopes), ct);

        var url = $"https://api.loganalytics.io/v1/workspaces/{Uri.EscapeDataString(profile.WorkspaceId.Trim())}/query";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(new { query = kql }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.TryAddWithoutValidation("Prefer", "wait=180");

        using var response = await Http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new QueryException(DescribeError(response.StatusCode, body));

        return Parse(body, DateTime.UtcNow - started);
    }

    /// <summary>Forget the cached sign-in for this client so the next run prompts again. Other clients are not affected.</summary>
    public void ForgetSignIn(ClientProfile profile)
    {
        foreach (var k in _credentials.Keys.Where(k => k.StartsWith(profile.Id + "|", StringComparison.Ordinal)).ToList())
            _credentials.Remove(k);

        DeleteRecords(profile, keep: null);

        // Records from older versions were shared by every client with the same tenant/login/app.
        // Only drop one if no other client still relies on it.
        var legacy = LegacyRecordPath(AuthKey(profile));
        var shared = App.Profiles.Profiles.Any(o => !ReferenceEquals(o, profile) && o.Id != profile.Id && AuthKey(o) == AuthKey(profile));
        if (!shared) TryDelete(legacy);
    }

    private static void Validate(ClientProfile p)
    {
        if (string.IsNullOrWhiteSpace(p.TenantId)) throw new QueryException("This client has no Tenant ID. Open Manage clients and fill it in.");
        if (string.IsNullOrWhiteSpace(p.WorkspaceId)) throw new QueryException("This client has no Workspace ID. Open Manage clients and fill it in.");
    }

    /// <summary>The sign-in settings; when any of them change, the saved sign-in no longer applies.</summary>
    private static string AuthKey(ClientProfile p) =>
        $"{p.TenantId.Trim().ToLowerInvariant()}|{p.ClientId.Trim().ToLowerInvariant()}|{p.LoginHint.Trim().ToLowerInvariant()}";

    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..16];

    /// <summary>One record per client (profile ID), suffixed with its current sign-in settings.</summary>
    private static string RecordPrefix(ClientProfile p) => Hash("profile|" + p.Id) + "-";

    private static string RecordPath(ClientProfile p) =>
        Path.Combine(AppPaths.AuthDir, RecordPrefix(p) + Hash(AuthKey(p)) + ".json");

    private static string LegacyRecordPath(string authKey) => Path.Combine(AppPaths.AuthDir, Hash(authKey) + ".json");

    /// <summary>Deletes this client's record files, except <paramref name="keep"/> (the one for its current settings).</summary>
    private static void DeleteRecords(ClientProfile p, string? keep)
    {
        if (!Directory.Exists(AppPaths.AuthDir)) return;
        foreach (var f in Directory.EnumerateFiles(AppPaths.AuthDir, RecordPrefix(p) + "*.json"))
            if (keep is null || !string.Equals(f, keep, StringComparison.OrdinalIgnoreCase)) TryDelete(f);
    }

    private static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static async Task<AuthenticationRecord?> ReadRecordAsync(string file, CancellationToken ct)
    {
        if (!File.Exists(file)) return null;
        try
        {
            await using var fs = File.OpenRead(file);
            return await AuthenticationRecord.DeserializeAsync(fs, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return null; }
    }

    private async Task<InteractiveBrowserCredential> GetCredentialAsync(ClientProfile p, CancellationToken ct)
    {
        var authKey = AuthKey(p);
        var cacheKey = p.Id + "|" + authKey;
        if (_credentials.TryGetValue(cacheKey, out var cached)) return cached;

        // Settings changed since the last sign-in: drop the stale credential and record for this client.
        foreach (var k in _credentials.Keys.Where(k => k.StartsWith(p.Id + "|", StringComparison.Ordinal)).ToList())
            _credentials.Remove(k);
        Directory.CreateDirectory(AppPaths.AuthDir);
        var recordFile = RecordPath(p);
        DeleteRecords(p, keep: recordFile);

        var record = await ReadRecordAsync(recordFile, ct);
        var migrated = false;
        if (record is null)
        {
            // Carry over a sign-in saved by an older version so the user is not prompted again.
            record = await ReadRecordAsync(LegacyRecordPath(authKey), ct);
            migrated = record is not null;
        }

        var options = new InteractiveBrowserCredentialOptions
        {
            TenantId = p.TenantId.Trim(),
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions { Name = "KqlPad" },
            AuthenticationRecord = record,
        };
        if (!string.IsNullOrWhiteSpace(p.LoginHint)) options.LoginHint = p.LoginHint.Trim();
        if (!string.IsNullOrWhiteSpace(p.ClientId)) options.ClientId = p.ClientId.Trim();

        var credential = new InteractiveBrowserCredential(options);
        if (record is null)
            record = await credential.AuthenticateAsync(new TokenRequestContext(Scopes), ct);
        if (record is not null && (migrated || !File.Exists(recordFile)))
        {
            await using var fs = File.Create(recordFile);
            await record.SerializeAsync(fs, ct);
        }

        _credentials[cacheKey] = credential;
        return credential;
    }

    private static string DescribeError(HttpStatusCode status, string body)
    {
        var message = body;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var err = doc.RootElement.GetProperty("error");
            var sb = new StringBuilder();
            var top = MessageOf(err);
            if (top is not null) sb.Append(top);
            if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("innererror", out var inner))
            {
                if (MessageOf(inner) is { } s && s != top) Append(sb, s);
                if (inner.ValueKind == JsonValueKind.Object && inner.TryGetProperty("innererror", out var inner2) && MessageOf(inner2) is { } s2) Append(sb, s2);
            }
            if (sb.Length > 0) message = sb.ToString();
        }
        catch { /* not JSON, keep raw body */ }

        var text = $"HTTP {(int)status} {status}\n{message}";
        if (status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            text += "\n\nSigned in, but this account has no data access to the workspace. Global Administrator does not grant Azure RBAC: " +
                    "assign Log Analytics Reader on the workspace, or elevate access in Entra (Properties > Access management for Azure resources).";
        return text;
    }

    private static string? MessageOf(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString()
            : null;

    private static void Append(StringBuilder sb, string s)
    {
        if (sb.Length > 0) sb.Append('\n');
        sb.Append(s);
    }

    private static QueryResult Parse(string json, TimeSpan elapsed)
    {
        using var doc = JsonDocument.Parse(json);
        var table = doc.RootElement.GetProperty("tables")[0];
        var cols = table.GetProperty("columns").EnumerateArray()
            .Select(c => (Name: c.GetProperty("name").GetString() ?? "", Type: c.GetProperty("type").GetString() ?? "string"))
            .ToArray();

        var dt = new DataTable();
        for (var i = 0; i < cols.Length; i++)
            dt.Columns.Add($"c{i}", ClrType(cols[i].Type));

        foreach (var row in table.GetProperty("rows").EnumerateArray())
        {
            var values = new object?[cols.Length];
            var i = 0;
            foreach (var cell in row.EnumerateArray())
            {
                if (i >= cols.Length) break;
                values[i] = ToCell(cell, cols[i].Type);
                i++;
            }
            dt.Rows.Add(values);
        }

        return new QueryResult { ColumnNames = cols.Select(c => c.Name).ToArray(), Table = dt, Elapsed = elapsed };
    }

    private static Type ClrType(string kustoType) => kustoType switch
    {
        "long" or "int" => typeof(long),
        "real" or "decimal" => typeof(double),
        "bool" => typeof(bool),
        _ => typeof(string),
    };

    private static object ToCell(JsonElement cell, string kustoType)
    {
        if (cell.ValueKind == JsonValueKind.Null || cell.ValueKind == JsonValueKind.Undefined) return DBNull.Value;
        switch (kustoType)
        {
            case "long" or "int":
                return cell.ValueKind == JsonValueKind.Number && cell.TryGetInt64(out var l) ? l : DBNull.Value;
            case "real" or "decimal":
                return cell.ValueKind == JsonValueKind.Number ? cell.GetDouble() : DBNull.Value;
            case "bool":
                return cell.ValueKind is JsonValueKind.True or JsonValueKind.False ? cell.GetBoolean() : DBNull.Value;
            case "datetime":
                var s = cell.GetString();
                return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto)
                    ? dto.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    : s ?? "";
            default:
                return cell.ValueKind == JsonValueKind.String ? cell.GetString() ?? "" : cell.GetRawText();
        }
    }
}
