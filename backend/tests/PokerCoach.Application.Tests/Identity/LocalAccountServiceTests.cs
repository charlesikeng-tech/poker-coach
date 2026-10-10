using System.Text.RegularExpressions;
using Microsoft.Extensions.Time.Testing;
using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;

namespace PokerCoach.Application.Tests.Identity;

public sealed partial class LocalAccountServiceTests
{
    private const string Password = "a long enough password";
    private static readonly AccountLinks Links = new("https://poker-coach.test");

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
    private readonly InMemoryUserAccountStore users = new();
    private readonly InMemoryLocalAccountStore accounts;
    private readonly FakeHasher hasher = new();
    private readonly SentEmails emails = new();

    public LocalAccountServiceTests() => accounts = new InMemoryLocalAccountStore(users);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sign_up_needs_the_emailed_link_and_the_password_before_signing_in()
    {
        Assert.Equal(RegistrationResult.Accepted, await Service().RegisterAsync(" Jo@Example.com ", Password, null, "fr", Links, Ct));

        var credential = Assert.Single(accounts.Credentials);
        Assert.Equal("jo@example.com", credential.Email);
        Assert.Equal("jo", Assert.Single(users.Users).DisplayName);
        Assert.Equal(LocalSignInFailure.EmailNotConfirmed, (await Service().SignInAsync("jo@example.com", Password, Ct)).Failure);

        var mail = Assert.Single(emails.Sent);
        Assert.Equal("Confirme ton adresse Poker Coach", mail.Subject);
        var token = TokenIn(mail);

        Assert.Equal(LocalSignInFailure.InvalidCredentials, (await Service().ConfirmEmailAsync(token, "someone else's guess", Ct)).Failure);
        Assert.NotNull((await Service().ConfirmEmailAsync(token, Password, Ct)).User);
        Assert.Equal(LocalSignInFailure.InvalidToken, (await Service().ConfirmEmailAsync(token, Password, Ct)).Failure);
        Assert.NotNull((await Service().SignInAsync("JO@example.com", Password, Ct)).User);
    }

    [Fact]
    public async Task An_email_that_already_has_an_account_gets_a_notice_not_a_second_account()
    {
        var google = User.Register("Jo", "Jo@Example.com", "en", time.GetUtcNow());
        users.AddUser(google);

        Assert.Equal(RegistrationResult.Accepted, await Service().RegisterAsync("jo@example.com", Password, null, "fr", Links, Ct));

        Assert.Empty(accounts.Credentials);
        Assert.Single(users.Users);
        Assert.Equal("You already have a Poker Coach account", Assert.Single(emails.Sent).Subject);
    }

