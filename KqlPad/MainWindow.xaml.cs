using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using KqlPad.Infrastructure;
using KqlPad.Models;
using KqlPad.Services;
using KqlPad.ViewModels;
using KqlPad.Views;
using Microsoft.Win32;

namespace KqlPad;

public partial class MainWindow : Window
{
    CancellationTokenSource? _cts;
    QueryResult? _result;

    MainViewModel Vm => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        ConsoleChrome.Attach(this);
        DataContext = new MainViewModel();

        Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.RenderedKql)) KqlHighlighter.Render(KqlView, Vm.RenderedKql);
        };
        KqlHighlighter.Render(KqlView, Vm.RenderedKql);

        Loaded += (_, _) =>
        {
            if (App.Library.LoadErrors.Count > 0)
                MessageBox.Show(this,
                    "Some query files could not be read and were skipped:\n\n" + string.Join("\n", App.Library.LoadErrors),
                    "KQL Pad", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (Vm.Profiles.Count == 0) Vm.Status = "Add a client (tenant + workspace) under Clients > Manage clients to run queries. Copy KQL works without one.";
        };
    }

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>Context-menu click -> the tree node the menu was opened on.</summary>
    static object? NodeOf(object sender) =>
        ((sender as MenuItem)?.Parent as ContextMenu)?.PlacementTarget is FrameworkElement fe ? fe.DataContext : null;

    QueryItemVM? ItemFor(object sender) => NodeOf(sender) as QueryItemVM ?? Vm.SelectedItem;

    // ---- tree ------------------------------------------------------------------------------

    void QueryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        // Ignore null: the tree clears itself while rebuilding/filtering and we keep the open query.
        switch (e.NewValue)
        {
            case QueryItemVM item:
                Vm.SelectedItem = item;
                Vm.SelectedCategory = item.Parent;
                break;
            case CategoryVM cat:
                Vm.SelectedCategory = cat;
                break;
        }
    }

    // ---- query/category management ---------------------------------------------------------

    void NewQuery_Click(object sender, RoutedEventArgs e) => NewQueryIn(Vm.SelectedCategory?.Name);

    void CatNewQuery_Click(object sender, RoutedEventArgs e) => NewQueryIn((NodeOf(sender) as CategoryVM)?.Name);

    void NewQueryIn(string? category)
    {
        var dlg = new QueryEditorWindow(null, Vm.CategoryNames.ToList(), category) { Owner = this };
        if (dlg.ShowDialog() == true) Vm.SaveQuery(null, dlg.Result!, dlg.CategoryName);
    }

    void ItemEdit_Click(object sender, RoutedEventArgs e)
    {
        if (ItemFor(sender) is not { } item) return;
        var dlg = new QueryEditorWindow(item.Model, Vm.CategoryNames.ToList(), item.Parent.Name) { Owner = this };
        if (dlg.ShowDialog() == true) Vm.SaveQuery(item.Model, dlg.Result!, dlg.CategoryName);
    }

    void ItemDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (ItemFor(sender) is { } item) Vm.Duplicate(item.Model);
    }

    void ItemDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ItemFor(sender) is not { } item) return;
        var ok = MessageBox.Show(this, $"Delete \"{item.Name}\"?", "Delete query", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok == MessageBoxResult.Yes) Vm.DeleteQuery(item.Model);
    }

    void NewCategory_Click(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Ask(this, "New category", "Category name:", "");
        if (!string.IsNullOrWhiteSpace(name)) Vm.AddCategory(name);
    }

    void CatRename_Click(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is not CategoryVM cat) return;
        var name = InputDialog.Ask(this, "Rename category", "Category name:", cat.Name);
        if (!string.IsNullOrWhiteSpace(name)) Vm.RenameCategory(cat.Model, name);
    }

    void CatDelete_Click(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is not CategoryVM cat) return;
        var n = cat.Model.Queries.Count;
        var ok = MessageBox.Show(this,
            n == 0 ? $"Delete empty category \"{cat.Name}\"?" : $"Delete category \"{cat.Name}\" and its {n} queries? This removes the JSON file.",
            "Delete category", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok == MessageBoxResult.Yes) Vm.DeleteCategory(cat.Model);
    }

    // ---- menu ------------------------------------------------------------------------------

    void OpenLibraryFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.QueriesDir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.QueriesDir}\"") { UseShellExecute = true });
    }

    void RestoreStarters_Click(object sender, RoutedEventArgs e)
    {
        var added = Vm.RestoreStarters();
        Vm.Status = added == 0 ? "No missing starter queries." : $"Restored {added} starter queries.";
    }

    void Exit_Click(object sender, RoutedEventArgs e) => Close();

    void ManageClients_Click(object sender, RoutedEventArgs e)
    {
        var selectedId = Vm.SelectedProfile?.Id;
        new ProfilesWindow { Owner = this }.ShowDialog();
        var next = Vm.Profiles.FirstOrDefault(p => p.Id == selectedId) ?? Vm.Profiles.FirstOrDefault();
        // Re-selecting the same object is a no-op, so force the closed combo box to rebuild its display.
        ClientCombo.Items.Refresh();
        Vm.SelectedProfile = next;
        Vm.RefreshProfileSummary();
    }

    // ---- actions ---------------------------------------------------------------------------

    void CopyKql_Click(object sender, RoutedEventArgs e)
    {
        if (!TrySetClipboard(Vm.RenderedKql)) return;
        Vm.Status = "KQL copied to clipboard";
    }

    bool TrySetClipboard(string text)
    {
        try { Clipboard.SetText(text); return true; }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not access the clipboard: " + ex.Message, "KQL Pad");
            return false;
        }
    }

    void OpenPortal_Click(object sender, RoutedEventArgs e)
    {
        var profile = Vm.SelectedProfile;
        if (profile is null) { Vm.Status = "Select a client first."; return; }
        var url = PortalLink.Build(profile, Vm.RenderedKql);
        if (url is null)
        {
            MessageBox.Show(this, "This client has no workspace resource ID. Add it under Manage clients to enable this button.", "Open in portal");
            return;
        }
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        Vm.Status = "Opened in the Azure portal";
    }

    async void Run_Click(object sender, RoutedEventArgs e)
    {
        var profile = Vm.SelectedProfile;
        if (profile is null)
        {
            MessageBox.Show(this, "Add and select a client first (Clients > Manage clients).", "Run query");
            return;
        }

        _cts = new CancellationTokenSource();
        Vm.IsRunning = true;
        Vm.Status = $"Running against {profile.Name}... (a browser window opens if you need to sign in)";
        ShowError(null);
        try
        {
            var result = await App.Client.RunAsync(profile, Vm.RenderedKql, _cts.Token);
            ShowResult(result);
            Vm.Status = $"{result.RowCount:N0} rows in {result.Elapsed.TotalSeconds:0.0}s from {profile.Name}";
        }
        catch (OperationCanceledException)
        {
            Vm.Status = "Cancelled";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            Vm.Status = "Query failed";
        }
        finally
        {
            Vm.IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    void ShowError(string? message)
    {
        ErrorBox.Text = message ?? "";
        ErrorBox.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    void ShowResult(QueryResult result)
    {
        _result = result;
        ResultGrid.ItemsSource = null;
        ResultGrid.Columns.Clear();
        for (var i = 0; i < result.ColumnNames.Length; i++)
        {
            ResultGrid.Columns.Add(new DataGridTextColumn
            {
                Header = result.ColumnNames[i],
                Binding = new Binding($"c{i}"),
                SortMemberPath = $"c{i}",
            });
        }
        ResultGrid.ItemsSource = result.Table.DefaultView;
        ResultInfo.Text = $"// RESULTS  [{result.RowCount:N0} ROWS]";
        ExportButton.IsEnabled = true;
    }

    void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_result is null) return;
        var name = LibraryStore.Slug(Vm.SelectedItem?.Name ?? "results");
        var dlg = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"{name}-{DateTime.Now:yyyyMMdd-HHmm}.csv",
        };
        if (dlg.ShowDialog(this) != true) return;

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", _result.ColumnNames.Select(Csv)));
        foreach (System.Data.DataRow row in _result.Table.Rows)
            sb.AppendLine(string.Join(",", row.ItemArray.Select(v => Csv(v is null or DBNull ? "" : System.Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""))));
        File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
        Vm.Status = $"Exported {_result.RowCount:N0} rows to {dlg.FileName}";
    }

    static string Csv(string s) =>
        s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
