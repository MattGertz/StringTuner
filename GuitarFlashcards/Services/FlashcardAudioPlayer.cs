using GuitarFlashcards.Core.Audio;
using GuitarFlashcards.Core.Domain;
using Plugin.Maui.Audio;

namespace GuitarFlashcards.Services;

public sealed class FlashcardAudioPlayer(IAudioManager audioManager) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IAudioPlayer? _player;

    public bool IsPlaying => _player?.IsPlaying == true;

    public async Task PlayAsync(GuitarFingering fingering, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var stream = new MemoryStream(ChordWaveformSynthesizer.CreateWaveFile(fingering));
            var player = audioManager.CreatePlayer(stream);
            player.Loop = true;
            player.Volume = 0;
            _player = player;
            player.Play();
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
}