namespace PokerCoach.Application.Notifications;

public sealed record ContactAttributes(
    string Email,
    string? Plan = null,
    DateTimeOffset? SignupDate = null,
    string? Language = null,
    int? TotalHands = null,
    string? Rooms = null,
    string? MainGameType = null);

public interface IContactSync
{
    Task SyncAsync(ContactAttributes contact, CancellationToken cancellationToken);
}
