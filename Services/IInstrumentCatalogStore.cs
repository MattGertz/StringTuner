namespace StringTuner.Services;

public interface IInstrumentCatalogStore
{
    Task SaveAsync(string json, CancellationToken cancellationToken = default);
}