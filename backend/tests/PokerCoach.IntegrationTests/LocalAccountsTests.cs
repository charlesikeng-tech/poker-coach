using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using PokerCoach.Application.Identity;
using PokerCoach.Domain.Identity;

namespace PokerCoach.IntegrationTests;

/// <summary>Email and password accounts on PostgreSQL (ADR-0013): unique login, single-use links, real hashing.</summary>
public sealed partial class LocalAccountsTests(PostgresFixture fixture)
{
    private const string Password = "a long enough password";
    private static readonly AccountLinks Links = new("https://poker-coach.test");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sign_up_confirm_and_sign_in_with_a_real_hash()
    {
        var address = $"{Guid.NewGuid():N}@example.com";
        var sent = new List<EmailMessage>();

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            Assert.Equal(RegistrationResult.Accepted, await Service(scope, sent).RegisterAsync(address, Password, "Jo", "fr", Links, Ct));
        }

        var token = TokenIn(Assert.Single(sent));
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var confirmed = await Service(scope, sent).ConfirmEmailAsync(token, Password, Ct);
            Assert.Equal("Jo", confirmed.User?.DisplayName);
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var service = Service(scope, sent);
            Assert.Equal(LocalSignInFailure.InvalidToken, (await service.ConfirmEmailAsync(token, Password, Ct)).Failure);
            Assert.NotNull((await service.SignInAsync(address.ToUpperInvariant(), Password, Ct)).User);
            Assert.Equal(LocalSignInFailure.InvalidCredentials, (await service.SignInAsync(address, "not the password", Ct)).Failure);
        }
    }

    [Fact]
    public async Task One_login_per_email_and_one_use_per_link_even_under_concurrency()
    {
        var address = $"{Guid.NewGuid():N}@example.com";
        var now = DateTimeOffset.UtcNow;
        Guid userId;

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ILocalAccountStore>();
            var user = User.Register("Jo", address, "en", now);
            Assert.True(await store.TryAddAsync(user, PasswordCredential.Create(user, address, "hash", now), Ct));
            userId = user.Id;
            await store.AddTokenAsync(EmailToken.Issue(userId, EmailTokenPurpose.ResetPassword, new string('c', 64), now), Ct);
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ILocalAccountStore>();
            var twin = User.Register("Jo bis", address, "en", now);
            Assert.False(await store.TryAddAsync(twin, PasswordCredential.Create(twin, address, "hash", now), Ct));

            var token = await store.FindUsableTokenAsync(new string('c', 64), EmailTokenPurpose.ResetPassword, now, Ct);
            Assert.Equal(userId, token?.UserId);
            Assert.Null(await store.FindUsableTokenAsync(new string('c', 64), EmailTokenPurpose.ConfirmEmail, now, Ct));
            Assert.Equal(1, await store.CountTokensSinceAsync(userId, EmailTokenPurpose.ResetPassword, now.AddMinutes(-1), Ct));

            var uses = await Task.WhenAll(
                UseAsync(token!.Value.TokenId, now),
                UseAsync(token.Value.TokenId, now));
            Assert.Equal(1, uses.Count(used => used));
            Assert.Null(await store.FindUsableTokenAsync(new string('c', 64), EmailTokenPurpose.ResetPassword, now, Ct));
        }
    }

    private async Task<bool> UseAsync(Guid tokenId, DateTimeOffset now)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ILocalAccountStore>().TryConsumeTokenAsync(tokenId, now, Ct);
    }

    private static LocalAccountService Service(AsyncServiceScope scope, List<EmailMessage> sent) =>
        new(
            scope.ServiceProvider.GetRequiredService<ILocalAccountStore>(),
            scope.ServiceProvider.GetRequiredService<IPasswordHasher>(),
            new CapturingSender(sent),
            TimeProvider.System);

    private static string TokenIn(EmailMessage message) => Uri.UnescapeDataString(TokenPattern().Match(message.Text).Groups[1].Value);

    [GeneratedRegex(@"token=([^\s&]+)")]
    private static partial Regex TokenPattern();

    private sealed class CapturingSender(List<EmailMessage> sent) : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
