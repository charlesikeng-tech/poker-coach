using System.Net;
using PokerCoach.Domain.Subscriptions;

namespace PokerCoach.Application.Identity;

/// <summary>
/// The invitation email, in the player's language. Same layout as <see cref="AccountEmails"/>: transactional,
/// no tracking pixel, no marketing.
/// </summary>
public static class InvitationEmails
{
    private sealed record Texts(string Subject, string Intro, string Action, string Outro);

    private static readonly Dictionary<string, Texts> Templates = new()
    {
        ["fr"] = new(
            "Tu es invité sur NutsIQ",
            "Tu as été invité à rejoindre NutsIQ avec un accès {0} offert. Crée ton compte pour commencer.",
            "Créer mon compte",
            "L'invitation est valable 7 jours. Tu n'as rien demandé ? Ignore ce message."),
        ["en"] = new(
            "You're invited to NutsIQ",
            "You've been invited to join NutsIQ with a free {0} plan. Create your account to get started.",
            "Create my account",
            "The invitation is valid for 7 days. Didn't request this? Ignore this message."),
        ["es"] = new(
            "Estás invitado a NutsIQ",
            "Has sido invitado a unirte a NutsIQ con un plan {0} gratuito. Crea tu cuenta para empezar.",
            "Crear mi cuenta",
            "La invitación es válida 7 días. ¿No lo has pedido? Ignora este mensaje."),
    };

    public static EmailMessage Invite(string to, string language, Plan plan, string signUpUrl)
    {
        var t = Templates.TryGetValue(language, out var found) ? found : Templates["en"];
        var planName = plan.ToString();
        var intro = string.Format(t.Intro, planName);
        return Compose(to, t.Subject, intro, t.Action, t.Outro, signUpUrl);
    }

    private static EmailMessage Compose(string to, string subject, string intro, string action, string outro, string link)
    {
        var text = $"{intro}\n\n{action}: {link}\n\n{outro}\n\nNutsIQ";
        var e = (Func<string, string>)WebUtility.HtmlEncode;

        var button = "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>"
            + "<td style=\"padding:4px 8px 4px 0\">"
            + $"<a href=\"{e(link)}\" style=\"display:inline-block;padding:14px 28px;"
            + "border-radius:8px;font-size:15px;font-weight:600;text-decoration:none;"
            + "background:#e9b949;color:#1a1405;"
            + $"\">{e(action)}</a></td></tr></table>";

        var html = "<!DOCTYPE html><html><head><meta charset=\"utf-8\">"
            + "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
            + "<meta name=\"color-scheme\" content=\"light dark\">"
            + "<meta name=\"supported-color-schemes\" content=\"light dark\">"
            + "<title>" + e(subject) + "</title></head>"
            + "<body style=\"margin:0;padding:0;background:#f4f4f0;-webkit-text-size-adjust:100%\">"
            + "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"100%\" "
            + "style=\"background:#f4f4f0\">"
            + "<tr><td align=\"center\" style=\"padding:32px 16px\">"
            + "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" "
            + "style=\"max-width:560px;width:100%\">"

            // Header
            + "<tr><td style=\"background:#1a1f1a;border-radius:12px 12px 0 0;padding:28px 32px;text-align:center\">"
            + "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" align=\"center\">"
            + "<tr>"
            + "<td style=\"color:#e9b949;font-family:Georgia,serif;font-size:13px;vertical-align:middle;padding-right:8px\">&#9824;</td>"
            + "<td style=\"font-family:system-ui,-apple-system,'Segoe UI',sans-serif;font-size:22px;font-weight:700;"
            + "color:#ffffff;letter-spacing:0.5px;vertical-align:middle\">NutsIQ</td>"
            + "<td style=\"color:#e9b949;font-family:Georgia,serif;font-size:13px;vertical-align:middle;padding-left:8px\">&#9830;</td>"
            + "</tr></table>"
            + "</td></tr>"

            // Body
            + "<tr><td style=\"background:#ffffff;padding:32px 32px 24px\">"
            + "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"100%\">"
            + "<tr><td style=\"font-family:system-ui,-apple-system,'Segoe UI',sans-serif;font-size:15px;"
            + "color:#2a2a2a;line-height:1.6;padding-bottom:24px\">"
            + e(intro) + "</td></tr>"
            + "<tr><td style=\"padding-bottom:24px\">" + button + "</td></tr>"
            + "<tr><td style=\"border-top:1px solid #e8e8e4;padding-top:20px;font-family:system-ui,-apple-system,'Segoe UI',sans-serif;"
            + "font-size:13px;color:#8a8a82;line-height:1.5\">"
            + e(outro) + "</td></tr>"
            + "</table></td></tr>"

            // Footer
            + "<tr><td style=\"background:#1a1f1a;border-radius:0 0 12px 12px;padding:20px 32px;text-align:center;"
            + "font-family:system-ui,-apple-system,'Segoe UI',sans-serif;font-size:12px;color:#6b7b6e\">"
            + "NutsIQ &mdash; Analyse tes mains. Corrige tes leaks."
            + "</td></tr>"

            + "</table></td></tr></table></body></html>";

        return new EmailMessage(to, subject, text, html);
    }
}
