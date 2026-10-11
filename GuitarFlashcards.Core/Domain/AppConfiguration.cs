using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuitarFlashcards.Core.Domain;

public sealed record PracticeSettings(
    [property: JsonRequired] double MinimumDurationSeconds = 2,
    [property: JsonRequired] double MaximumDurationSeconds = 4,
    [property: JsonRequired] bool ShowFingering = true,
    [property: JsonRequired] bool PlayChordDuringRun = true)
{
    public double StrumIntervalSeconds { get; init; } = 1.0;
}

public sealed record AppConfiguration(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] IReadOnlyList<ChordDefinition> Chords,
    [property: JsonRequired] PracticeSettings Settings)
{
    public static AppConfiguration Default { get; } = new(1, [], new PracticeSettings());
}

public static class ConfigurationJson
{
    private static readonly JsonSerializerOptions Options = new()
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