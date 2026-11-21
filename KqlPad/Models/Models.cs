using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using KqlPad.Infrastructure;

namespace KqlPad.Models;

public static class AppJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
}

/// <summary>
/// KQL is stored as an array of lines in the JSON files so they stay readable and diff-friendly.
/// A plain string is accepted too when reading.
/// </summary>
public sealed class KqlLinesConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return reader.GetString() ?? "";
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("kql must be a string or an array of strings");
        var lines = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            lines.Add(reader.GetString() ?? "");
        return string.Join("\n", lines);
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var line in value.Replace("\r\n", "\n").Split('\n'))
            writer.WriteStringValue(line);
        writer.WriteEndArray();
    }
}

public class ParamDef
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string Default { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>When true the value is inserted as-is (no quote escaping), e.g. for lists or numbers.</summary>
    public bool Raw { get; set; }
}

public class QueryDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public List<ParamDef> Parameters { get; set; } = new();

    [JsonConverter(typeof(KqlLinesConverter))]
    public string Kql { get; set; } = "";
}

public class QueryCategory
{
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public List<QueryDef> Queries { get; set; } = new();

    [JsonIgnore] public string? FileName { get; set; }
}

public class ClientProfile : Observable
{
    string _id = Guid.NewGuid().ToString("N");
    string _name = "";
    string _tenantId = "";
    string _workspaceId = "";
    string _workspaceResourceId = "";
    string _loginHint = "";
    string _clientId = "";

    public string Id { get => _id; set => Set(ref _id, value); }
    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>Tenant GUID or a verified domain (contoso.onmicrosoft.com).</summary>
    public string TenantId { get => _tenantId; set => Set(ref _tenantId, value); }

    /// <summary>Log Analytics workspace ID (the "Workspace ID" GUID, not the resource ID).</summary>
    public string WorkspaceId { get => _workspaceId; set => Set(ref _workspaceId, value); }

    /// <summary>Optional ARM resource ID of the workspace; only used for the "Open in portal" link.</summary>
    public string WorkspaceResourceId { get => _workspaceResourceId; set => Set(ref _workspaceResourceId, value); }

    /// <summary>Optional account to pre-select at sign-in, e.g. the admin account for this tenant.</summary>
    public string LoginHint { get => _loginHint; set => Set(ref _loginHint, value); }

    /// <summary>Optional app registration client ID, if the default public client is blocked by the tenant.</summary>
    public string ClientId { get => _clientId; set => Set(ref _clientId, value); }

    public override string ToString() => Name;
}

public class ProfilesFile
{
    public string? LastProfileId { get; set; }
    public List<ClientProfile> Profiles { get; set; } = new();
}
