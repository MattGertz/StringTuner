using System.Diagnostics;
using GuitarFlashcards.Core.Domain;

namespace GuitarFlashcards.Core.Practice;

public enum PracticeEndReason
{
    NoEnabledChords
}

public interface IPracticeRuntime
{
    Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken);

    long GetTimestamp();

    TimeSpan GetElapsedTime(long timestamp);

    int NextIndex(int exclusiveMaximum);

    double NextFraction();
}

public sealed class SystemPracticeRuntime : IPracticeRuntime
{
    public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken) => Task.Delay(duration, cancellationToken);

    public long GetTimestamp() => Stopwatch.GetTimestamp();

    public TimeSpan GetElapsedTime(long timestamp) => Stopwatch.GetElapsedTime(timestamp);

    public int NextIndex(int exclusiveMaximum) => Random.Shared.Next(exclusiveMaximum);

    public double NextFraction() => Random.Shared.NextDouble();
}

public sealed class PracticeEngine(IPracticeRuntime? runtime = null)
{
    private readonly IPracticeRuntime _runtime = runtime ?? new SystemPracticeRuntime();

    public async Task<PracticeEndReason> RunAsync(
        Func<IReadOnlyList<ChordDefinition>> getChords,
        PracticeSettings settings,
        Func<int, ValueTask> onCountdown,
        Func<ChordDefinition, double, ValueTask> onChord,
        CancellationToken cancellationToken) =>
        await RunWithPreviewAsync(
            getChords,
            settings,
            (seconds, _) => onCountdown(seconds),
            (chord, _, duration) => onChord(chord, duration),
            waitForNextAsync: null,
            cancellationToken);

    public async Task<PracticeEndReason> RunWithPreviewAsync(
        Func<IReadOnlyList<ChordDefinition>> getChords,
        PracticeSettings settings,
        Func<int, ChordDefinition?, ValueTask> onCountdown,
        Func<ChordDefinition, ChordDefinition?, double, ValueTask> onChord,
        Func<CancellationToken, Task>? waitForNextAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(getChords);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(onCountdown);
        ArgumentNullException.ThrowIfNull(onChord);
        if (settings.MinimumDurationSeconds <= 0 || settings.MaximumDurationSeconds <= settings.MinimumDurationSeconds)
        {
            throw new ArgumentException("Practice duration settings are invalid.", nameof(settings));
        }

        var enabledAtStart = getChords().Where(chord => chord.Enabled).ToArray();
        if (enabledAtStart.Length == 0)
        {
            return PracticeEndReason.NoEnabledChords;
        }

        ChordDefinition? current = Select(enabledAtStart, previous: null);
        for (var seconds = 3; seconds >= 1; seconds--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await onCountdown(seconds, current);
            await _runtime.DelayAsync(TimeSpan.FromSeconds(1), cancellationToken);
            var enabled = getChords().Where(chord => chord.Enabled).ToArray();
            if (enabled.Length == 0)
            {
                return PracticeEndReason.NoEnabledChords;
            }

            if (!enabled.Any(chord => chord.Id == current.Id))
            {
                current = Select(enabled, previous: null);
            }
        }

        ChordDefinition? previous = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var enabled = getChords().Where(chord => chord.Enabled).ToArray();
            if (enabled.Length == 0)
            {
                return PracticeEndReason.NoEnabledChords;
            }

            if (current is null || !enabled.Any(chord => chord.Id == current.Id))
            {
                current = Select(enabled, previous);
            }

            var next = Select(enabled, current);
            var fraction = Math.Clamp(_runtime.NextFraction(), 0, 1);
            var duration = settings.MinimumDurationSeconds
                + fraction * (settings.MaximumDurationSeconds - settings.MinimumDurationSeconds);
            cancellationToken.ThrowIfCancellationRequested();
            var appearanceTimestamp = _runtime.GetTimestamp();
            await onChord(current, next, duration);
            if (settings.Mode == PracticeMode.Manual)
            {
                if (waitForNextAsync is null)
                {
                    throw new InvalidOperationException("Manual practice requires an advance signal.");
                }

                await waitForNextAsync(cancellationToken);
            }
            else
            {
                var remainingSeconds = duration - _runtime.GetElapsedTime(appearanceTimestamp).TotalSeconds;
                if (remainingSeconds > 0)
                {
                    await _runtime.DelayAsync(TimeSpan.FromSeconds(remainingSeconds), cancellationToken);
                }
                else
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            previous = current;
            var refreshedEnabled = getChords().Where(chord => chord.Enabled).ToArray();
            if (refreshedEnabled.Length == 0)
            {
                return PracticeEndReason.NoEnabledChords;
            }

            current = refreshedEnabled.FirstOrDefault(chord => chord.Id == next.Id)
                ?? Select(refreshedEnabled, previous);
        }
    }

    private ChordDefinition Select(IReadOnlyList<ChordDefinition> enabled, ChordDefinition? previous)
    {
        var choices = enabled.Count > 1 && previous is not null
            ? enabled.Where(chord => chord.Id != previous.Id).ToArray()
            : enabled.ToArray();
        return choices[_runtime.NextIndex(choices.Length)];
    }
}