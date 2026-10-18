namespace PokerCoach.Domain.Identity;

/// <summary>
/// A read-only access token the student generates and shares with their coach.
/// The coach presents the <see cref="Id"/> as a token; the server resolves the student.
/// </summary>
public sealed class CoachInvitation
{
    private CoachInvitation() { }

    public Guid Id { get; private set; }

    public Guid StudentId { get; private set; }

    /// <summary>Human label set by the student ("Mon coach Dupont") to manage multiple invitations.</summary>
    public string Label { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Null means no expiry.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>Non-null once the student has revoked this invitation.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive(DateTimeOffset now) =>
        RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);

    public const int MaxLabelLength = 100;

    public static CoachInvitation Create(Guid studentId, string label, DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        var trimmed = label?.Trim() ?? string.Empty;
        ArgumentException.ThrowIfNullOrEmpty(trimmed);
        if (trimmed.Length > MaxLabelLength)
        {
            throw new ArgumentOutOfRangeException(nameof(label), $"Label must be at most {MaxLabelLength} characters.");
        }

        return new CoachInvitation
        {
            Id = Guid.CreateVersion7(),
            StudentId = studentId,
            Label = trimmed,
            ExpiresAt = expiresAt,
            CreatedAt = now,
        };
    }

    public void Revoke(DateTimeOffset now) => RevokedAt = now;
}
