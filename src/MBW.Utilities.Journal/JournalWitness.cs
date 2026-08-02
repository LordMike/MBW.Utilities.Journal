using System.Collections.ObjectModel;

namespace MBW.Utilities.Journal;

/// <summary>
/// Describes the durable commit-phase witness for one coordinated journal transaction.
/// </summary>
public sealed class JournalWitness
{
    private readonly ulong[] _participantNonces;

    /// <summary>
    /// Creates a witness for a preparation key and the complete set of participating journal nonces.
    /// </summary>
    /// <param name="preparationKey">The non-empty preparation key shared by every participant.</param>
    /// <param name="participantNonces">The complete, non-empty set of unique journal header nonces.</param>
    /// <exception cref="ArgumentException">
    /// The preparation key is empty, or the participant set is empty or contains duplicate nonces.
    /// </exception>
    public JournalWitness(Guid preparationKey, IEnumerable<ulong> participantNonces)
    {
        ArgumentNullException.ThrowIfNull(participantNonces);
        if (preparationKey == Guid.Empty)
            throw new ArgumentException("The preparation key must not be empty", nameof(preparationKey));

        _participantNonces = participantNonces.ToArray();
        if (_participantNonces.Length == 0)
            throw new ArgumentException("At least one participant nonce is required", nameof(participantNonces));

        Array.Sort(_participantNonces);
        for (int i = 1; i < _participantNonces.Length; i++)
        {
            if (_participantNonces[i - 1] == _participantNonces[i])
                throw new ArgumentException("Participant nonces must be unique", nameof(participantNonces));
        }

        PreparationKey = preparationKey;
        ParticipantNonces = new ReadOnlyCollection<ulong>(_participantNonces);
    }

    /// <summary>
    /// Gets the preparation key shared by every participant.
    /// </summary>
    public Guid PreparationKey { get; }

    /// <summary>
    /// Gets the complete participant nonce set in ascending order.
    /// </summary>
    public IReadOnlyList<ulong> ParticipantNonces { get; }

    internal ReadOnlySpan<ulong> ParticipantNonceSpan => _participantNonces;

    internal bool ValueEquals(JournalWitness other) =>
        PreparationKey == other.PreparationKey && ParticipantNonceSpan.SequenceEqual(other.ParticipantNonceSpan);
}
