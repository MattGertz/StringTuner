using GuitarFlashcards.Core.Domain;

namespace GuitarFlashcards.Core.Tests;

public sealed class FretboardScrollPositionTests
{
    [Fact]
    public void EmptyAndFirstPositionFingeringsStartAtFretOne()
    {
        Assert.Equal(1, FretboardScrollPosition.FirstVisibleFret(GuitarFingering.Open()));

        var fingering = FingeringEditor.SetFrettedPosition(GuitarFingering.Open(), 2, 3, 1);
        Assert.Equal(1, FretboardScrollPosition.FirstVisibleFret(fingering));
    }

    [Fact]
    public void PracticeWindowKeepsEntireFourThroughSevenShapeVisible()
    {
        var fingering = GuitarFingering.Open();
        for (var index = 0; index < 4; index++)
        {
            fingering = FingeringEditor.SetFrettedPosition(fingering, index, index + 4, 1);
        }

        Assert.Equal(4, FretboardScrollPosition.FirstVisibleFret(fingering));
    }

    [Fact]
    public void WideFretSpanScrollsEnoughToKeepHighestFretVisible()
    {
        var fingering = GuitarFingering.Open();
        fingering = FingeringEditor.SetFrettedPosition(fingering, 0, 4, 1);
        fingering = FingeringEditor.SetFrettedPosition(fingering, 1, 12, 2);

        Assert.Equal(8, FretboardScrollPosition.FirstVisibleFret(fingering));
    }
}