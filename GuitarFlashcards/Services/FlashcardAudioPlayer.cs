using GuitarFlashcards.Core.Audio;
using GuitarFlashcards.Core.Domain;
using Plugin.Maui.Audio;

namespace GuitarFlashcards.Services;

public sealed class FlashcardAudioPlayer(IAudioManager audioManager, ChordWaveformSynthesizer synthesizer) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IAudioPlayer? _player;
    private CancellationTokenSource? _monitorCancellation;
    private CancellationTokenRegistration _playCancellation;

    public bool IsPlaying => _player?.IsPlaying == true;

    public event EventHandler? PlaybackStateChanged;

    public async Task PlayAsync(GuitarFingering fingering, double strumIntervalSeconds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var wave = await Task.Run(
            () => synthesizer.CreateWaveFile(fingering, cancellationToken: cancellationToken, strumIntervalSeconds: strumIntervalSeconds),
            cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var stream = new MemoryStream(wave, writable: false);
            var player = audioManager.CreatePlayer(stream);
            player.Loop = true;
            player.Volume = 0;
            _player = player;
            player.Play();
            _monitorCancellation = new CancellationTokenSource();
            _ = MonitorPlaybackAsync(player, _monitorCancellation.Token);
            _playCancellation = cancellationToken.Register(() => _ = StopAsync());
            for (var step = 1; step <= 4; step++)
            {
                await Task.Delay(12, cancellationToken);
                if (!ReferenceEquals(_player, player))
                {
                    return;
                }

                player.Volume = step / 4f;
            }
        }
        catch
        {
            await StopCoreAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }

        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _gate.Release();
        }

        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _gate.Dispose();
    }

    private async Task StopCoreAsync()
    {
        var player = _player;
        _player = null;
        _monitorCancellation?.Cancel();
        _monitorCancellation?.Dispose();
        _monitorCancellation = null;
        _playCancellation.Dispose();
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

    private async Task MonitorPlaybackAsync(IAudioPlayer player, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(100, cancellationToken);
            while (player.IsPlaying)
            {
                await Task.Delay(100, cancellationToken);
            }

            await _gate.WaitAsync(cancellationToken);
            var released = false;
            try
            {
                if (ReferenceEquals(_player, player))
                {
                    _player = null;
                    _monitorCancellation?.Dispose();
                    _monitorCancellation = null;
                    _playCancellation.Dispose();
                    player.Dispose();
                    released = true;
                }
            }
            finally
            {
                _gate.Release();
            }

            if (released)
            {
                PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}