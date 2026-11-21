using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using KqlPad.Models;
using KqlPad.Services;

namespace KqlPad.Views;

public partial class QueryEditorWindow : Window
{
    readonly QueryDef? _original;

    public ObservableCollection<ParamDef> Params { get; } = new();
    public QueryDef? Result { get; private set; }
    public string CategoryName { get; private set; } = "";

    public QueryEditorWindow(QueryDef? original, IReadOnlyList<string> categories, string? category)
    {
        InitializeComponent();
        Infrastructure.ConsoleChrome.Attach(this);
        DataContext = this;
        _original = original;
        Title = original is null ? "NEW QUERY" : "EDIT QUERY";

        CategoryBox.ItemsSource = categories;
        CategoryBox.Text = category ?? categories.FirstOrDefault() ?? "";

        if (original is not null)
        {
            var copy = AppJson.Clone(original);
            NameBox.Text = copy.Name;
            DescriptionBox.Text = copy.Description;
            TagsBox.Text = string.Join(", ", copy.Tags);
            KqlBox.Text = copy.Kql;
            foreach (var p in copy.Parameters) Params.Add(p);
        }
        Loaded += (_, _) => (original is null ? NameBox : KqlBox).Focus();
    }

    void Detect_Click(object sender, RoutedEventArgs e) => AddMissingParams();

    int AddMissingParams()
    {
        ParamGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var existing = Params.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var token in Templating.FindTokens(KqlBox.Text))
        {
            if (existing.Contains(token)) continue;
            Params.Add(new ParamDef { Name = token, Label = token });
            added++;
        }
        return added;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        ParamGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var category = CategoryBox.Text.Trim();
        var name = NameBox.Text.Trim();
        if (category.Length == 0) { Warn("Category is required."); return; }
        if (name.Length == 0) { Warn("Name is required."); return; }
        if (string.IsNullOrWhiteSpace(KqlBox.Text)) { Warn("KQL is required."); return; }

        var named = Params.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
        foreach (var p in named) p.Name = p.Name.Trim();
        var bad = named.FirstOrDefault(p => !Regex.IsMatch(p.Name, "^[A-Za-z_][A-Za-z0-9_]*$"));
        if (bad is not null) { Warn($"Parameter name \"{bad.Name}\" is invalid. Use letters, digits and underscores."); return; }
        var dup = named.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) { Warn($"Parameter \"{dup.Key}\" is listed twice."); return; }

        // Any {{token}} without a row gets one automatically so the form always shows it.
        var missing = Templating.FindTokens(KqlBox.Text).Where(t => !named.Any(p => p.Name.Equals(t, StringComparison.OrdinalIgnoreCase)));
        foreach (var t in missing) named.Add(new ParamDef { Name = t, Label = t });

        CategoryName = category;
        Result = new QueryDef
        {
            Id = _original?.Id ?? "",
            Name = name,
            Description = DescriptionBox.Text.Trim(),
            Tags = TagsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Parameters = named,
            Kql = KqlBox.Text.Replace("\r\n", "\n").TrimEnd(),
        };
        DialogResult = true;
    }

    void Warn(string message) => MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
}
