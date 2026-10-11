namespace GuitarFlashcards.Core.Domain;

public sealed class ChordLibrary
{
    private readonly List<ChordDefinition> _chords;

    public ChordLibrary(IEnumerable<ChordDefinition>? chords = null)
    {
        _chords = chords?.ToList() ?? [];
    }

    public IReadOnlyList<ChordDefinition> Chords => _chords
        .OrderBy(chord => chord.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(chord => chord.DisplayName, StringComparer.Ordinal)
        .ThenBy(chord => chord.Id)
        .ToArray();

    public IReadOnlyList<ChordDefinition> EnabledChords => Chords.Where(chord => chord.Enabled).ToArray();

    public ChordDefinition Add(string displayName, GuitarFingering fingering)
    {
        ValidateName(displayName);
        fingering.Validate();
        var chord = new ChordDefinition(Guid.NewGuid(), displayName.Trim(), fingering, Identify(fingering), true);
        _chords.Add(chord);
        return chord;
    }

    public ChordDefinition Update(Guid id, string displayName, GuitarFingering fingering)
    {
        ValidateName(displayName);
        fingering.Validate();
        var index = FindIndex(id);
        var current = _chords[index];
        var updated = current with
        {
            DisplayName = displayName.Trim(),
            Fingering = fingering,
            DerivedChordName = Identify(fingering)
        };
        _chords[index] = updated;
        return updated;
    }

    public bool SetEnabled(Guid id, bool enabled)
    {
        var index = FindIndex(id);
        if (_chords[index].Enabled == enabled)
        {
            return false;
        }

        _chords[index] = _chords[index] with { Enabled = enabled };
        return true;
    }

    public bool Delete(Guid id)
    {
        var index = FindIndex(id);
        _chords.RemoveAt(index);
        return true;
    }

    private int FindIndex(Guid id)
    {
        var index = _chords.FindIndex(chord => chord.Id == id);
        return index >= 0 ? index : throw new KeyNotFoundException($"Chord '{id}' was not found.");
    }

    private static string? Identify(GuitarFingering fingering)
    {
        var notes = ChordNoteEngine.DeriveNotes(fingering, InstrumentTuning.StandardGuitar);
        return ChordNoteEngine.Identify(notes.Select(note => note.PitchClass).ToArray(), notes.MinBy(note => note.MidiNote)?.PitchClass).Name;
    }

    private static void ValidateName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("A display name is required.", nameof(displayName));
        }
    }
}