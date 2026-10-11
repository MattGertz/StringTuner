using GuitarFlashcards.Core.Domain;

namespace GuitarFlashcards.Core.Tests;

public sealed class ChordNoteEngineTests
{
    [Fact]
    public void EditorSupportsOpenMutedFrettedReplacementAndClear()
    {
        var fingering = GuitarFingering.Open();
        fingering = FingeringEditor.SetFrettedPosition(fingering, 0, 3, 3);
        Assert.Equal(StringFingering.FrettedAt(3, 3), fingering.Strings[0]);
        fingering = FingeringEditor.SetFrettedPosition(fingering, 0, 5, 2);
        Assert.Equal(StringFingering.FrettedAt(5, 2), fingering.Strings[0]);
        fingering = FingeringEditor.SetFrettedPosition(fingering, 0, 5, 2);
        Assert.Equal(StringFingering.Open, fingering.Strings[0]);
        fingering = FingeringEditor.SetFrettedPosition(fingering, 0, 2, 1);
        fingering = FingeringEditor.SetStringState(fingering, 0, StringNoteState.Muted);
        Assert.Equal(StringFingering.Muted, fingering.Strings[0]);
    }

    [Fact]
    public void DerivesOpenAndFrettedNotesInStandardGuitarTuningAndSkipsMutedStrings()
    {
        var fingering = GuitarFingering.Open();
        fingering = FingeringEditor.SetFrettedPosition(fingering, 1, 2, 2);
        fingering = FingeringEditor.SetStringState(fingering, 2, StringNoteState.Muted);

        var notes = ChordNoteEngine.DeriveNotes(fingering, InstrumentTuning.StandardGuitar);

        Assert.Equal([40, 47, 55, 59, 64], notes.Select(note => note.MidiNote));
        Assert.Equal(82.4069, notes[0].Frequency, precision: 3);
    }

    [Theory]
    [InlineData(new[] { 0, 4, 7 }, "C")]
    [InlineData(new[] { 0, 3, 7 }, "Cm")]
    [InlineData(new[] { 0, 4 }, "C")]
    [InlineData(new[] { 0, 7 }, "C5")]
    public void IdentifiesChordsFromPitchClasses(int[] notes, string expected)
    {
        var result = ChordNoteEngine.Identify(notes, notes[0]);

        Assert.Equal(expected, result.Name);
    }

    [Fact]
    public void IdentifiesInversionUsingBassNote()
    {
        var result = ChordNoteEngine.Identify([0, 4, 7], bassPitchClass: 4);

        Assert.Equal("C/E", result.Name);
    }

    [Fact]
    public void IdentifiesSupportedRootlessVoicingFromRemainingChordTones()
    {
        var result = ChordNoteEngine.Identify([4, 7], bassPitchClass: 4);

        Assert.Equal("Em", result.Name);
    }

    [Theory]
    [InlineData("C", new[] { 0, 3, 2, 0, 1, 0 }, new[] { 0 })]
    [InlineData("G", new[] { 3, 2, 0, 0, 0, 3 }, new int[] { })]
    [InlineData("D", new[] { 0, 0, 0, 2, 3, 2 }, new[] { 0, 1 })]
    [InlineData("E", new[] { 0, 2, 2, 1, 0, 0 }, new int[] { })]
    [InlineData("Em", new[] { 0, 2, 2, 0, 0, 0 }, new int[] { })]
    public void IdentifiesCommonOpenChordFingeringsFromTheirNotes(string expected, int[] frets, int[] mutedStrings)
    {
        var fingering = GuitarFingering.Open();
        foreach (var stringIndex in mutedStrings)
        {
            fingering = FingeringEditor.SetStringState(fingering, stringIndex, StringNoteState.Muted);
        }

        for (var stringIndex = 0; stringIndex < frets.Length; stringIndex++)
        {
            if (frets[stringIndex] > 0)
            {
                fingering = FingeringEditor.SetFrettedPosition(fingering, stringIndex, frets[stringIndex], 1);
            }
        }

        var notes = ChordNoteEngine.DeriveNotes(fingering, InstrumentTuning.StandardGuitar);
        var result = ChordNoteEngine.Identify(notes.Select(note => note.PitchClass).ToArray(), notes.MinBy(note => note.MidiNote)?.PitchClass);

        Assert.Equal(expected, result.Name);
    }

    [Fact]
    public void DoesNotInventANameForSingleOrUnmatchedPitchSets()
    {
        Assert.Null(ChordNoteEngine.Identify([0]).Name);
        Assert.Null(ChordNoteEngine.Identify([0, 1, 6]).Name);
    }

    [Fact]
    public void SymmetricAugmentedChordReportsConsistentAmbiguousCandidatesWithoutBass()
    {
        var result = ChordNoteEngine.Identify([0, 4, 8]);

        Assert.True(result.IsAmbiguous);
        Assert.Null(result.Name);
        Assert.Contains("Caug", result.Candidates);
        Assert.Contains("Eaug", result.Candidates);
        Assert.Contains("G#aug", result.Candidates);
    }
}