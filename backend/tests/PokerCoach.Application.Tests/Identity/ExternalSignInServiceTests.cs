using Microsoft.Extensions.Time.Testing;
using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;

namespace PokerCoach.Application.Tests.Identity;

public sealed class ExternalSignInServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 20, 0, 0, TimeSpan.Zero);

    private readonly InMemoryUserAccountStore store = new();
    private readonly FakeTimeProvider clock = new(Now);
    private readonly ExternalSignInService service;

    public ExternalSignInServiceTests() => service = new ExternalSignInService(store, clock);

    [Fact]
    public async Task A_first_sign_in_creates_the_account_with_the_language_in_use()
    {
        var user = await service.SignInAsync(GoogleSignIn(language: "fr"), TestContext.Current.CancellationToken);

        Assert.Equal("Charles", user.DisplayName);
        Assert.Equal("charles@example.com", user.Email);
        Assert.Equal("fr", user.PreferredLanguage);
        Assert.Equal(Now, user.CreatedAt);
        Assert.Equal(Now, user.LastLoginAt);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Same(user, Assert.Single(store.Users));
    }

    [Fact]
    public async Task Signing_in_again_returns_the_same_account_and_records_the_login()
    {
        var first = await service.SignInAsync(GoogleSignIn(), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromDays(2));

        var second = await service.SignInAsync(
            GoogleSignIn(email: "new-address@example.com"),
            TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Single(store.Users);
        Assert.Equal(Now.AddDays(2), second.LastLoginAt);
        Assert.Equal(Now, second.CreatedAt);
        Assert.Equal("new-address@example.com", second.Email);
    }

    [Fact]
    public async Task The_same_email_from_another_subject_is_another_person()
    {
        var first = await service.SignInAsync(GoogleSignIn(subject: "111"), TestContext.Current.CancellationToken);
        var second = await service.SignInAsync(GoogleSignIn(subject: "222"), TestContext.Current.CancellationToken);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, store.Users.Count);
    }

    [Fact]
    public async Task Losing_a_race_against_a_parallel_first_sign_in_reuses_the_account_it_created()
    {
        var winner = User.Register("Charles", "charles@example.com", "en", Now);
        store.SimulateConcurrentFirstSignIn =
            (winner, ExternalIdentity.Link(winner, IdentityProviders.Google, "111", "charles@example.com", Now));

        var user = await service.SignInAsync(GoogleSignIn(subject: "111"), TestContext.Current.CancellationToken);

        Assert.Same(winner, user);
        Assert.Single(store.Users);
    }

    [Theory]
    [InlineData(null, "en")]
    [InlineData("de", "en")]
    [InlineData("es", "es")]
    public async Task An_unsupported_or_missing_language_falls_back_to_english(string? language, string expected)
    {
        var user = await service.SignInAsync(GoogleSignIn(language: language), TestContext.Current.CancellationToken);

        Assert.Equal(expected, user.PreferredLanguage);
    }

    [Theory]
    [InlineData(null, "charles@example.com", "charles")]
    [InlineData("  ", null, "Player")]
    public async Task A_missing_name_never_produces_an_empty_display_name(string? name, string? email, string expected)
    {
        var signIn = new ExternalSignIn(IdentityProviders.Google, "111", email, name, null);

        var user = await service.SignInAsync(signIn, TestContext.Current.CancellationToken);

        Assert.Equal(expected, user.DisplayName);
    }

    private static ExternalSignIn GoogleSignIn(
        string subject = "111",
        string? email = "charles@example.com",
        string? language = "en") =>
        new(IdentityProviders.Google, subject, email, "Charles", language);
}
