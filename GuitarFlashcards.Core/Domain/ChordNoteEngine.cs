namespace GuitarFlashcards.Core.Domain;

public static class ChordNoteEngine
{
    private static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    public static IReadOnlyList<DerivedNote> DeriveNotes(GuitarFingering fingering, InstrumentTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(fingering);
        ArgumentNullException.ThrowIfNull(tuning);
        tuning.Validate();
        fingering.Validate(tuning.Strings.Count);

        return fingering.Strings
            .Select((position, index) => (position, index))
            .Where(item => item.position.State != StringNoteState.Muted)
            .Select(item =>
            {
                var midiNote = tuning.Strings[item.index].OpenMidiNote + (item.position.Fret ?? 0);
                var pitchClass = midiNote % 12;
                return new DerivedNote(item.index, midiNote, pitchClass, NoteNames[pitchClass], 440 * Math.Pow(2, (midiNote - 69) / 12d));
            })
            .ToArray();
    }

    public static ChordIdentificationResult Identify(IReadOnlyCollection<int> pitchClasses, int? bassPitchClass = null)
    {
        ArgumentNullException.ThrowIfNull(pitchClasses);
        var notes = pitchClasses.Select(note => ((note % 12) + 12) % 12).Distinct().ToHashSet();
        if (notes.Count < 2)
        {
            return new ChordIdentificationResult(null, []);
        }

        var candidates = new List<Candidate>();
        for (var root = 0; root < 12; root++)
        {
            foreach (var quality in Qualities)
            {
                var tones = quality.Intervals.Select(interval => (root + interval) % 12).ToHashSet();
                if (!notes.IsSubsetOf(tones))
                {
                    continue;
                }

                var complete = notes.SetEquals(tones);
                var evidenceCount = quality.DefiningIntervals.Count(interval => notes.Contains((root + interval) % 12));
                if (!complete && (notes.Count < quality.MinimumVoicingNotes || evidenceCount < quality.MinimumEvidence))
                {
                    continue;
                }

                var missing = tones.Count - notes.Count;
                var bassMatchesRoot = bassPitchClass is int bass && ((bass % 12) + 12) % 12 == root;
                candidates.Add(new Candidate(root, quality, complete, notes.Contains(root), bassMatchesRoot, evidenceCount, missing));
            }
        }

        var ordered = candidates
            .OrderByDescending(candidate => candidate.BassMatchesRoot)
            .ThenByDescending(candidate => candidate.RootPresent)
            .ThenByDescending(candidate => candidate.EvidenceCount)
            .ThenBy(candidate => candidate.MissingTones)
            .ThenBy(candidate => candidate.Quality.Complexity)
            .ThenByDescending(candidate => candidate.Complete)
            .ThenBy(candidate => FormatName(candidate, bassPitchClass).Length)
            .ThenBy(candidate => FormatName(candidate, bassPitchClass), StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0)
        {
            return new ChordIdentificationResult(null, []);
        }

        var best = ordered[0];
        var sameRank = ordered.Where(candidate => candidate.Complete == best.Complete
                && candidate.BassMatchesRoot == best.BassMatchesRoot
                && candidate.RootPresent == best.RootPresent
                && candidate.EvidenceCount == best.EvidenceCount
                && candidate.MissingTones == best.MissingTones
                && candidate.Quality.Complexity == best.Quality.Complexity)
            .Select(candidate => FormatName(candidate, bassPitchClass))
            .Distinct()
            .OrderBy(name => name.Length)
            .ThenBy(name => name, StringComparer.Ordinal)
            .ToArray();
        return new ChordIdentificationResult(sameRank[0], sameRank);
    }

    public static ChordIdentificationResult IdentifyFingering(GuitarFingering fingering, InstrumentTuning? tuning = null)
    {
        var notes = DeriveNotes(fingering, tuning ?? InstrumentTuning.StandardGuitar);
        var bass = notes.MinBy(note => note.MidiNote)?.PitchClass;
        return Identify(notes.Select(note => note.PitchClass).ToArray(), bass);
    }

    private static string FormatName(Candidate candidate, int? bassPitchClass)
    {
        var name = NoteNames[candidate.Root] + candidate.Quality.Suffix;
        if (bassPitchClass is int bass && ((bass % 12) + 12) % 12 != candidate.Root)
        {
            name += "/" + NoteNames[((bass % 12) + 12) % 12];
        }

        return name;
    }

    private sealed record Quality(string Suffix, int[] Intervals, int[] DefiningIntervals, int MinimumEvidence, int MinimumVoicingNotes, int Complexity);
    private sealed record Candidate(int Root, Quality Quality, bool Complete, bool RootPresent, bool BassMatchesRoot, int EvidenceCount, int MissingTones);

    private static readonly Quality[] Qualities =
    [
        new("", [0, 4, 7], [4], 1, 2, 1),
        new("m", [0, 3, 7], [3], 1, 2, 1),
        new("dim", [0, 3, 6], [3, 6], 2, 3, 2),
        new("aug", [0, 4, 8], [4, 8], 2, 3, 2),
        new("sus2", [0, 2, 7], [2], 1, 2, 1),
        new("sus4", [0, 5, 7], [5], 1, 2, 1),
        new("5", [0, 7], [7], 1, 2, 0),
        new("7", [0, 4, 7, 10], [4, 10], 2, 3, 2),
        new("maj7", [0, 4, 7, 11], [4, 11], 2, 3, 2),
        new("m7", [0, 3, 7, 10], [3, 10], 2, 3, 2),
        new("m7(add11)", [0, 3, 5, 7, 10], [3, 10, 5], 3, 3, 3),
        new("ø7", [0, 3, 6, 10], [3, 6, 10], 3, 3, 2),
        new("°7", [0, 3, 6, 9], [3, 6, 9], 3, 3, 2),
        new("6", [0, 4, 7, 9], [4, 9], 2, 3, 2),
        new("m6", [0, 3, 7, 9], [3, 9], 2, 3, 2),
        new("add9", [0, 2, 4, 7], [4, 2], 2, 3, 2),
        new("m(add9)", [0, 2, 3, 7], [3, 2], 2, 3, 2),
        new("7sus2", [0, 2, 7, 10], [2, 10], 2, 3, 2),
        new("7sus4", [0, 5, 7, 10], [5, 10], 2, 3, 2),
        new("9", [0, 2, 4, 7, 10], [4, 10, 2], 3, 3, 3),
        new("m9", [0, 2, 3, 7, 10], [3, 10, 2], 3, 3, 3),
        new("maj9", [0, 2, 4, 7, 11], [4, 11, 2], 3, 3, 3),
        new("11", [0, 2, 4, 5, 7, 10], [10, 5], 2, 3, 4),
        new("m11", [0, 2, 3, 5, 7, 10], [3, 10, 5], 3, 3, 4),
        new("maj11", [0, 2, 4, 5, 7, 11], [11, 5], 2, 3, 4),
        new("13", [0, 2, 4, 5, 7, 9, 10], [4, 10, 9], 3, 3, 5),
        new("m13", [0, 2, 3, 5, 7, 9, 10], [3, 10, 9], 3, 3, 5),
        new("maj13", [0, 2, 4, 5, 7, 9, 11], [4, 11, 9], 3, 3, 5)
    ];
}