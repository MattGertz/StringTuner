namespace GuitarFlashcards.Core.Domain;

public sealed record BarreSegment(int Fret, int Finger, int StartStringIndex, int EndStringIndex);

public static class BarreInference
{
    public static IReadOnlyList<BarreSegment> Infer(GuitarFingering fingering)
    {
        ArgumentNullException.ThrowIfNull(fingering);
        fingering.Validate();

        return fingering.Strings
            .Select((position, stringIndex) => (position, stringIndex))
            .Where(item => item.position.State == StringNoteState.Fretted
                && item.position.Fret is not null
                && item.position.Finger is not null)
            .GroupBy(item => (Fret: item.position.Fret!.Value, Finger: item.position.Finger!.Value))
            .Select(group =>
        {
            var stringIndices = group.Select(item => item.stringIndex).ToArray();
            return (group.Key.Fret, group.Key.Finger, Start: stringIndices[0], End: stringIndices[^1]);
        })
            .Where(segment => segment.End > segment.Start)
            .OrderBy(segment => segment.Fret)
            .ThenBy(segment => segment.Start)
            .Select(segment => new BarreSegment(segment.Fret, segment.Finger, segment.Start, segment.End))
            .ToArray();
    }
}