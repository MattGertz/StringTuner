namespace StringTuner.Core.Domain;

public sealed record InstrumentCatalogLoadResult(
    IReadOnlyList<InstrumentTuning> Instruments,
    string? Error = null);