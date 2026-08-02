namespace MBW.Utilities.Journal.Abstracts;

/// <summary>
/// Stores the durable commit decision for one coordinated participant set.
/// </summary>
public interface IJournalWitnessStore
{
    ValueTask<Guid?> ReadAsync(CancellationToken cancellationToken = default);
    ValueTask StoreAsync(Guid preparationKey, CancellationToken cancellationToken = default);
    ValueTask ClearAsync(Guid preparationKey, CancellationToken cancellationToken = default);
}
