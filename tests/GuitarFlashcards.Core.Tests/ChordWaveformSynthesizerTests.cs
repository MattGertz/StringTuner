using System.Buffers.Binary;
using GuitarFlashcards.Core.Audio;
using GuitarFlashcards.Core.Domain;

namespace GuitarFlashcards.Core.Tests;

public sealed class ChordWaveformSynthesizerTests
{
    [Fact]
    public void OpenSixStringChordProducesFiniteBoundedPcm()
    {
        var synthesizerParameters = TestParameters() with { SampleRate = 8000, ReleaseMilliseconds = 12 };
        var synthesizer = new ChordWaveformSynthesizer(synthesizerParameters);
        var wave = synthesizer.CreateWaveFile(GuitarFingering.Open());
        var expectedInterval = (int)Math.Round(synthesizerParameters.StrumIntervalSeconds * 8000);

        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wave, 8, 4));
        Assert.Equal(44 + expectedInterval * synthesizerParameters.StrumsPerPhrase * sizeof(short), wave.Length);
        Assert.True(ReadRms(wave, 0, Math.Min(8000, SampleCount(wave))) > 100);
        var maxMagnitude = 0;
        for (var offset = 44; offset < wave.Length; offset += sizeof(short))
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(offset, sizeof(short)));
            maxMagnitude = Math.Max(maxMagnitude, Math.Abs((int)sample));
        }

        Assert.True(maxMagnitude < short.MaxValue * 0.98);
    }

    [Theory]
    [InlineData(82.4069, 0)]
    [InlineData(329.6276, 5)]
    public void SingleLowAndHighStringsHaveExpectedStrumOnsets(double frequency, int stringIndex)
    {
        var wave = CreateSynthesizer().CreateWaveFile([new ChordTone(stringIndex, frequency)]);
        var onsetSample = (int)Math.Round(25d * stringIndex / 5 * 8000 / 1000);

        Assert.True(wave.Length > 44 + (onsetSample + 80) * sizeof(short));
        Assert.All(Enumerable.Range(0, onsetSample), sampleIndex => Assert.Equal((short)0, ReadSample(wave, sampleIndex)));
        Assert.True(ReadRms(wave, onsetSample, 80) > 1);
    }

    [Fact]
    public void MutedStringsAreSkippedAndTwoStringVoicingsRemainAudible()
    {
        var fingering = GuitarFingering.Open();
        foreach (var stringIndex in new[] { 0, 1, 3, 4 })
        {
            fingering = FingeringEditor.SetStringState(fingering, stringIndex, StringNoteState.Muted);
        }
        fingering = FingeringEditor.SetFrettedPosition(fingering, 5, 3, 2);

        Assert.Equal(2, ChordNoteEngine.DeriveNotes(fingering, InstrumentTuning.StandardGuitar).Count);
        var wave = CreateSynthesizer().CreateWaveFile(fingering);
        Assert.True(ReadRms(wave, 0, Math.Min(8000, SampleCount(wave))) > 30);

        var threeStringFingering = GuitarFingering.Open();
        foreach (var stringIndex in new[] { 0, 1, 4 })
        {
            threeStringFingering = FingeringEditor.SetStringState(threeStringFingering, stringIndex, StringNoteState.Muted);
        }
        Assert.Equal(3, ChordNoteEngine.DeriveNotes(threeStringFingering, InstrumentTuning.StandardGuitar).Count);
        var threeStringWave = CreateSynthesizer().CreateWaveFile(threeStringFingering);
        Assert.True(ReadRms(threeStringWave, 0, Math.Min(8000, SampleCount(threeStringWave))) > 30);
    }

    [Fact]
    public void TransientNoiseIsReproducibleForUnitTests()
    {
        var synthesizer = CreateSynthesizer(new ChordSynthesisParameters { NoiseSeed = 12345 });
        var tones = new[] { new ChordTone(0, 82.4069), new ChordTone(5, 329.6276) };

        Assert.Equal(synthesizer.CreateWaveFile(tones), synthesizer.CreateWaveFile(tones));
    }

    [Fact]
    public void StringIsRepluckedAfterItsDecayCrossesTheConfiguredThreshold()
    {
        var parameters = TestParameters();
        var tone = new[] { new ChordTone(0, 82.4069) };
        var wave = new ChordWaveformSynthesizer(parameters).CreateWaveFile(tone);
        var quietRetrigger = new ChordWaveformSynthesizer(parameters with { RetriggerExcitationLevel = 0.01 }).CreateWaveFile(tone);
        var retriggerSample = (int)Math.Round(parameters.StrumIntervalSeconds * parameters.SampleRate);
        var afterRetrigger = ReadRms(wave, retriggerSample + 10, 500);
        var quietRetriggerRms = ReadRms(quietRetrigger, retriggerSample + 10, 500);

        Assert.True(afterRetrigger > quietRetriggerRms * 1.5,
            $"Expected a fresh pluck at the configured interval; normal={afterRetrigger}, quiet={quietRetriggerRms}.");
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(1.0)]
    [InlineData(4.0)]
    public void PhraseLengthUsesConfiguredStrumInterval(double intervalSeconds)
    {
        var parameters = TestParameters() with
        {
            SampleRate = 8000,
            StrumIntervalSeconds = intervalSeconds,
            StrumsPerPhrase = 2
        };

        var wave = new ChordWaveformSynthesizer(parameters).CreateWaveFile([new ChordTone(0, 82.4069)]);

        Assert.Equal(44 + (int)(intervalSeconds * 8000 * 2) * sizeof(short), wave.Length);
    }

    [Fact]
    public async Task SynthesisCancellationInterruptsStrumAndDecayGeneration()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => CreateSynthesizer().CreateWaveFile(GuitarFingering.Open(), cancellationToken: cancelled.Token));

        using var duringGeneration = new CancellationTokenSource();
        var longSynthesizer = new ChordWaveformSynthesizer(new ChordSynthesisParameters
        {
            SampleRate = 44100,
            StrumIntervalSeconds = 4,
            StrumsPerPhrase = 8
        });
        var generation = Task.Run(() => longSynthesizer.CreateWaveFile(GuitarFingering.Open(), cancellationToken: duringGeneration.Token));
        duringGeneration.CancelAfter(1);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generation);
    }

    [Fact]
    public void SynthesizerRejectsInvalidToneSetsAndParameters()
    {
        var synthesizer = CreateSynthesizer();
        Assert.Throws<ArgumentException>(() => synthesizer.CreateWaveFile([]));
        Assert.Throws<ArgumentException>(() => synthesizer.CreateWaveFile([new ChordTone(0, double.NaN)]));
        Assert.Throws<ArgumentException>(() => new ChordWaveformSynthesizer(new ChordSynthesisParameters { StrumDurationMilliseconds = -1 }));
        Assert.Throws<ArgumentException>(() => new ChordWaveformSynthesizer(new ChordSynthesisParameters { StrumIntervalSeconds = 0.249 }));
        Assert.Throws<ArgumentException>(() => new ChordWaveformSynthesizer(new ChordSynthesisParameters { StrumIntervalSeconds = 4.001 }));
    }

    private static ChordWaveformSynthesizer CreateSynthesizer(ChordSynthesisParameters? parameters = null) =>
        new((parameters ?? TestParameters()) with { SampleRate = 8000, ReleaseMilliseconds = 12 });

    private static ChordSynthesisParameters TestParameters() => new()
    {
        StringDecaySeconds = [0.3, 0.3, 0.3, 0.3, 0.3, 0.3],
        TransientDurationMilliseconds = 2.5
    };

    private static short ReadSample(byte[] wave, int sampleIndex) =>
        BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(44 + sampleIndex * sizeof(short), sizeof(short)));

    private static int SampleCount(byte[] wave) => (wave.Length - 44) / sizeof(short);

    private static double ReadRms(byte[] wave, int startSample, int sampleCount)
    {
        var sumSquares = 0d;
        for (var index = startSample; index < startSample + sampleCount; index++)
        {
            var sample = ReadSample(wave, index);
            sumSquares += (double)sample * sample;
        }

        return Math.Sqrt(sumSquares / sampleCount);
    }
}