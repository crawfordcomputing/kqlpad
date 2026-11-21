using System.Collections.ObjectModel;
using KqlPad.Infrastructure;
using KqlPad.Models;
using KqlPad.Services;

namespace KqlPad.ViewModels;

public sealed class MainViewModel : Observable
{
    readonly LibraryStore _lib = App.Library;

    // Values typed into a parameter are remembered by name so {{upn}} carries over between queries.
    readonly Dictionary<string, string> _sharedValues = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<CategoryVM> Tree { get; } = new();
    public ObservableCollection<ParamVM> Params { get; } = new();
    public ObservableCollection<ClientProfile> Profiles => App.Profiles.Profiles;

    string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set { if (Set(ref _searchText, value)) RebuildTree(); }
    }

    QueryItemVM? _selectedItem;
    public QueryItemVM? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!Set(ref _selectedItem, value)) return;
            LoadSelection();
            Raise(nameof(HasSelection));
        }
    }
    public bool HasSelection => _selectedItem is not null;

    public CategoryVM? SelectedCategory { get; set; }

    string _renderedKql = "";
    public string RenderedKql { get => _renderedKql; private set => Set(ref _renderedKql, value); }

    ClientProfile? _selectedProfile;
    public ClientProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!Set(ref _selectedProfile, value)) return;
            if (value is not null)
            {
                App.Profiles.LastProfileId = value.Id;
                if (!App.Profiles.Save()) Status = App.Profiles.LastSaveError!;
            }
            Raise(nameof(ProfileSummary));
        }
    }

    public string ProfileSummary => _selectedProfile is null
        ? "No client selected - use Manage clients to add one"
        : $"Tenant {Short(_selectedProfile.TenantId)}   Workspace {Short(_selectedProfile.WorkspaceId)}";

    string _status = "Ready";
    public string Status { get => _status; set => Set(ref _status, value); }

    bool _isRunning;
    public bool IsRunning { get => _isRunning; set { if (Set(ref _isRunning, value)) Raise(nameof(IsNotRunning)); } }
    public bool IsNotRunning => !_isRunning;

    public MainViewModel()
    {
        RebuildTree();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == App.Profiles.LastProfileId) ?? Profiles.FirstOrDefault();
    }

    public void RefreshProfileSummary() => Raise(nameof(ProfileSummary));

    static string Short(string s) => string.IsNullOrWhiteSpace(s) ? "(not set)" : s.Length > 13 ? s[..8] + "..." : s;

    // ---- tree ------------------------------------------------------------------------------

    public void RebuildTree(QueryDef? select = null)
    {
        var expanded = Tree.Where(c => c.IsExpanded).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selModel = select ?? _selectedItem?.Model;
        var term = SearchText.Trim();
        var searching = term.Length > 0;

        var nodes = new List<CategoryVM>();
        QueryItemVM? newSel = null;
        foreach (var cat in _lib.Categories)
        {
            var node = new CategoryVM(cat)
            {
                IsExpanded = searching || expanded.Contains(cat.Category) || (select is not null && cat.Queries.Contains(select)),
            };
            foreach (var q in cat.Queries)
            {
                if (searching && !Matches(cat, q, term)) continue;
                var item = new QueryItemVM(node, q);
                node.Items.Add(item);
                if (ReferenceEquals(q, selModel)) { newSel = item; item.IsSelected = true; }
            }
            if (!searching || node.Items.Count > 0) nodes.Add(node);
        }

        Tree.Clear();
        foreach (var n in nodes) Tree.Add(n);

        if (newSel is not null) { SelectedItem = newSel; SelectedCategory = newSel.Parent; }
    }

    static bool Matches(QueryCategory cat, QueryDef q, string term)
    {
        foreach (var word in term.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var hit = Has(q.Name, word) || Has(q.Description, word) || Has(cat.Category, word)
                      || Has(q.Kql, word) || q.Tags.Any(t => Has(t, word));
            if (!hit) return false;
        }
        return true;
    }

    static bool Has(string? haystack, string needle) =>
        haystack?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true;

    // ---- selection / rendering -------------------------------------------------------------

    void LoadSelection()
    {
        Params.Clear();
        if (_selectedItem is not null)
        {
            foreach (var def in _selectedItem.Model.Parameters)
            {
                var value = _sharedValues.TryGetValue(def.Name, out var v) ? v : def.Default;
                Params.Add(new ParamVM(def, value, OnParamChanged));
            }
        }
        Render();
    }

    void OnParamChanged(ParamVM p)
    {
        _sharedValues[p.Name] = p.Value;
        Render();
    }

    void Render()
    {
        if (_selectedItem is null) { RenderedKql = ""; return; }
        var values = Params.ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
        RenderedKql = Templating.Render(_selectedItem.Model, values);
    }

    // ---- editing ---------------------------------------------------------------------------

    public IEnumerable<string> CategoryNames => _lib.Categories.Select(c => c.Category);

    /// <summary>Adds a new query, or replaces <paramref name="original"/> with the edited copy.</summary>
    public void SaveQuery(QueryDef? original, QueryDef edited, string categoryName)
    {
        var target = _lib.FindCategory(categoryName) ?? _lib.AddCategory(categoryName);

        if (original is null)
        {
            if (string.IsNullOrWhiteSpace(edited.Id)) edited.Id = _lib.NewQueryId(edited.Name);
            target.Queries.Add(edited);
        }
        else
        {
            var source = _lib.OwnerOf(original);
            if (source is not null && !ReferenceEquals(source, target))
            {
                source.Queries.Remove(original);
                _lib.Save(source);
                target.Queries.Add(edited);
            }
            else
            {
                var idx = target.Queries.IndexOf(original);
                if (idx >= 0) target.Queries[idx] = edited; else target.Queries.Add(edited);
            }
        }

        _lib.Save(target);
        _lib.Sort();
        RebuildTree(edited);
        Status = $"Saved \"{edited.Name}\" in {target.Category}";
    }

    public void DeleteQuery(QueryDef q)
    {
        var owner = _lib.OwnerOf(q);
        if (owner is null) return;
        owner.Queries.Remove(q);
        _lib.Save(owner);
        if (_selectedItem?.Model == q) SelectedItem = null;
        RebuildTree();
        Status = $"Deleted \"{q.Name}\"";
    }

    public QueryDef Duplicate(QueryDef q)
    {
        var copy = AppJson.Clone(q);
        copy.Name += " (copy)";
        copy.Id = _lib.NewQueryId(copy.Name);
        var owner = _lib.OwnerOf(q)!;
        owner.Queries.Add(copy);
        _lib.Save(owner);
        _lib.Sort();
        RebuildTree(copy);
        return copy;
    }

    public void AddCategory(string name)
    {
        if (_lib.FindCategory(name) is not null) return;
        _lib.AddCategory(name);
        RebuildTree();
    }

    public void RenameCategory(QueryCategory cat, string newName)
    {
        cat.Category = newName.Trim();
        _lib.Save(cat);
        _lib.Sort();
        RebuildTree();
    }

    public void DeleteCategory(QueryCategory cat)
    {
        if (_selectedItem is not null && cat.Queries.Contains(_selectedItem.Model)) SelectedItem = null;
        _lib.DeleteCategory(cat);
        RebuildTree();
    }

    public int RestoreStarters()
    {
        var added = _lib.RestoreStarters();
        RebuildTree();
        return added;
    }
}
