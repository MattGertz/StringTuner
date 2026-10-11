using System.Buffers.Binary;
using GuitarFlashcards.Core.Audio;
using GuitarFlashcards.Core.Domain;

namespace GuitarFlashcards.Core.Tests;

public sealed class ChordWaveformSynthesizerTests
{
    [Fact]
    public void SynthesizesMultiNotePcmWaveAndIgnoresMutedStrings()
    {
        var fingering = GuitarFingering.Open();
        fingering = FingeringEditor.SetStringState(fingering, 2, StringNoteState.Muted);
        var wave = ChordWaveformSynthesizer.CreateWaveFile(fingering, loopSeconds: 1);

        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wave, 8, 4));
        Assert.Equal(44100 * 2 + 44, wave.Length);
        Assert.Contains(wave.AsSpan(44).ToArray(), sample => sample != 0);
        for (var offset = 44; offset < wave.Length; offset += sizeof(short))
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(offset, sizeof(short)));
            Assert.InRange(sample, (short)-short.MaxValue, short.MaxValue);
        }
    }

    [Fact]
    public void SynthesizerRejectsEmptyAndInvalidFrequencySets()
    {
        Assert.Throws<ArgumentException>(() => ChordWaveformSynthesizer.CreateWaveFile([]));
        Assert.Throws<ArgumentException>(() => ChordWaveformSynthesizer.CreateWaveFile([double.NaN]));
    }

    [Fact]
    public void SynthesizesFrettedNoteAtItsDerivedPitch()
    {
        var fingering = FingeringEditor.SetFrettedPosition(GuitarFingering.Open(), 0, 3, 2);
        var notes = ChordNoteEngine.DeriveNotes(fingering, InstrumentTuning.StandardGuitar);
        var wave = ChordWaveformSynthesizer.CreateWaveFile(fingering, loopSeconds: 1);

        Assert.Equal(43, notes[0].MidiNote);
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wave, 8, 4));
        Assert.Equal(44100 * 2 + 44, wave.Length);
    }

    [Fact]
    public void LoopTailDoesNotFadeToSilenceAtTheTwoSecondBoundary()
    {
        var wave = ChordWaveformSynthesizer.CreateWaveFile([440], loopSeconds: 2);
        var steadyRms = ReadRms(wave, startSample: 40000, sampleCount: 1000);
        var tailRms = ReadRms(wave, startSample: 87800, sampleCount: 400);

        Assert.True(tailRms > steadyRms * 0.9, $"Expected an audible loop tail; steady RMS={steadyRms}, tail RMS={tailRms}.");
    }

    private static double ReadRms(byte[] wave, int startSample, int sampleCount)
    {
        var sumSquares = 0d;
        for (var index = startSample; index < startSample + sampleCount; index++)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(44 + index * sizeof(short), sizeof(short)));
            sumSquares += (double)sample * sample;
        }

        return Math.Sqrt(sumSquares / sampleCount);
    }
}