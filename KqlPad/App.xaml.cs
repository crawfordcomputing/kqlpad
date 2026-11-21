using System.Windows;
using KqlPad.Services;

namespace KqlPad;

public partial class App : Application
{
    public static LibraryStore Library { get; } = new();
    public static ProfileStore Profiles { get; } = new();
    public static LogAnalyticsClient Client { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "KQL Pad", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        Library.Load();
        Profiles.Load();
        base.OnStartup(e);
    }
}
