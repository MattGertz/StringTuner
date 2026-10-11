using GuitarFlashcards.Core.Domain;

namespace GuitarFlashcards.Core.Tests;

public sealed class BarreInferenceTests
{
    [Fact]
    public void InfersTwoAndThreeStringAdjacentBarres()
    {
        var twoStrings = GuitarFingering.Open();
        twoStrings = FingeringEditor.SetFrettedPosition(twoStrings, 1, 2, 1);
        twoStrings = FingeringEditor.SetFrettedPosition(twoStrings, 2, 2, 1);

        Assert.Equal(new BarreSegment(2, 1, 1, 2), Assert.Single(BarreInference.Infer(twoStrings)));

        var threeStrings = FingeringEditor.SetFrettedPosition(twoStrings, 3, 2, 1);
        Assert.Equal(new BarreSegment(2, 1, 1, 3), Assert.Single(BarreInference.Infer(threeStrings)));
    }

    [Fact]
    public void DifferentFingersAndFretsDoNotJoinBarres()
    {
        var fingering = GuitarFingering.Open();
        fingering = FingeringEditor.SetFrettedPosition(fingering, 1, 2, 1);
        fingering = FingeringEditor.SetFrettedPosition(fingering, 2, 2, 2);
        fingering = FingeringEditor.SetFrettedPosition(fingering, 3, 3, 1);

        Assert.Empty(BarreInference.Infer(fingering));
    }

    [Theory]
    [InlineData(StringNoteState.Open)]
    [InlineData(StringNoteState.Muted)]
    public void SameFingerAndFretConnectAcrossOpenOrMutedGap(StringNoteState interruption)
    {
        var fingering = GuitarFingering.Open();
        fingering = FingeringEditor.SetFrettedPosition(fingering, 1, 2, 1);
        fingering = FingeringEditor.SetStringState(fingering, 2, interruption);
        fingering = FingeringEditor.SetFrettedPosition(fingering, 3, 2, 1);

        Assert.Equal(new BarreSegment(2, 1, 1, 3), Assert.Single(BarreInference.Infer(fingering)));
    }

    [Fact]
    public void SameAssignmentsAcrossGapsFormSpansAndUpdateWithFingering()
    {
        var fingering = GuitarFingering.Open();
        fingering = FingeringEditor.SetFrettedPosition(fingering, 0, 3, 1);
        fingering = FingeringEditor.SetFrettedPosition(fingering, 1, 3, 1);
        fingering = FingeringEditor.SetFrettedPosition(fingering, 4, 5, 1);
        fingering = FingeringEditor.SetFrettedPosition(fingering, 5, 5, 1);

        Assert.Equal(
            [new BarreSegment(3, 1, 0, 1), new BarreSegment(5, 1, 4, 5)],
            BarreInference.Infer(fingering));

        var changed = FingeringEditor.SetFrettedPosition(fingering, 1, 4, 2);
        Assert.Equal(new BarreSegment(5, 1, 4, 5), Assert.Single(BarreInference.Infer(changed)));
    }

    [Fact]
    public void InferenceDoesNotChangeDerivedNotesOrChordIdentification()
    {
        var fingering = GuitarFingering.Open();
        fingering = FingeringEditor.SetFrettedPosition(fingering, 0, 3, 1);
        fingering = FingeringEditor.SetFrettedPosition(fingering, 1, 3, 1);
        var notesBefore = ChordNoteEngine.DeriveNotes(fingering, InstrumentTuning.StandardGuitar);
        var nameBefore = ChordNoteEngine.Identify(notesBefore.Select(note => note.PitchClass).ToArray(), notesBefore.MinBy(note => note.MidiNote)?.PitchClass).Name;

        _ = BarreInference.Infer(fingering);
        var notesAfter = ChordNoteEngine.DeriveNotes(fingering, InstrumentTuning.StandardGuitar);
        var nameAfter = ChordNoteEngine.Identify(notesAfter.Select(note => note.PitchClass).ToArray(), notesAfter.MinBy(note => note.MidiNote)?.PitchClass).Name;

        Assert.Equal(notesBefore, notesAfter);
        Assert.Equal(nameBefore, nameAfter);
    }
}