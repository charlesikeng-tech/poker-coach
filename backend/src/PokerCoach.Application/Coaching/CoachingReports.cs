namespace PokerCoach.Application.Coaching;

/// <summary>
/// A generated report (tournament debrief, weekly review) as stored: one per user, kind, subject and
/// language, replaced when generated again. <see cref="Fingerprint"/> says which inputs it was written from:
/// when the inputs have moved since, the report is shown as out of date and the player may regenerate it.
/// </summary>
public sealed record StoredReport<T>(T Report, string Fingerprint, string Model, DateTimeOffset CreatedAt);

/// <summary>A stored report and whether the facts it was written from have changed since.</summary>
public sealed record ReportView<T>(T Report, bool Stale, string Model, DateTimeOffset CreatedAt);

/// <summary>Outcome of a generation: the report, or why there is none.</summary>
public sealed record ReportOutcome<T>(ReportView<T>? View, CoachingFailure? Failure)
    where T : class
{
    public static ReportOutcome<T> Of(ReportView<T> view) => new(view, null);

    public static ReportOutcome<T> Fail(CoachingFailure failure) => new(null, failure);
}

public interface ICoachingReportStore
{
    /// <param name="kind">"tournament-debrief", "week-review".</param>
    /// <param name="subject">What the report is about: a tournament id, a week's Monday.</param>
    Task<StoredReport<T>?> FindAsync<T>(Guid userId, string kind, string subject, string language, CancellationToken cancellationToken);

    /// <summary>Creates or replaces the report for this user, kind, subject and language.</summary>
    Task SaveAsync<T>(Guid userId, string kind, string subject, string language, StoredReport<T> report, CancellationToken cancellationToken);

    /// <summary>The user's hands by id, ready to be told (<see cref="HandNarrator"/>); unknown ids are skipped.</summary>
    Task<IReadOnlyList<ExampleHand>> LoadHandsAsync(Guid userId, IReadOnlyList<Guid> handIds, CancellationToken cancellationToken);
}
