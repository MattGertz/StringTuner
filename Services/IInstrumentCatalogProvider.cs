using StringTuner.Core.Domain;

namespace StringTuner.Services;

public interface IInstrumentCatalogProvider
{
    Task<InstrumentCatalogLoadResult> LoadAsync(CancellationToken cancellationToken = default);
}