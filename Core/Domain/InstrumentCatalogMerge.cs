namespace StringTuner.Core.Domain;

public static class InstrumentCatalogMerge
{
    public static IReadOnlyList<InstrumentTuning>? Merge(
        IReadOnlyList<InstrumentTuning> current,
        IReadOnlyList<InstrumentTuning> imported,
        Func<InstrumentTuning, InstrumentTuning, bool?> resolveConflict)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(resolveConflict);

        var result = current.ToList();
        foreach (var incoming in imported)
        {
            var index = result.FindIndex(existing => HasSameIdentity(existing, incoming));
            if (index < 0)
            {
                result.Add(incoming);
                continue;
            }

            var useImported = resolveConflict(result[index], incoming);
            if (useImported is null)
            {
                return null;
            }

            if (useImported.Value)
            {
                result[index] = incoming;
            }
        }

        return result;
    }

    public static bool HasSameIdentity(InstrumentTuning left, InstrumentTuning right) =>
        string.Equals(left.InstrumentName, right.InstrumentName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.TuningName, right.TuningName, StringComparison.OrdinalIgnoreCase);
}