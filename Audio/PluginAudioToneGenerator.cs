using Plugin.Maui.Audio;

namespace StringTuner.Audio;

public sealed class PluginAudioToneGenerator(IAudioManager audioManager) : IAudioToneGenerator, IDisposable
{
    private const int SampleRate = 44100;
    private IAudioPlayer? _player;

    public bool IsPlaying => _player?.IsPlaying == true;

    public void SetVolume(double volume)
    {
        if (_player is not null)
        {
            _player.Volume = (float)Math.Clamp(volume, 0, 1);
        }
    }

    public async Task StartAsync(double frequency, double volume, CancellationToken cancellationToken = default)
    {
        if (frequency is < 20 or > 2000)
        {
            throw new ArgumentOutOfRangeException(nameof(frequency));
        }

        cancellationToken.ThrowIfCancellationRequested();
        await StopAsync();
        var wave = CreateLoopingWave(frequency);
        _player = audioManager.CreatePlayer(wave);
        var player = _player;
        player.Loop = true;
        var targetVolume = (float)Math.Clamp(volume, 0, 1);
        player.Volume = 0;
        player.Play();
        for (var step = 1; step <= 4; step++)
        {
            await Task.Delay(12, cancellationToken);
            if (!ReferenceEquals(_player, player))
            {
                return;
            }

            player.Volume = targetVolume * step / 4;
        }
    }

    public async Task StopAsync()
    {
        var player = _player;
        _player = null;
        if (player is null)
        {
            return;
        }

        try
        {
            var startingVolume = player.Volume;
            for (var step = 1; step <= 4 && player.IsPlaying; step++)
            {
                await Task.Delay(15);
                player.Volume = startingVolume * (1 - step / 4f);
            }

            player.Stop();
        }
        finally
        {
            player.Dispose();
        }
    }

    public void Dispose()
    {
        _player?.Stop();
        _player?.Dispose();
        _player = null;
    }

    private static MemoryStream CreateLoopingWave(double frequency)
    {
        const int loopSeconds = 2;
        const double secondHarmonic = 0.28;
        const double thirdHarmonic = 0.12;
        const double fourthHarmonic = 0.05;
        const double peakNormalization = 1 + secondHarmonic + thirdHarmonic + fourthHarmonic;
        var cycleCount = (int)Math.Round(frequency * loopSeconds);
        var sampleCount = (int)Math.Round(cycleCount * SampleRate / frequency);
        var stream = new MemoryStream(44 + sampleCount * sizeof(short));
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(36 + sampleCount * sizeof(short));
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(sampleCount * sizeof(short));

        for (var index = 0; index < sampleCount; index++)
        {
            var phase = 2 * Math.PI * frequency * index / SampleRate;
            var waveform = Math.Sin(phase)
                + secondHarmonic * Math.Sin(2 * phase)
                + thirdHarmonic * Math.Sin(3 * phase)
                + fourthHarmonic * Math.Sin(4 * phase);
            var value = waveform / peakNormalization * short.MaxValue * 0.25;
            writer.Write((short)Math.Round(value));
        }

        stream.Position = 0;
        return stream;
    }
}