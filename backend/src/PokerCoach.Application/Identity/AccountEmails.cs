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
        string Button(string label, string href, bool primary) =>
            $"<a href=\"{WebUtility.HtmlEncode(href)}\" style=\"display:inline-block;margin:8px 8px 8px 0;padding:12px 20px;border-radius:10px;"
            + (primary ? "background:#e9b949;color:#1a1405;" : "background:#eef3ef;color:#10201a;")
            + $"font-weight:600;text-decoration:none\">{WebUtility.HtmlEncode(label)}</a>";
        var html = "<div style=\"font-family:system-ui,-apple-system,'Segoe UI',sans-serif;max-width:520px;color:#10201a;line-height:1.5\">"
            + $"<p style=\"font-size:18px;font-weight:600\">NutsIQ</p><p>{WebUtility.HtmlEncode(t.Intro)}</p><p>"
            + Button(t.Action, link, primary: true)
            + (secondary is { } b ? Button(b.Label, b.Link, primary: false) : string.Empty)
            + $"</p><p style=\"color:#5b6b63;font-size:13px\">{WebUtility.HtmlEncode(t.Outro)}</p></div>";
        return new EmailMessage(to, t.Subject, text, html);
    }
}
