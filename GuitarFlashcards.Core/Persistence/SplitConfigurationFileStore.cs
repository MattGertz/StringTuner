using System.Text;
using GuitarFlashcards.Core.Domain;

namespace GuitarFlashcards.Core.Persistence;

public sealed class SplitConfigurationFileStore(string settingsPath, string chordLibraryPath)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var settings = File.Exists(settingsPath)
                ? PracticeSettingsJson.Deserialize(await File.ReadAllTextAsync(settingsPath, cancellationToken))
                : new PracticeSettings();
            IReadOnlyList<ChordDefinition> chords = File.Exists(chordLibraryPath)
                ? ChordLibraryJson.Deserialize(await File.ReadAllTextAsync(chordLibraryPath, cancellationToken))
                : Array.Empty<ChordDefinition>();
            var configuration = new AppConfiguration(1, chords, settings);
            ConfigurationJson.Validate(configuration);
            return configuration;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ConfigurationJson.Validate(configuration);
        var settingsJson = PracticeSettingsJson.Serialize(configuration.Settings);
        var chordsJson = ChordLibraryJson.Serialize(configuration.Chords);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await WriteAtomicAsync(settingsPath, settingsJson, cancellationToken);
            await WriteAtomicAsync(chordLibraryPath, chordsJson, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveSettingsAsync(PracticeSettings settings, CancellationToken cancellationToken = default)
    {
        var json = PracticeSettingsJson.Serialize(settings);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await WriteAtomicAsync(settingsPath, json, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveChordsAsync(IReadOnlyList<ChordDefinition> chords, CancellationToken cancellationToken = default)
    {
        var json = ChordLibraryJson.Serialize(chords);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await WriteAtomicAsync(chordLibraryPath, json, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PracticeSettings> ImportSettingsAsync(string json, CancellationToken cancellationToken = default)
    {
        var settings = PracticeSettingsJson.Deserialize(json);
        await SaveSettingsAsync(settings, cancellationToken);
        return settings;
    }

    public async Task<IReadOnlyList<ChordDefinition>> ImportChordsAsync(string json, CancellationToken cancellationToken = default)
    {
        var chords = ChordLibraryJson.Deserialize(json);
        await SaveChordsAsync(chords, cancellationToken);
        return chords;
    }

    private static async Task WriteAtomicAsync(string path, string contents, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, contents, new UTF8Encoding(false), cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
