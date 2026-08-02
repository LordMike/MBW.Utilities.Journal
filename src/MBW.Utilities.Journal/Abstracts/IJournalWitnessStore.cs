namespace MBW.Utilities.Journal.Abstracts;

/// <summary>
/// Stores the durable commit decision for one coordinated participant set.
/// </summary>
public interface IJournalWitnessStore
{
    /// <summary>
    /// Reads the preparation key for the durable commit decision, if one exists.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the read.</param>
    /// <returns>The stored key, or <see langword="null"/> only when no witness exists.</returns>
    /// <remarks>
    /// Storage, format, and integrity failures must be reported as exceptions and must never be treated as a missing
    /// witness.
    /// </remarks>
    ValueTask<Guid?> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Durably stores the commit decision for <paramref name="preparationKey"/> before returning.
    /// </summary>
    /// <param name="preparationKey">The non-empty key shared by every prepared participant.</param>
    /// <param name="cancellationToken">Token used to cancel the store operation.</param>
    /// <remarks>
    /// Storing the existing key is idempotent. An implementation must reject a different existing key rather than
    /// overwrite another transaction's decision.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="preparationKey"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">A witness for another preparation key already exists.</exception>
    ValueTask StoreAsync(Guid preparationKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the witness only when it contains <paramref name="preparationKey"/>.
    /// </summary>
    /// <param name="preparationKey">The non-empty key whose completed decision may be removed.</param>
    /// <param name="cancellationToken">Token used to cancel the clear operation.</param>
    /// <remarks>Clearing an already-missing witness is idempotent.</remarks>
    /// <exception cref="ArgumentException"><paramref name="preparationKey"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">The stored witness belongs to another preparation key.</exception>
    ValueTask ClearAsync(Guid preparationKey, CancellationToken cancellationToken = default);
}
