namespace KqlPad.Infrastructure;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KqlPad");

    public static string QueriesDir => Path.Combine(Root, "queries");
    public static string AuthDir => Path.Combine(Root, "auth");
    public static string ProfilesFile => Path.Combine(Root, "profiles.json");
    public static string SeededMarker => Path.Combine(Root, ".seeded");
}
