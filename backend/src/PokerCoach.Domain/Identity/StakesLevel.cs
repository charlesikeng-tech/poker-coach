namespace PokerCoach.Domain.Identity;

/// <summary>The stakes the player usually plays at; selects which reference ranges to compare against.</summary>
public enum StakesLevel
{
    /// <summary>MTT 5–20 €, Cash NL10–NL25.</summary>
    Low = 0,

    /// <summary>MTT 50–100 €, Cash NL50–NL100.</summary>
    Mid = 1,

    /// <summary>MTT 200 €+, Cash NL200+.</summary>
    High = 2,
}
