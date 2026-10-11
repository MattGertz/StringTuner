namespace GuitarFlashcards.Core.Domain;

public static class FingeringEditor
{
    public static GuitarFingering SetFrettedPosition(GuitarFingering fingering, int stringIndex, int fret, int finger)
    {
        ArgumentNullException.ThrowIfNull(fingering);
        fingering.Validate();
        if (stringIndex < 0 || stringIndex >= fingering.Strings.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(stringIndex));
        }

        var current = fingering.Strings[stringIndex];
        var replacement = current.State == StringNoteState.Fretted && current.Fret == fret
            ? StringFingering.Open
            : StringFingering.FrettedAt(fret, finger);
        return Replace(fingering, stringIndex, replacement);
    }

    public static GuitarFingering SetStringState(GuitarFingering fingering, int stringIndex, StringNoteState state)
    {
        if (state is not (StringNoteState.Open or StringNoteState.Muted))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        return Replace(fingering, stringIndex, state == StringNoteState.Open ? StringFingering.Open : StringFingering.Muted);
    }

    private static GuitarFingering Replace(GuitarFingering fingering, int stringIndex, StringFingering replacement)
    {
        if (stringIndex < 0 || stringIndex >= fingering.Strings.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(stringIndex));
        }

        replacement.Validate();
        var strings = fingering.Strings.ToArray();
        strings[stringIndex] = replacement;
        return new GuitarFingering(strings);
    }
}