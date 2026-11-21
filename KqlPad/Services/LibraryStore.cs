using System.Reflection;
using System.Text;
using System.Text.Json;
using KqlPad.Infrastructure;
using KqlPad.Models;

namespace KqlPad.Services;

/// <summary>
/// One JSON file per category in %APPDATA%\KqlPad\queries. The folder is the single source of truth;
/// the starter set shipped inside the exe is copied there on first run.
/// </summary>
public sealed class LibraryStore
{
    public List<QueryCategory> Categories { get; } = new();
    public List<string> LoadErrors { get; } = new();

    public void Load()
    {
        Directory.CreateDirectory(AppPaths.QueriesDir);
        Categories.Clear();
        LoadErrors.Clear();

        if (!File.Exists(AppPaths.SeededMarker))
        {
            foreach (var starter in ReadStarters())
                if (!File.Exists(PathFor(FileNameFor(starter.Category))))
                    Save(starter);
            File.WriteAllText(AppPaths.SeededMarker, DateTime.UtcNow.ToString("O"));
            Categories.Clear();
        }

        foreach (var file in Directory.EnumerateFiles(AppPaths.QueriesDir, "*.json"))
        {
            try
            {
                var cat = JsonSerializer.Deserialize<QueryCategory>(File.ReadAllText(file, Encoding.UTF8), AppJson.Options);
                if (cat is null || string.IsNullOrWhiteSpace(cat.Category))
                    throw new InvalidDataException("missing \"category\" name");
                cat.FileName = Path.GetFileName(file);
                foreach (var q in cat.Queries)
                    if (string.IsNullOrWhiteSpace(q.Id)) q.Id = Slug(q.Name);
                Categories.Add(cat);
            }
            catch (Exception ex)
            {
                LoadErrors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        Sort();
    }

    public void Sort()
    {
        Categories.Sort((a, b) => string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase));
        foreach (var c in Categories)
            c.Queries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    public QueryCategory? FindCategory(string name) =>
        Categories.FirstOrDefault(c => c.Category.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    public QueryCategory? OwnerOf(QueryDef q) => Categories.FirstOrDefault(c => c.Queries.Contains(q));

    public QueryCategory AddCategory(string name)
    {
        var cat = new QueryCategory { Category = name.Trim() };
        Categories.Add(cat);
        Save(cat);
        Sort();
        return cat;
    }

    public string NewQueryId(string name)
    {
        var baseId = Slug(name).ToLowerInvariant();
        var id = baseId;
        var n = 2;
        var all = Categories.SelectMany(c => c.Queries).Select(q => q.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (all.Contains(id)) id = $"{baseId}-{n++}";
        return id;
    }

    public void Save(QueryCategory cat)
    {
        Directory.CreateDirectory(AppPaths.QueriesDir);
        var desired = FileNameFor(cat.Category);
        var target = cat.FileName;
        if (target is null || !target.Equals(desired, StringComparison.OrdinalIgnoreCase))
        {
            target = desired;
            var n = 2;
            while (File.Exists(PathFor(target)))
                target = $"{Path.GetFileNameWithoutExtension(desired)}-{n++}.json";
        }

        File.WriteAllText(PathFor(target), JsonSerializer.Serialize(cat, AppJson.Options), new UTF8Encoding(false));

        if (cat.FileName is not null && !cat.FileName.Equals(target, StringComparison.OrdinalIgnoreCase)
            && File.Exists(PathFor(cat.FileName)))
            File.Delete(PathFor(cat.FileName));
        cat.FileName = target;
    }

    public void DeleteCategory(QueryCategory cat)
    {
        if (cat.FileName is not null && File.Exists(PathFor(cat.FileName)))
            File.Delete(PathFor(cat.FileName));
        Categories.Remove(cat);
    }

    /// <summary>Adds any starter category/query that is missing (matched by category name and query id). Returns queries added.</summary>
    public int RestoreStarters()
    {
        var added = 0;
        foreach (var starter in ReadStarters())
        {
            var existing = FindCategory(starter.Category);
            if (existing is null)
            {
                Categories.Add(starter);
                Save(starter);
                added += starter.Queries.Count;
                continue;
            }
            var ids = existing.Queries.Select(q => q.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = starter.Queries.Where(q => !ids.Contains(q.Id)).ToList();
            if (missing.Count == 0) continue;
            existing.Queries.AddRange(missing);
            Save(existing);
            added += missing.Count;
        }
        Sort();
        return added;
    }

    private static IEnumerable<QueryCategory> ReadStarters()
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.Contains(".Starter.") && n.EndsWith(".json")).OrderBy(n => n))
        {
            using var s = asm.GetManifestResourceStream(res)!;
            var cat = JsonSerializer.Deserialize<QueryCategory>(s, AppJson.Options);
            if (cat is not null && !string.IsNullOrWhiteSpace(cat.Category)) yield return cat;
        }
    }

    private static string PathFor(string fileName) => Path.Combine(AppPaths.QueriesDir, fileName);
    private static string FileNameFor(string category) => Slug(category) + ".json";

    public static string Slug(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text.Trim())
            sb.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-');
        var s = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "-{2,}", "-").Trim('-');
        return s.Length == 0 ? "untitled" : s;
    }
}
