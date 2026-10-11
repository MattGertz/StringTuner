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

        for (var seconds = 3; seconds >= 1; seconds--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await onCountdown(seconds);
            await _runtime.DelayAsync(TimeSpan.FromSeconds(1), cancellationToken);
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

            var choices = enabled.Length > 1 && previous is not null
                ? enabled.Where(chord => chord.Id != previous.Id).ToArray()
                : enabled.ToArray();
            var chord = choices[_runtime.NextIndex(choices.Length)];
            previous = chord;
            var fraction = Math.Clamp(_runtime.NextFraction(), 0, 1);
            var duration = settings.MinimumDurationSeconds
                + fraction * (settings.MaximumDurationSeconds - settings.MinimumDurationSeconds);
            cancellationToken.ThrowIfCancellationRequested();
            var appearanceTimestamp = _runtime.GetTimestamp();
            await onChord(chord, duration);
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
    }
}