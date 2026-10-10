using PokerCoach.Application.Coaching;

namespace PokerCoach.Application.Tests.Coaching;

public sealed class ModelTextTests
{
    [Theory]
    [InlineData("les rÃ©guliers solides", "les réguliers solides")]
    [InlineData("ces pots coÃ»tent cher", "ces pots coûtent cher")]
    [InlineData("Le leak est confirmÃ© : Ã§a coÃ»te", "Le leak est confirmé : ça coûte")]
    [InlineData("dÃ©jÃ\u00A0 vu", "déjà vu")]
    public void Utf8_read_as_windows_1252_is_repaired(string garbled, string expected)
    {
        var repaired = ModelText.Repair(garbled);

        Assert.Equal(expected, repaired);
        Assert.False(ModelText.IsGarbled(repaired));
    }

    [Theory]
    [InlineData("Âge, bâton, tôt, préfère, coûtent, ça, où, naïf")]
    [InlineData("En el año, el bluff más común: tú decides – 35 %–52 %")]
    [InlineData("C-bet, 3-bet, check-raise : « ok » … 24 %–33 %")]
    public void Correct_french_and_spanish_are_left_alone(string text)
    {
        Assert.Equal(text, ModelText.Repair(text));
        Assert.False(ModelText.IsGarbled(text));
    }

    [Fact]
    public void Look_alikes_that_do_not_map_back_stay_garbled()
    {
        // Observed: Latin-2 look-alikes mixed in ("tôt" written "tĂŽt"); they cannot be repaired faithfully.
        Assert.True(ModelText.IsGarbled(ModelText.Repair("plutĂŽt que de payer")));
    }
}
