using System.Buffers.Binary;
using Plugin.Maui.Audio;

namespace StringTuner.Audio;

public sealed class PluginAudioCapture(IAudioManager audioManager) : IAudioCapture, IAsyncDisposable
{
    private IAudioStreamer? _streamer;
    private Action<ReadOnlyMemory<float>>? _onSamples;

    public async Task StartAsync(Action<ReadOnlyMemory<float>> onSamples, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onSamples);
        cancellationToken.ThrowIfCancellationRequested();
        if (_streamer is not null)
        {
            return;
        }

        var streamer = audioManager.CreateStreamer();
        streamer.Options.SampleRate = 44100;
        streamer.Options.Channels = ChannelType.Mono;
        streamer.Options.BitDepth = BitDepth.Pcm16bit;
        _onSamples = onSamples;
        streamer.OnAudioCaptured += OnAudioCaptured;

        try
        {
            await streamer.StartAsync();
            _streamer = streamer;
        }
        catch
        {
            streamer.OnAudioCaptured -= OnAudioCaptured;
            _onSamples = null;
            throw;
        }
    }

    public async Task StopAsync()
    {
        var streamer = _streamer;
        _streamer = null;
        if (streamer is null)
        {
            return;
        }

        streamer.OnAudioCaptured -= OnAudioCaptured;
        _onSamples = null;
        await streamer.StopAsync();
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private void OnAudioCaptured(object? sender, AudioStreamEventArgs eventArgs)
    {
        var bytes = eventArgs.Audio;
        var samples = new float[bytes.Length / sizeof(short)];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(index * sizeof(short))) / 32768f;
        }

        _onSamples?.Invoke(samples);
    }
}