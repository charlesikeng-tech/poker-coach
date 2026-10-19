using System.Net;
using PokerCoach.Application.Identity;

namespace PokerCoach.Application.Notifications;

public static class WeeklyNotificationEmail
{
    private sealed record Texts(string Subject, string Intro, string Action, string Outro);

    private static readonly Dictionary<string, Texts> Plan = new()
    {
        ["fr"] = new(
            "Ton plan de la semaine est prêt",
            "Tes priorités pour cette semaine ont été préparées. Consulte-les pour travailler sur les bons aspects de ton jeu.",
            "Voir mon plan",
            "Bonne semaine.\n\nNutsIQ"),
        ["en"] = new(
            "Your weekly plan is ready",
            "Your priorities for this week have been prepared. Check them out to focus on the right aspects of your game.",
            "See my plan",
            "Good luck this week.\n\nNutsIQ"),
        ["es"] = new(
            "Tu plan semanal está listo",
            "Tus prioridades para esta semana están preparadas. Consúltalas para trabajar en los aspectos correctos de tu juego.",
            "Ver mi plan",
            "Buena semana.\n\nNutsIQ"),
    };

    public static EmailMessage WeeklyPlanReady(string to, string language, string link)
    {
        var t = Plan.TryGetValue(language, out var found) ? found : Plan["en"];
        var text = $"{t.Intro}\n\n{t.Action}: {link}\n\n{t.Outro}";
        var html = "<div style=\"font-family:system-ui,-apple-system,'Segoe UI',sans-serif;max-width:520px;color:#10201a;line-height:1.5\">"
            + $"<p style=\"font-size:18px;font-weight:600\">NutsIQ</p><p>{WebUtility.HtmlEncode(t.Intro)}</p><p>"
            + $"<a href=\"{WebUtility.HtmlEncode(link)}\" style=\"display:inline-block;margin:8px 0;padding:12px 20px;border-radius:10px;"
            + "background:#e9b949;color:#1a1405;font-weight:600;text-decoration:none\">"
            + $"{WebUtility.HtmlEncode(t.Action)}</a>"
            + $"</p><p style=\"color:#5b6b63;font-size:13px\">{WebUtility.HtmlEncode(t.Outro.Replace("\n\n", " — "))}</p></div>";
        return new EmailMessage(to, t.Subject, text, html);
    }
}
