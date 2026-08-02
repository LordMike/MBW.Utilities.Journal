namespace MBW.Utilities.Journal.Abstracts;

/// <summary>
/// Stores the durable commit-phase witness for one coordinated participant set.
/// </summary>
public interface IJournalWitnessStore
{
    /// <summary>
    /// Reads the durable witness, if one exists.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the read.</param>
    /// <returns>The stored witness, or <see langword="null"/> only when no witness exists.</returns>
    /// <remarks>
    /// Storage, format, and integrity failures must be reported as exceptions and must never be treated as a missing
    /// witness.
    /// </remarks>
    ValueTask<JournalWitness?> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Durably stores <paramref name="witness"/> before returning.
    /// </summary>
    /// <param name="witness">The preparation key and complete participant nonce set.</param>
    /// <param name="cancellationToken">Token used to cancel the store operation.</param>
    /// <remarks>
    /// Storing the exact existing witness is idempotent. An implementation must reject any different existing witness
    /// rather than overwrite another transaction's coordination state.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A different witness already exists.</exception>
    ValueTask StoreAsync(JournalWitness witness, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the witness only when it exactly matches <paramref name="witness"/>.
    /// </summary>
    /// <param name="witness">The witness whose completed commit phase may be removed.</param>
    /// <param name="cancellationToken">Token used to cancel the clear operation.</param>
    /// <remarks>Clearing an already-missing witness is idempotent.</remarks>
    /// <exception cref="InvalidOperationException">The stored witness does not exactly match.</exception>
    ValueTask ClearAsync(JournalWitness witness, CancellationToken cancellationToken = default);
}
