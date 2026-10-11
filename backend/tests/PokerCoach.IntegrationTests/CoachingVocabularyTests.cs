using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PokerCoach.Application.Coaching;
using PokerCoach.Infrastructure.Coaching;

namespace PokerCoach.IntegrationTests;

public sealed class CoachingVocabularyTests
{
    [Theory]
    [InlineData("Cette fuite apparaît surtout en 3-bet pot : garde ta range.")]
    [InlineData("El farol funciona mejor en esta situación de all-in.")]
    [InlineData("La mise de continuation ne doit pas être automatique.")]
    [InlineData("La resubida a été trop large.")]
    [InlineData("Une mise de valeur trop fine peut perdre contre une range plus forte.")]
    [InlineData("La gran ciega doit défendre plus souvent ce spot.")]
    [InlineData("Le joueur est hors position au turn.")]
    public void Detects_translated_poker_vocabulary(string text)
    {
        Assert.True(AnthropicCoachingModel.ContainsTranslatedPokerTerm(text));
    }

    [Theory]
    [InlineData("Cette leak apparaît surtout après le flop.")]
    [InlineData("El bluff funciona mejor en esta situación.")]
    [InlineData("Le joueur est en position au turn.")]
    [InlineData("El jugador está en posición en el turn.")]
    [InlineData("This is a sufficiently long answer without poker vocabulary translations.")]
    public void Allows_english_poker_terms(string text)
    {
        Assert.False(AnthropicCoachingModel.ContainsTranslatedPokerTerm(text));
    }

    [Fact]
    public async Task Retries_once_with_a_correction_prompt_and_counts_both_calls()
    {
        var handler = new SequenceHandler(
            """{"summary":"plutĂŽt que de payer","why_it_costs":"Cela coûte des jetons.","actions":[],"hands":[]}""",
            """{"summary":"Le leak est confirmé.","why_it_costs":"Cela coûte des jetons.","actions":[],"hands":[]}""");
        using var http = new HttpClient(handler);
        var model = new AnthropicCoachingModel(
            http,
            Options.Create(new AnthropicOptions { ApiKey = "test", BaseUrl = new Uri("https://example.test/") }),
            NullLogger<AnthropicCoachingModel>.Instance);

        var result = await model.ExplainAsync(new ExplanationPrompt("fr", "facts", []), CancellationToken.None);

        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Contains("previous answer was rejected", handler.RequestBodies[1], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Le leak est confirmé.", result.Explanation.Summary);
        Assert.Equal(20, result.Usage.InputTokens);
        Assert.Equal(10, result.Usage.OutputTokens);
    }

    [Fact]
    public async Task Correction_prompt_identifies_the_translated_term_to_replace()
    {
        var handler = new SequenceHandler(
            """{"summary":"Cette fuite coûte des jetons.","why_it_costs":"Cela coûte des jetons.","actions":[],"hands":[]}""",
            """{"summary":"Ce leak coûte des jetons.","why_it_costs":"Cela coûte des jetons.","actions":[],"hands":[]}""");
        using var http = new HttpClient(handler);
        var model = new AnthropicCoachingModel(
            http,
            Options.Create(new AnthropicOptions { ApiKey = "test", BaseUrl = new Uri("https://example.test/") }),
            NullLogger<AnthropicCoachingModel>.Instance);

        var result = await model.ExplainAsync(new ExplanationPrompt("fr", "facts", []), CancellationToken.None);

        Assert.Contains("fuite", handler.RequestBodies[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not repeat any detected translated term", handler.RequestBodies[1], StringComparison.Ordinal);
        Assert.Equal("Ce leak coûte des jetons.", result.Explanation.Summary);
    }

    [Fact]
    public async Task Throws_after_the_single_correction_attempt_is_rejected()
    {
        const string invalidAnswer = """{"summary":"plutĂŽt que de payer","why_it_costs":"Cela coûte des jetons.","actions":[],"hands":[]}""";
        var handler = new SequenceHandler(invalidAnswer, invalidAnswer);
        using var http = new HttpClient(handler);
        var model = new AnthropicCoachingModel(
            http,
            Options.Create(new AnthropicOptions { ApiKey = "test", BaseUrl = new Uri("https://example.test/") }),
            NullLogger<AnthropicCoachingModel>.Instance);

        var exception = await Assert.ThrowsAsync<CoachingModelException>(() =>
            model.ExplainAsync(new ExplanationPrompt("fr", "facts", []), CancellationToken.None));

        Assert.Contains("garbled characters", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, handler.RequestBodies.Count);
    }

    private sealed class SequenceHandler(params string[] answers) : HttpMessageHandler
    {
        private int _nextAnswer;

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            var answer = answers[_nextAnswer++];
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    model = "test-model",
                    stop_reason = "end_turn",
                    content = new[] { new { type = "text", text = answer } },
                    usage = new { input_tokens = 10, output_tokens = 5, cache_creation_input_tokens = 0, cache_read_input_tokens = 0 },
                }),
            };
        }
    }
}
