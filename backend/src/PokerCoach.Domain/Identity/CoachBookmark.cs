namespace PokerCoach.Domain.Identity;

/// <summary>
/// A coach's saved link to a student via an invitation token. Lets the coach build
/// a persistent list of students they follow without re-entering tokens.
/// </summary>
public sealed class CoachBookmark
{
    public const int MaxNotesLength = 500;

    private CoachBookmark() { }

    public Guid Id { get; private set; }

    public Guid CoachUserId { get; private set; }

    public Guid InvitationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string? Notes { get; private set; }

    public void UpdateNotes(string? notes)
    {
        if (notes is not null && notes.Length > MaxNotesLength)
            throw new ArgumentException($"Notes must not exceed {MaxNotesLength} characters.");
        Notes = notes;
    }

    public static CoachBookmark Create(Guid coachUserId, Guid invitationId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            CoachUserId = coachUserId,
            InvitationId = invitationId,
            CreatedAt = now,
        };
}
