using System.Net;

namespace PokerCoach.Application.Identity;

/// <summary>
/// The account emails, in the player's language. Plain and short: transactional emails, no tracking pixel,
/// no marketing. The links expire (24 h to confirm, 1 h to reset) and work once.
/// </summary>
public static class AccountEmails
{
    private sealed record Texts(string Subject, string Intro, string Action, string Outro);

    private static readonly Dictionary<string, Texts> Confirm = new()
    {
        ["fr"] = new("Confirme ton adresse NutsIQ", "Bienvenue sur NutsIQ ! Confirme ton adresse pour activer ton compte.", "Confirmer mon adresse", "Le lien est valable 24 heures. Tu n'as pas créé de compte ? Ignore ce message."),
        ["en"] = new("Confirm your NutsIQ address", "Welcome to NutsIQ! Confirm your address to activate your account.", "Confirm my address", "The link is valid for 24 hours. Didn't create an account? Ignore this message."),
        ["es"] = new("Confirma tu dirección de NutsIQ", "¡Bienvenido a NutsIQ! Confirma tu dirección para activar tu cuenta.", "Confirmar mi dirección", "El enlace es válido 24 horas. ¿No has creado una cuenta? Ignora este mensaje."),
    };

    private static readonly Dictionary<string, Texts> Reset = new()
    {
        ["fr"] = new("Ton mot de passe NutsIQ", "Tu as demandé à choisir un nouveau mot de passe.", "Choisir un mot de passe", "Le lien est valable 1 heure et ne sert qu'une fois. Ce n'est pas toi ? Ignore ce message : rien ne change."),
        ["en"] = new("Your NutsIQ password", "You asked to choose a new password.", "Choose a password", "The link is valid for 1 hour and works once. Not you? Ignore this message: nothing changes."),
        ["es"] = new("Tu contraseña de NutsIQ", "Has pedido elegir una contraseña nueva.", "Elegir una contraseña", "El enlace es válido 1 hora y solo funciona una vez. ¿No has sido tú? Ignora este mensaje: no cambia nada."),
    };

    private static readonly Dictionary<string, (Texts Texts, string SignIn)> Existing = new()
    {
        ["fr"] = (new("Tu as déjà un compte NutsIQ", "Quelqu'un (sans doute toi) a voulu créer un compte avec cette adresse, mais elle en a déjà un. Connecte-toi, ou choisis un mot de passe si tu te connectais avec Google.", "Choisir un mot de passe", "Ce n'est pas toi ? Ignore ce message : ton compte ne change pas."), "Se connecter"),
        ["en"] = (new("You already have a NutsIQ account", "Someone (probably you) tried to create an account with this address, but it already has one. Sign in, or choose a password if you used to sign in with Google.", "Choose a password", "Not you? Ignore this message: your account does not change."), "Sign in"),
        ["es"] = (new("Ya tienes una cuenta de NutsIQ", "Alguien (seguramente tú) ha intentado crear una cuenta con esta dirección, pero ya tiene una. Inicia sesión, o elige una contraseña si entrabas con Google.", "Elegir una contraseña", "¿No has sido tú? Ignora este mensaje: tu cuenta no cambia."), "Iniciar sesión"),
    };

    public static EmailMessage ConfirmEmail(string to, string language, string link) => Compose(to, Pick(Confirm, language), link, null);

    public static EmailMessage ResetPassword(string to, string language, string link) => Compose(to, Pick(Reset, language), link, null);

    public static EmailMessage AlreadyRegistered(string to, string language, string signInLink, string resetLink)
    {
        var (texts, signIn) = Pick(Existing, language);
        return Compose(to, texts, resetLink, (signIn, signInLink));
    }

    private static T Pick<T>(Dictionary<string, T> texts, string language) =>
        texts.TryGetValue(language, out var found) ? found : texts["en"];

    private static EmailMessage Compose(string to, Texts t, string link, (string Label, string Link)? secondary)
    {
        var text = $"{t.Intro}\n\n{t.Action}: {link}\n"
            + (secondary is { } s ? $"{s.Label}: {s.Link}\n" : string.Empty)
            + $"\n{t.Outro}\n\nNutsIQ";

        var e = (Func<string, string>)WebUtility.HtmlEncode;

        string Button(string label, string href, bool primary) =>
            "<td style=\"padding:4px 8px 4px 0\">"
            + $"<a href=\"{e(href)}\" style=\"display:inline-block;padding:14px 28px;"
            + "border-radius:8px;font-size:15px;font-weight:600;text-decoration:none;"
            + (primary
                ? "background:#e9b949;color:#1a1405;"
                : "background:transparent;color:#e9b949;border:1.5px solid #e9b949;")
            + $"\">{e(label)}</a></td>";

        var buttons = "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>"
            + Button(t.Action, link, primary: true)
            + (secondary is { } b ? Button(b.Label, b.Link, primary: false) : string.Empty)
            + "</tr></table>";

        var html = "<!DOCTYPE html><html><head><meta charset=\"utf-8\">"
            + "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
            + "<meta name=\"color-scheme\" content=\"light dark\">"
            + "<meta name=\"supported-color-schemes\" content=\"light dark\">"
            + "<title>" + e(t.Subject) + "</title></head>"
            + "<body style=\"margin:0;padding:0;background:#f4f4f0;-webkit-text-size-adjust:100%\">"
            + "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"100%\" "
            + "style=\"background:#f4f4f0\">"
            + "<tr><td align=\"center\" style=\"padding:32px 16px\">"

            // Container
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
            + e(t.Intro) + "</td></tr>"
            + "<tr><td style=\"padding-bottom:24px\">" + buttons + "</td></tr>"
            + "<tr><td style=\"border-top:1px solid #e8e8e4;padding-top:20px;font-family:system-ui,-apple-system,'Segoe UI',sans-serif;"
            + "font-size:13px;color:#8a8a82;line-height:1.5\">"
            + e(t.Outro) + "</td></tr>"
            + "</table></td></tr>"

            // Footer
            + "<tr><td style=\"background:#1a1f1a;border-radius:0 0 12px 12px;padding:20px 32px;text-align:center;"
            + "font-family:system-ui,-apple-system,'Segoe UI',sans-serif;font-size:12px;color:#6b7b6e\">"
            + "NutsIQ &mdash; Analyse tes mains. Corrige tes leaks."
            + "</td></tr>"

            + "</table></td></tr></table></body></html>";

        return new EmailMessage(to, t.Subject, text, html);
    }
}
