using System.Text;
using GuitarFlashcards.Core.Domain;

namespace GuitarFlashcards.Core.Audio;

public sealed record ChordSynthesisParameters
{
    public int SampleRate { get; init; } = 44100;
    public double StrumIntervalSeconds { get; init; } = 1.0;
    public double StrumDurationMilliseconds { get; init; } = 25;
    public double PluckPositionFraction { get; init; } = 0.18;
    public double RetriggerThreshold { get; init; } = 0.24;
    public int StrumsPerPhrase { get; init; } = 2;
    public double RetriggerExcitationLevel { get; init; } = 0.85;
    public double TransientDurationMilliseconds { get; init; } = 2.5;
    public double TransientAttackMilliseconds { get; init; } = 0.2;
    public double TransientDecayMilliseconds { get; init; } = 0.75;
    public double TransientLevel { get; init; } = 0.36;
    public double ToneAttackMilliseconds { get; init; } = 0.35;
    public double ReleaseMilliseconds { get; init; } = 12;
    public double MasterGain { get; init; } = 0.18;
    public int NoiseSeed { get; init; } = 19790423;
    public IReadOnlyList<double> StringLevels { get; init; } = [1, 0.96, 0.92, 0.88, 0.84, 0.8];
    public IReadOnlyList<double> StringDecaySeconds { get; init; } = [2.2, 2.05, 1.9, 1.75, 1.6, 1.45];

    internal void Validate()
    {
        if (SampleRate < 8000
            || !double.IsFinite(StrumIntervalSeconds) || StrumIntervalSeconds is < 0.25 or > 4.0
            || !double.IsFinite(StrumDurationMilliseconds) || StrumDurationMilliseconds < 0
            || !double.IsFinite(PluckPositionFraction) || PluckPositionFraction is <= 0 or >= 0.5
            || !double.IsFinite(RetriggerThreshold) || RetriggerThreshold is <= 0 or >= 1
            || StrumsPerPhrase is < 2 or > 8
            || !double.IsFinite(RetriggerExcitationLevel) || RetriggerExcitationLevel <= 0
            || !double.IsFinite(TransientDurationMilliseconds) || TransientDurationMilliseconds is < 1 or > 4
            || !double.IsFinite(TransientAttackMilliseconds) || TransientAttackMilliseconds <= 0
            || !double.IsFinite(TransientDecayMilliseconds) || TransientDecayMilliseconds <= 0
            || !double.IsFinite(TransientLevel) || TransientLevel < 0
            || !double.IsFinite(ToneAttackMilliseconds) || ToneAttackMilliseconds <= 0
            || !double.IsFinite(ReleaseMilliseconds) || ReleaseMilliseconds < 0
            || !double.IsFinite(MasterGain) || MasterGain <= 0)
        {
            throw new ArgumentException("Chord synthesis parameters contain an invalid value.");
        }

        if (StringLevels.Count < 6 || StringLevels.Take(6).Any(level => !double.IsFinite(level) || level < 0)
            || StringDecaySeconds.Count < 6 || StringDecaySeconds.Take(6).Any(decay => !double.IsFinite(decay) || decay <= 0))
        {
            throw new ArgumentException("Synthesis requires six valid string levels and decay times.");
        }
    }
}

public readonly record struct ChordTone(int StringIndex, double Frequency);

public sealed class ChordWaveformSynthesizer
{
    private readonly ChordSynthesisParameters _parameters;

    public ChordWaveformSynthesizer(ChordSynthesisParameters? parameters = null)
    {
        _parameters = parameters ?? new ChordSynthesisParameters();
        _parameters.Validate();
    }

    public byte[] CreateWaveFile(
        GuitarFingering fingering,
        InstrumentTuning? tuning = null,
        CancellationToken cancellationToken = default,
        double? strumIntervalSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(fingering);
        var synthesizer = strumIntervalSeconds is double interval
            ? new ChordWaveformSynthesizer(_parameters with { StrumIntervalSeconds = interval })
            : this;
        var notes = ChordNoteEngine.DeriveNotes(fingering, tuning ?? InstrumentTuning.StandardGuitar);
        return synthesizer.CreateWaveFile(notes.Select(note => new ChordTone(note.StringIndex, note.Frequency)), cancellationToken);
    }

