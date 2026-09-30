namespace StringTuner.Audio;

public interface IAudioToneGenerator
{
    bool IsPlaying { get; }

    void SetVolume(double volume);

    Task StartAsync(double frequency, double volume, CancellationToken cancellationToken = default);

    Task StopAsync();
}