    [Fact]
    public async Task Signing_up_again_before_confirming_replaces_the_password_and_voids_the_old_link()
    {
        await Service().RegisterAsync("jo@example.com", "first password here", null, "en", Links, Ct);
        var firstLink = TokenIn(emails.Sent[^1]);
        await Service().RegisterAsync("jo@example.com", Password, null, "en", Links, Ct);

        Assert.Equal(LocalSignInFailure.InvalidToken, (await Service().ConfirmEmailAsync(firstLink, "first password here", Ct)).Failure);
        Assert.NotNull((await Service().ConfirmEmailAsync(TokenIn(emails.Sent[^1]), Password, Ct)).User);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_for_a_while()
    {
        await RegisterConfirmedAsync("jo@example.com");

        for (var i = 0; i < PasswordCredential.MaxFailedAttempts - 1; i++)
        {
            Assert.Equal(LocalSignInFailure.InvalidCredentials, (await Service().SignInAsync("jo@example.com", "wrong password!", Ct)).Failure);
        }

        Assert.Equal(LocalSignInFailure.Locked, (await Service().SignInAsync("jo@example.com", "wrong password!", Ct)).Failure);
        Assert.Equal(LocalSignInFailure.Locked, (await Service().SignInAsync("jo@example.com", Password, Ct)).Failure);

        time.Advance(PasswordCredential.LockoutDuration);
        Assert.NotNull((await Service().SignInAsync("jo@example.com", Password, Ct)).User);
    }

    [Fact]
    public async Task An_unknown_email_costs_a_password_check_like_a_known_one()
    {
        var outcome = await Service().SignInAsync("nobody@example.com", Password, Ct);

        Assert.Equal(LocalSignInFailure.InvalidCredentials, outcome.Failure);
        Assert.Equal(1, hasher.Verifications);
    }

    [Fact]
    public async Task A_google_account_adds_a_password_through_the_forgotten_password_link()
    {
        var google = User.Register("Jo", "jo@example.com", "es", time.GetUtcNow());
        users.AddUser(google);

        await Service().ForgotPasswordAsync("jo@example.com", "fr", Links, Ct);
        var mail = Assert.Single(emails.Sent);
        Assert.Equal("Tu contraseña de Poker Coach", mail.Subject);

        var outcome = await Service().ResetPasswordAsync(TokenIn(mail), Password, Ct);

        Assert.Equal(google.Id, outcome.User?.Id);
        Assert.True(Assert.Single(accounts.Credentials).IsConfirmed);
        Assert.Equal(google.Id, (await Service().SignInAsync("jo@example.com", Password, Ct)).User?.Id);
        Assert.Equal(LocalSignInFailure.InvalidToken, (await Service().ResetPasswordAsync(TokenIn(mail), Password, Ct)).Failure);
    }

    [Fact]
    public async Task Answers_and_limits_never_reveal_or_flood_an_inbox()
    {
        await Service().ForgotPasswordAsync("nobody@example.com", "en", Links, Ct);
        await Service().ResendConfirmationAsync("nobody@example.com", "en", Links, Ct);
        Assert.Empty(emails.Sent);

        await RegisterConfirmedAsync("jo@example.com");
        emails.Sent.Clear();
        for (var i = 0; i < 5; i++)
        {
            await Service().ForgotPasswordAsync("jo@example.com", "en", Links, Ct);
        }

        Assert.Equal(LocalAccountService.MaxEmailsPerHour, emails.Sent.Count);
    }

    [Theory]
    [InlineData("not-an-email", Password, RegistrationResult.InvalidEmail)]
    [InlineData("Jo <jo@example.com>", Password, RegistrationResult.InvalidEmail)]
    [InlineData("jo@localhost", Password, RegistrationResult.InvalidEmail)]
    [InlineData("jo@example.com", "short", RegistrationResult.WeakPassword)]
    [InlineData("jo@example.com", "jo@example.com", RegistrationResult.WeakPassword)]
    [InlineData("jo@example.com", "aaaaaaaaaaaa", RegistrationResult.WeakPassword)]
    public async Task Invalid_emails_and_weak_passwords_are_refused(string address, string password, RegistrationResult expected)
    {
        Assert.Equal(expected, await Service().RegisterAsync(address, password, null, "en", Links, Ct));
        Assert.Empty(emails.Sent);
    }

    [Fact]
    public async Task Google_joins_a_confirmed_email_account_but_not_an_unconfirmed_one()
    {
        var confirmed = await RegisterConfirmedAsync("jo@example.com");
        await Service().RegisterAsync("max@example.com", Password, null, "en", Links, Ct);
        var signIns = new ExternalSignInService(users, time, accounts);

        var jo = await signIns.SignInAsync(new ExternalSignIn(IdentityProviders.Google, "g-jo", "Jo@Example.com", "Jo", "en"), Ct);
        var max = await signIns.SignInAsync(new ExternalSignIn(IdentityProviders.Google, "g-max", "max@example.com", "Max", "en"), Ct);

        Assert.Equal(confirmed.Id, jo.Id);
        Assert.NotEqual(accounts.Credentials.Single(c => c.Email == "max@example.com").UserId, max.Id);
    }

    private async Task<User> RegisterConfirmedAsync(string address)
    {
        await Service().RegisterAsync(address, Password, null, "en", Links, Ct);
        return (await Service().ConfirmEmailAsync(TokenIn(emails.Sent[^1]), Password, Ct)).User!;
    }

    private LocalAccountService Service() => new(accounts, hasher, emails, time);

    private static string TokenIn(EmailMessage message) => Uri.UnescapeDataString(TokenPattern().Match(message.Text).Groups[1].Value);

    [GeneratedRegex(@"token=([^\s&]+)")]
    private static partial Regex TokenPattern();

    private sealed class FakeHasher : IPasswordHasher
    {
        public int Verifications { get; private set; }

        public string Hash(string password) => "hash:" + password;

        public PasswordCheck Verify(string hash, string password)
        {
            Verifications++;
            return hash == "hash:" + password ? PasswordCheck.Success : PasswordCheck.Failed;
        }
    }

    private sealed class SentEmails : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
