namespace GuitarFlashcards.Core.Domain;

public static class FretboardScrollPosition
{
    public const int VisibleFretCount = 5;

    public static int FirstVisibleFret(GuitarFingering fingering)
    {
        ArgumentNullException.ThrowIfNull(fingering);
        fingering.Validate();
        var frets = fingering.Strings
            .Where(item => item.State == StringNoteState.Fretted && item.Fret.HasValue)
            .Select(item => item.Fret!.Value)
            .ToArray();
        if (frets.Length == 0)
        {
            return 1;
        }

        var lowestFret = frets.Min();
        var highestFret = frets.Max();
        if (highestFret <= VisibleFretCount)
        {
            return 1;
        }

        return highestFret - lowestFret < VisibleFretCount
            ? lowestFret
            : highestFret - VisibleFretCount + 1;
    }
}