using System.Text;
using GuitarFlashcards.Core.Domain;

namespace GuitarFlashcards.Core.Audio;

public static class ChordWaveformSynthesizer
{
    private const double SecondHarmonic = 0.28;
    private const double ThirdHarmonic = 0.12;
    private const double FourthHarmonic = 0.05;
    private const double PeakNormalization = 1 + SecondHarmonic + ThirdHarmonic + FourthHarmonic;

    public static byte[] CreateWaveFile(GuitarFingering fingering, InstrumentTuning? tuning = null, int sampleRate = 44100, int loopSeconds = 2)
    {
        ArgumentNullException.ThrowIfNull(fingering);
        var notes = ChordNoteEngine.DeriveNotes(fingering, tuning ?? InstrumentTuning.StandardGuitar);
        return CreateWaveFile(notes.Select(note => note.Frequency), sampleRate, loopSeconds);
    }

    public static byte[] CreateWaveFile(IEnumerable<double> frequencies, int sampleRate = 44100, int loopSeconds = 2)
    {
        ArgumentNullException.ThrowIfNull(frequencies);
        var notes = frequencies.ToArray();
        if (notes.Length == 0 || notes.Any(frequency => !double.IsFinite(frequency) || frequency is < 20 or > 5000))
        {
            throw new ArgumentException("At least one valid note frequency is required.", nameof(frequencies));
        }

        if (sampleRate < 8000 || loopSeconds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be at least 8000 Hz and loop duration at least one second.");
        }

        var sampleCount = checked(sampleRate * loopSeconds);
        var dataLength = checked(sampleCount * sizeof(short));
        using var stream = new MemoryStream(44 + dataLength);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataLength);

        var voiceGain = 0.25 / Math.Sqrt(notes.Length);
        for (var index = 0; index < sampleCount; index++)
        {
            var time = (double)index / sampleRate;
            var mixed = 0d;
            foreach (var frequency in notes)
            {
                var phase = 2 * Math.PI * frequency * time;
                mixed += (Math.Sin(phase)
                    + SecondHarmonic * Math.Sin(2 * phase)
                    + ThirdHarmonic * Math.Sin(3 * phase)
                    + FourthHarmonic * Math.Sin(4 * phase)) / PeakNormalization;
            }

            var sample = (short)Math.Clamp(Math.Round(mixed * voiceGain * short.MaxValue), short.MinValue, short.MaxValue);
            writer.Write(sample);
        }

        return stream.ToArray();
    }
}