using System.Text;
using System.Text.RegularExpressions;

namespace PokerCoach.Application.Coaching;

/// <summary>
/// Guards the text a model writes before it is stored and shown. Observed with Sonnet 5.5 in structured
/// outputs: French accents written as mojibake ("rÃ©guliers" for "réguliers", sometimes with Latin-2
/// look-alikes such as "Ă"). The transport is UTF-8 end to end; the garbling is in the generated text.
/// </summary>
public static partial class ModelText
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Windows-1252 characters for bytes 0x80–0x9F (the rest of 0xA0–0xFF is Latin-1 itself).</summary>
    private static readonly Dictionary<char, byte> Cp1252High = new()
    {
        ['€'] = 0x80,
        ['‚'] = 0x82,
        ['ƒ'] = 0x83,
        ['„'] = 0x84,
        ['…'] = 0x85,
        ['†'] = 0x86,
        ['‡'] = 0x87,
        ['ˆ'] = 0x88,
        ['‰'] = 0x89,
        ['Š'] = 0x8A,
        ['‹'] = 0x8B,
        ['Œ'] = 0x8C,
        ['Ž'] = 0x8E,
        ['‘'] = 0x91,
        ['’'] = 0x92,
        ['“'] = 0x93,
        ['”'] = 0x94,
        ['•'] = 0x95,
        ['–'] = 0x96,
        ['—'] = 0x97,
        ['˜'] = 0x98,
        ['™'] = 0x99,
        ['š'] = 0x9A,
        ['›'] = 0x9B,
        ['œ'] = 0x9C,
        ['ž'] = 0x9E,
        ['Ÿ'] = 0x9F,
    };

    /// <summary>
    /// Undoes UTF-8 text decoded as Windows-1252 ("Ã©" → "é"), run by run, only where the bytes form valid
    /// UTF-8: correct accents and ordinary text are left untouched.
    /// </summary>
    public static string Repair(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return MojibakeRun().Replace(text, match =>
        {
            var bytes = new byte[match.Length];
            for (var i = 0; i < match.Length; i++)
            {
                var c = match.Value[i];
                if (c is >= ' ' and <= 'ÿ')
                {
                    bytes[i] = (byte)c;
                }
                else if (Cp1252High.TryGetValue(c, out var b))
                {
                    bytes[i] = b;
                }
                else
                {
                    return match.Value;
                }
            }

            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return match.Value;
            }
        });
    }

    /// <summary>
    /// True when mojibake remains: a UTF-8 lead byte rendered as "Ã", "Â" or the Latin-2 "Ă", followed by a
    /// continuation look-alike. Never true for real French or Spanish ("Âge", "bâton", "año").
    /// </summary>
    public static bool IsGarbled(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Garbled().IsMatch(text);
    }

    [GeneratedRegex("[Â-ô](?:[\u0080-¿€‚ƒ„…†‡ˆ‰Š‹ŒŽ‘’“”•–—˜™š›œžŸ])+")]
    private static partial Regex MojibakeRun();

    [GeneratedRegex("[ÃÂĂ][\u0080-¿Ā-ſƒˆ-˝–-›€™]")]
    private static partial Regex Garbled();
}
