using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PokerCoach.Api.Account;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
using PokerCoach.Api.Health;
using PokerCoach.Api.Import;
using PokerCoach.Api.Poker;
using PokerCoach.Api.Tournaments;
using PokerCoach.Application.Identity;
using PokerCoach.Application.Import;
using PokerCoach.Application.Poker;
using PokerCoach.Application.Tournaments;
using PokerCoach.HandHistories;
using PokerCoach.HandHistories.Winamax;
using PokerCoach.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
// Enums travel as camelCase strings ("pending", "winamax"): readable and stable when members are added.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddApiProblemDetails();
builder.Services.AddApiAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ExternalSignInService>();
builder.Services.AddScoped<UserProfileService>();

builder.Services.AddOptions<ImportOptions>()
    .BindConfiguration(ImportOptions.SectionName)
    .Validate(
        o => o.MaxFileBytes > 0 && o.MaxFilesPerUpload > 0 && o.MaxUploadBytes > 0 && o.MaxExpandedBytes > 0 && o.MaxProcessingAttempts > 0,
        "Import limits must be positive.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<ImportOptions>>().Value);
builder.Services.AddSingleton<IHandHistoryProvider, WinamaxHandHistoryProvider>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<ImportProcessor>();
builder.Services.AddScoped<PokerAccountService>();
builder.Services.AddScoped<TournamentListService>();
builder.Services.AddScoped<PerformanceService>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();

// The fallback authorization policy also applies to requests matching no endpoint: answer 404, not 401.
// Placed after UseAuthentication on purpose: the Google callback (/signin-google) has no endpoint and is
// handled by the authentication middleware itself.
app.Use(async (context, next) =>
{
    if (context.GetEndpoint() is null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next(context);
});

app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapImportEndpoints();
app.MapPokerAccountEndpoints();
app.MapTournamentEndpoints();

app.Run();

// Exposes the entry point to WebApplicationFactory in tests.
public partial class Program;
