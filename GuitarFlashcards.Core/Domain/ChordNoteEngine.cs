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
                var thirdPresent = quality.RequiredThird is int third && notes.Contains((root + third) % 12);
                var isPowerChord = quality.Suffix == "5" && notes.Count == 2 && notes.Contains((root + 7) % 12);
                if (!complete && !thirdPresent && !isPowerChord)
                {
                    continue;
                }

                var missing = tones.Count - notes.Count;
                var bassMatchesRoot = bassPitchClass is int bass && ((bass % 12) + 12) % 12 == root;
                candidates.Add(new Candidate(root, quality, complete, notes.Contains(root), bassMatchesRoot, missing));
            }
        }

        var ordered = candidates
            .OrderByDescending(candidate => candidate.Complete)
            .ThenByDescending(candidate => candidate.RootPresent)
            .ThenByDescending(candidate => candidate.BassMatchesRoot)
            .ThenBy(candidate => candidate.MissingTones)
            .ThenBy(candidate => candidate.Quality.Preference)
            .ThenBy(candidate => candidate.Root)
            .ToArray();
        if (ordered.Length == 0)
        {
            return new ChordIdentificationResult(null, []);
        }

        var best = ordered[0];
        var sameRank = ordered.Where(candidate => candidate.Complete == best.Complete
                && candidate.BassMatchesRoot == best.BassMatchesRoot
                && candidate.RootPresent == best.RootPresent
                && candidate.MissingTones == best.MissingTones
                && candidate.Quality.Preference == best.Quality.Preference)
            .ToArray();
        var names = sameRank.Select(candidate => FormatName(candidate, bassPitchClass)).Distinct().ToArray();
        return names.Length > 1
            ? new ChordIdentificationResult(null, names)
            : new ChordIdentificationResult(names[0], names);
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

    private sealed record Quality(string Suffix, int[] Intervals, int? RequiredThird, int Preference);
    private sealed record Candidate(int Root, Quality Quality, bool Complete, bool RootPresent, bool BassMatchesRoot, int MissingTones);

    private static readonly Quality[] Qualities =
    [
        new("", [0, 4, 7], 4, 0),
        new("m", [0, 3, 7], 3, 0),
        new("dim", [0, 3, 6], null, 1),
        new("aug", [0, 4, 8], null, 1),
        new("sus2", [0, 2, 7], null, 1),
        new("sus4", [0, 5, 7], null, 1),
        new("5", [0, 7], null, 0),
        new("7", [0, 4, 7, 10], 4, 2),
        new("maj7", [0, 4, 7, 11], 4, 2),
        new("m7", [0, 3, 7, 10], 3, 2),
        new("m7(add11)", [0, 3, 5, 7, 10], 3, 4),
        new("m7b5", [0, 3, 6, 10], 3, 3),
        new("dim7", [0, 3, 6, 9], null, 3),
        new("6", [0, 4, 7, 9], 4, 3),
        new("m6", [0, 3, 7, 9], 3, 3),
        new("add9", [0, 2, 4, 7], 4, 3),
        new("m(add9)", [0, 2, 3, 7], 3, 3),
        new("7sus2", [0, 2, 7, 10], null, 3),
        new("7sus4", [0, 5, 7, 10], null, 3),
        new("9", [0, 2, 4, 7, 10], 4, 4),
        new("maj9", [0, 2, 4, 7, 11], 4, 4),
        new("m9", [0, 2, 3, 7, 10], 3, 4)
    ];
}