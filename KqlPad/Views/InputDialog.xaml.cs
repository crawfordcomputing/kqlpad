using System.Windows;

namespace KqlPad.Views;

public partial class InputDialog : Window
{
    InputDialog(string title, string prompt, string initial)
    {
        InitializeComponent();
        Infrastructure.ConsoleChrome.Attach(this);
        Title = title.ToUpperInvariant();
        PromptText.Text = prompt.ToUpperInvariant().TrimEnd(':');
        Input.Text = initial;
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    public static string? Ask(Window owner, string title, string prompt, string initial)
    {
        var dlg = new InputDialog(title, prompt, initial) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.Input.Text.Trim() : null;
    }
}
