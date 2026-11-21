using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using KqlPad.Infrastructure;
using KqlPad.Models;

namespace KqlPad.Services;

public sealed class ProfileStore
{
    public ObservableCollection<ClientProfile> Profiles { get; } = new();
    public string? LastProfileId { get; set; }

    /// <summary>Set when the last save failed, cleared when one succeeds.</summary>
    public string? LastSaveError { get; private set; }

    /// <summary>Raised after every save attempt, so windows can show a failure.</summary>
    public event Action<string?>? Saved;

    bool _loading;

    public ProfileStore()
    {
        // Save as soon as clients are added, removed or edited, so nothing is lost on a crash.
        Profiles.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null) foreach (ClientProfile p in e.OldItems) p.PropertyChanged -= OnProfileChanged;
            if (e.NewItems is not null) foreach (ClientProfile p in e.NewItems) p.PropertyChanged += OnProfileChanged;
            if (!_loading) Save();
        };
    }

    void OnProfileChanged(object? sender, PropertyChangedEventArgs e) => Save();

    public void Load()
    {
        _loading = true;
        try
        {
            foreach (var p in Profiles) p.PropertyChanged -= OnProfileChanged;
            Profiles.Clear();
            if (!File.Exists(AppPaths.ProfilesFile)) return;
            try
            {
                var file = JsonSerializer.Deserialize<ProfilesFile>(File.ReadAllText(AppPaths.ProfilesFile), AppJson.Options);
                if (file is null) return;
                LastProfileId = file.LastProfileId;
                foreach (var p in file.Profiles) Profiles.Add(p);
            }
            catch
            {
                // Corrupt file: keep it for inspection instead of silently overwriting on next save.
                try { File.Copy(AppPaths.ProfilesFile, AppPaths.ProfilesFile + ".bad", overwrite: true); } catch { }
            }
        }
        finally { _loading = false; }
    }

    /// <summary>Writes profiles.json. Returns false (and sets <see cref="LastSaveError"/>) instead of throwing.</summary>
    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            var file = new ProfilesFile { LastProfileId = LastProfileId, Profiles = Profiles.ToList() };
            var json = JsonSerializer.Serialize(file, AppJson.Options);

            // Write to a temp file and swap it in, so a failure mid-write never truncates the existing file.
            var tmp = AppPaths.ProfilesFile + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            File.Move(tmp, AppPaths.ProfilesFile, overwrite: true);
            LastSaveError = null;
        }
        catch (Exception ex)
        {
            LastSaveError = "Could not save clients: " + ex.Message;
        }
        Saved?.Invoke(LastSaveError);
        return LastSaveError is null;
    }
}
