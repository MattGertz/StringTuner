using System.Text.Json.Serialization;

namespace GuitarFlashcards.Core.Domain;

public enum StringNoteState
{
    Open,
    Muted,
    Fretted
}

public sealed record StringFingering([property: JsonRequired] StringNoteState State, int? Fret = null, int? Finger = null)
{
    public static StringFingering Open { get; } = new(StringNoteState.Open);

    public static StringFingering Muted { get; } = new(StringNoteState.Muted);

    public static StringFingering FrettedAt(int fret, int finger) => new(StringNoteState.Fretted, fret, finger);

    public void Validate()
    {
        var valid = State switch
        {
            StringNoteState.Open or StringNoteState.Muted => Fret is null && Finger is null,
            StringNoteState.Fretted => Fret is > 0 && Finger is >= 1 and <= 4,
            _ => false
        };

        if (!valid)
        {
            throw new ArgumentException("A string must be open, muted, or fretted with a positive fret and finger 1-4.");
        }
    }
}

public sealed record GuitarFingering([property: JsonRequired] IReadOnlyList<StringFingering> Strings)
{
    public static GuitarFingering Open(int stringCount = 6) =>
        new(Enumerable.Repeat(StringFingering.Open, stringCount).ToArray());

    public void Validate(int expectedStringCount = 6)
    {
        ArgumentNullException.ThrowIfNull(Strings);
        if (Strings.Count != expectedStringCount)
        {
            throw new ArgumentException($"The fingering must contain exactly {expectedStringCount} strings.");
        }

        foreach (var fingering in Strings)
        {
            fingering.Validate();
        }
    }
}

public sealed record InstrumentStringSpec(string Label, int OpenMidiNote);

public sealed record InstrumentTuning(string InstrumentName, string TuningName, IReadOnlyList<InstrumentStringSpec> Strings)
{
    public static InstrumentTuning StandardGuitar { get; } = new(
        "Guitar",
        "Standard",
        [new("E", 40), new("A", 45), new("D", 50), new("G", 55), new("B", 59), new("E", 64)]);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(InstrumentName) || string.IsNullOrWhiteSpace(TuningName) || Strings.Count == 0)
        {
            throw new ArgumentException("A tuning must have names and at least one string.");
        }

        if (Strings.Any(item => string.IsNullOrWhiteSpace(item.Label) || item.OpenMidiNote is < 0 or > 127))
        {
            throw new ArgumentException("Every string must have a label and a MIDI note from 0 to 127.");
        }
    }
}

public sealed record ChordDefinition(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired] string DisplayName,
    [property: JsonRequired] GuitarFingering Fingering,
    [property: JsonRequired] string? DerivedChordName,
    [property: JsonRequired] bool Enabled = true);

public sealed record DerivedNote(int StringIndex, int MidiNote, int PitchClass, string Name, double Frequency);

public sealed record ChordIdentificationResult(string? Name, IReadOnlyList<string> Candidates)
{
    public bool IsIdentified => Name is not null;

    public bool IsAmbiguous => Candidates.Count > 1;

    public IReadOnlyList<string> Alternatives => Name is null
        ? Candidates
        : Candidates.Where(candidate => !string.Equals(candidate, Name, StringComparison.Ordinal)).ToArray();

    public string DisplayText => Name is null
        ? "Chord not identified"
        : Alternatives.Count == 0
            ? $"Identified as {Name}"
            : $"Identified as {Name}; also known as {string.Join(", ", Alternatives)}";
}