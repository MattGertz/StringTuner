using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuitarFlashcards.Core.Domain;

public enum PracticeMode
{
    Timed,
    Manual
}

public sealed record PracticeSettings(
    [property: JsonRequired] double MinimumDurationSeconds = 2,
    [property: JsonRequired] double MaximumDurationSeconds = 4,
    [property: JsonRequired] bool ShowFingering = true,
    [property: JsonRequired] bool PlayChordDuringRun = true)
{
    public double StrumIntervalSeconds { get; init; } = 1.0;

    public bool ShowNextChord { get; init; } = true;

    public PracticeMode Mode { get; init; } = PracticeMode.Timed;
}

public sealed record AppConfiguration(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] IReadOnlyList<ChordDefinition> Chords,
    [property: JsonRequired] PracticeSettings Settings)
{
    public static AppConfiguration Default { get; } = new(1, [], new PracticeSettings());
}

public sealed record PracticeSettingsDocument(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] PracticeSettings Settings);

public sealed record ChordLibraryDocument(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] IReadOnlyList<ChordDefinition> Chords);

public static class ConfigurationJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string Serialize(AppConfiguration configuration)
    {
        Validate(configuration);
        return JsonSerializer.Serialize(configuration, Options);
    }

    public static AppConfiguration Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var configuration = JsonSerializer.Deserialize<AppConfiguration>(json, Options)
            ?? throw new JsonException("The configuration document is empty.");
        Validate(configuration);
        return configuration;
    }

    public static void Validate(AppConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.SchemaVersion != 1)
        {
            throw new ArgumentException($"Unsupported configuration schema version: {configuration.SchemaVersion}.");
        }

        ArgumentNullException.ThrowIfNull(configuration.Chords);
        ArgumentNullException.ThrowIfNull(configuration.Settings);
        if (!double.IsFinite(configuration.Settings.MinimumDurationSeconds)
            || !double.IsFinite(configuration.Settings.MaximumDurationSeconds)
            || configuration.Settings.MinimumDurationSeconds <= 0
            || configuration.Settings.MaximumDurationSeconds <= configuration.Settings.MinimumDurationSeconds)
        {
            throw new ArgumentException("Minimum duration must be greater than zero and maximum duration must be greater than minimum duration.");
        }

        if (!double.IsFinite(configuration.Settings.StrumIntervalSeconds)
            || configuration.Settings.StrumIntervalSeconds is < 0.25 or > 4.0)
        {
            throw new ArgumentException("Strum interval must be between 0.25 and 4.0 seconds.");
        }

        if (!Enum.IsDefined(configuration.Settings.Mode))
        {
            throw new ArgumentException("Practice mode is invalid.");
        }

        var ids = new HashSet<Guid>();
        foreach (var chord in configuration.Chords)
        {
            if (chord is null || chord.Id == Guid.Empty || string.IsNullOrWhiteSpace(chord.DisplayName))
            {
                throw new ArgumentException("Every chord must have an identifier and display name.");
            }

            if (!ids.Add(chord.Id))
            {
                throw new ArgumentException($"Duplicate chord identifier: {chord.Id}.");
            }

            ArgumentNullException.ThrowIfNull(chord.Fingering);
            chord.Fingering.Validate();
            var notes = ChordNoteEngine.DeriveNotes(chord.Fingering, InstrumentTuning.StandardGuitar);
            var derivedName = ChordNoteEngine.Identify(notes.Select(note => note.PitchClass).ToArray(), notes.MinBy(note => note.MidiNote)?.PitchClass).Name;
            if (!string.Equals(chord.DerivedChordName, derivedName, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Derived chord metadata is inconsistent for '{chord.DisplayName}'.");
            }
        }
    }
}

public static class PracticeSettingsJson
{
    public static string Serialize(PracticeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ConfigurationJson.Validate(AppConfiguration.Default with { Settings = settings });
        return JsonSerializer.Serialize(new PracticeSettingsDocument(1, settings), ConfigurationJson.Options);
    }

    public static PracticeSettings Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var document = JsonSerializer.Deserialize<PracticeSettingsDocument>(json, ConfigurationJson.Options)
            ?? throw new JsonException("The practice settings document is empty.");
        if (document.SchemaVersion != 1)
        {
            throw new ArgumentException($"Unsupported practice settings schema version: {document.SchemaVersion}.");
        }

        ConfigurationJson.Validate(AppConfiguration.Default with { Settings = document.Settings });
        return document.Settings;
    }
}

public static class ChordLibraryJson
{
    public static string Serialize(IReadOnlyList<ChordDefinition> chords)
    {
        ArgumentNullException.ThrowIfNull(chords);
        ConfigurationJson.Validate(AppConfiguration.Default with { Chords = chords });
        return JsonSerializer.Serialize(new ChordLibraryDocument(1, chords), ConfigurationJson.Options);
    }

    public static IReadOnlyList<ChordDefinition> Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var document = JsonSerializer.Deserialize<ChordLibraryDocument>(json, ConfigurationJson.Options)
            ?? throw new JsonException("The chord library document is empty.");
        if (document.SchemaVersion != 1)
        {
            throw new ArgumentException($"Unsupported chord library schema version: {document.SchemaVersion}.");
        }

        ConfigurationJson.Validate(AppConfiguration.Default with { Chords = document.Chords });
        return document.Chords;
    }
}