    public byte[] CreateWaveFile(
        IEnumerable<ChordTone> chordTones,
        CancellationToken cancellationToken = default,
        double? strumIntervalSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(chordTones);
        if (strumIntervalSeconds is double interval && interval != _parameters.StrumIntervalSeconds)
        {
            return new ChordWaveformSynthesizer(_parameters with { StrumIntervalSeconds = interval })
                .CreateWaveFile(chordTones, cancellationToken);
        }

        var tones = chordTones.OrderBy(tone => tone.StringIndex).ToArray();
        if (tones.Length == 0 || tones.Any(tone => tone.StringIndex is < 0 or > 5
                || !double.IsFinite(tone.Frequency) || tone.Frequency is < 20 or > 5000)
            || tones.Select(tone => tone.StringIndex).Distinct().Count() != tones.Length)
        {
            throw new ArgumentException("Provide one valid tone per sounding guitar string.", nameof(chordTones));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var sampleRate = _parameters.SampleRate;
        var retriggerIntervalSamples = Math.Max(1, (int)Math.Round(_parameters.StrumIntervalSeconds * sampleRate));
        var sampleCount = checked(retriggerIntervalSamples * _parameters.StrumsPerPhrase);
        var dataLength = checked(sampleCount * sizeof(short));
        var onsetSamples = tones.Select(tone => (int)Math.Round(_parameters.StrumDurationMilliseconds
            * tone.StringIndex / 5 * sampleRate / 1000)).ToArray();
        var transientSamples = Math.Max(1, (int)Math.Round(_parameters.TransientDurationMilliseconds * sampleRate / 1000));
        var transientAttackSamples = Math.Max(1, (int)Math.Round(_parameters.TransientAttackMilliseconds * sampleRate / 1000));
        var transientDecaySamples = Math.Max(1, (int)Math.Round(_parameters.TransientDecayMilliseconds * sampleRate / 1000));
        var toneAttackSamples = Math.Max(1, (int)Math.Round(_parameters.ToneAttackMilliseconds * sampleRate / 1000));
        var releaseSamples = Math.Min(sampleCount, (int)Math.Round(_parameters.ReleaseMilliseconds * sampleRate / 1000));
        var releaseStart = sampleCount - releaseSamples;
        var normalization = _parameters.MasterGain / Math.Sqrt(tones.Length);
        var voices = tones.Select(tone => CreateVoice(tone, sampleRate)).ToArray();

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

        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            if ((sampleIndex & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var mixed = 0d;
            for (var toneIndex = 0; toneIndex < tones.Length; toneIndex++)
            {
                var voice = voices[toneIndex];
                var cycleStart = sampleIndex / retriggerIntervalSamples * retriggerIntervalSamples;
                var onset = cycleStart + onsetSamples[toneIndex];
                if (sampleIndex < onset)
                {
                    continue;
                }

                var ageSamples = sampleIndex - onset;
                if (ageSamples == 0)
                {
                    Excite(voice, _parameters, firstPluck: !voice.IsExcited);
                }

                var transient = 0d;
                if (ageSamples < transientSamples)
                {
                    var noise = NextNoise(ref voice.TransientRandomState);
                    var highPassedNoise = (noise - voice.PreviousTransientNoise * 0.85) * 0.54;
                    voice.PreviousTransientNoise = noise;
                    var attack = Math.Min(1, (ageSamples + 1d) / transientAttackSamples);
                    var decay = Math.Exp(-ageSamples / (double)transientDecaySamples);
                    transient = highPassedNoise * attack * decay * _parameters.TransientLevel;
                }

                var current = voice.DelayLine[voice.Cursor];
                var next = voice.DelayLine[(voice.Cursor + 1) % voice.DelayLine.Length];
                voice.DelayLine[voice.Cursor] = (current + next) * 0.5 * voice.Feedback;
                voice.Cursor = (voice.Cursor + 1) % voice.DelayLine.Length;
                var toneAttack = Math.Min(1, (ageSamples + 1d) / toneAttackSamples);
                mixed += (current * toneAttack + transient) * _parameters.StringLevels[voice.Tone.StringIndex];
            }

            var releaseProgress = releaseSamples <= 1
                ? 1
                : (sampleIndex - releaseStart) / (double)(releaseSamples - 1);
            var release = releaseSamples == 0 || sampleIndex < releaseStart
                ? 1
                : Math.Cos(releaseProgress * Math.PI / 2);
            var value = Math.Tanh(mixed * normalization) * release;
            writer.Write((short)Math.Round(value * short.MaxValue));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return stream.ToArray();
    }

    private StringVoice CreateVoice(ChordTone tone, int sampleRate)
    {
        var delayLength = Math.Max(2, (int)Math.Round(sampleRate / tone.Frequency));
        var decayRatio = _parameters.StringDecaySeconds[tone.StringIndex] / _parameters.StringDecaySeconds[0];
        var decaySeconds = _parameters.StrumIntervalSeconds / Math.Log(1 / _parameters.RetriggerThreshold) * decayRatio;
        var feedback = Math.Exp(-1 / (tone.Frequency * decaySeconds));
        var seed = unchecked((uint)_parameters.NoiseSeed + (uint)(tone.StringIndex + 1) * 0x9E3779B9u);
        return new StringVoice(tone, new double[delayLength], feedback, seed, seed ^ 0xA511E9B3u);
    }

    private void Excite(StringVoice voice, ChordSynthesisParameters parameters, bool firstPluck)
    {
        var noise = new double[voice.DelayLine.Length];
        for (var index = 0; index < noise.Length; index++)
        {
            noise[index] = NextNoise(ref voice.ExcitationRandomState);
        }

        var pluckOffset = Math.Clamp((int)Math.Round(noise.Length * parameters.PluckPositionFraction), 1, noise.Length - 1);
        var scale = 1 / Math.Sqrt(1 + 0.7 * 0.7);
        var excitationLevel = firstPluck ? 1 : parameters.RetriggerExcitationLevel;
        for (var index = 0; index < noise.Length; index++)
        {
            var value = (noise[index] - noise[(index + pluckOffset) % noise.Length] * 0.7) * scale * excitationLevel;
            var destination = (voice.Cursor + index) % noise.Length;
            voice.DelayLine[destination] = firstPluck ? value : voice.DelayLine[destination] + value;
        }

        voice.IsExcited = true;
    }

    private sealed class StringVoice(
        ChordTone tone,
        double[] delayLine,
        double feedback,
        uint excitationRandomState,
        uint transientRandomState)
    {
        public ChordTone Tone { get; } = tone;
        public double[] DelayLine { get; } = delayLine;
        public double Feedback { get; } = feedback;
        public uint ExcitationRandomState = excitationRandomState;
        public uint TransientRandomState = transientRandomState;
        public double PreviousTransientNoise;
        public int Cursor;
        public bool IsExcited;
    }

    private static double NextNoise(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state / (double)uint.MaxValue) * 2 - 1;
    }
}