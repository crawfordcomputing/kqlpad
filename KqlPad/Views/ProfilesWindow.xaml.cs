using System.Windows;
using KqlPad.Models;

namespace KqlPad.Views;

public partial class ProfilesWindow : Window
{
    public System.Collections.ObjectModel.ObservableCollection<ClientProfile> Profiles => App.Profiles.Profiles;

    public ProfilesWindow()
    {
        InitializeComponent();
        Infrastructure.ConsoleChrome.Attach(this);
        DataContext = this;
        List.SelectedItem = Profiles.FirstOrDefault();

        // Profiles save themselves on every change; surface a failed write here.
        Action<string?> onSaved = err => { if (err is not null) Show(err, error: true); };
        App.Profiles.Saved += onSaved;
        Closed += (_, _) => App.Profiles.Saved -= onSaved;

        Closing += (_, e) =>
        {
            if (NameProblem() is { } problem)
            {
                Show(problem.Message, error: true);
                List.SelectedItem = problem.Profile;
                e.Cancel = true;
                return;
            }
            if (!App.Profiles.Save())
            {
                var ok = MessageBox.Show(this, App.Profiles.LastSaveError + "\n\nClose anyway and lose unsaved client changes?",
                    "Clients", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (ok != MessageBoxResult.Yes) e.Cancel = true;
            }
        };
    }

    /// <summary>First client with a blank name or a name already used by another client.</summary>
    (ClientProfile Profile, string Message)? NameProblem()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Profiles)
        {
            var name = p.Name.Trim();
            if (name.Length == 0) return (p, "Every client needs a name.");
            if (!seen.Add(name)) return (p, $"Two clients are named \"{name}\". Give each client a different name.");
        }
        return null;
    }

    string UniqueName(string baseName)
    {
        var name = baseName;
        for (var i = 2; Profiles.Any(p => string.Equals(p.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)); i++)
            name = $"{baseName} {i}";
        return name;
    }

    ClientProfile? Current => List.SelectedItem as ClientProfile;

    void Add_Click(object sender, RoutedEventArgs e)
    {
        var p = new ClientProfile { Name = UniqueName("New client") };
        Profiles.Add(p);
        List.SelectedItem = p;
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } p) return;
        var ok = MessageBox.Show(this, $"Remove client \"{p.Name}\"? Saved queries are not affected.", "Remove client",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;
        App.Client.ForgetSignIn(p);
        Profiles.Remove(p);
        List.SelectedItem = Profiles.FirstOrDefault();
    }

    void Forget_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } p) return;
        App.Client.ForgetSignIn(p);
        Show("Saved sign-in forgotten. The next run will ask you to sign in again.", error: false);
    }

    async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } p) return;
        Show("Signing in and running a test query... (a browser window opens if you need to sign in)", error: false);
        try
        {
            var result = await App.Client.RunAsync(p, "print Workspace = 'ok', Now = now()", CancellationToken.None);
            Show($"Success: signed in and the workspace answered ({result.Elapsed.TotalSeconds:0.0}s).", error: false);
        }
        catch (Exception ex)
        {
            Show(ex.Message, error: true);
        }
    }

    void Show(string text, bool error)
    {
        TestResult.Text = text;
        TestResult.Foreground = (System.Windows.Media.Brush)FindResource(error ? "RedBrush" : "GreenBrush");
        TestResult.Visibility = Visibility.Visible;
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
