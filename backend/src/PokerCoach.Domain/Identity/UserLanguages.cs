namespace PokerCoach.Domain.Identity;

/// <summary>Languages the product is available in. Stored as lowercase ISO 639-1 codes.</summary>
public static class UserLanguages
{
    public const string French = "fr";
    public const string English = "en";
    public const string Spanish = "es";

    /// <summary>Fallback when nothing better is known (spec §13).</summary>
    public const string Default = English;

    public static IReadOnlyList<string> All { get; } = [French, English, Spanish];

    public static bool IsSupported(string? language) => language is French or English or Spanish;
}
