namespace StringTuner.Audio;

public interface IAudioCapture
{
    Task StartAsync(Action<ReadOnlyMemory<float>> onSamples, CancellationToken cancellationToken = default);

    Task StopAsync();
}