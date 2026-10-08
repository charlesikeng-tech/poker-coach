using PokerCoach.Api.Account;
using PokerCoach.Api.Authentication;
using PokerCoach.Api.Errors;
using PokerCoach.Api.Health;
using PokerCoach.Application.Identity;
using PokerCoach.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddApiProblemDetails();
builder.Services.AddApiAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ExternalSignInService>();
builder.Services.AddScoped<UserProfileService>();

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

app.Run();

// Exposes the entry point to WebApplicationFactory in tests.
public partial class Program;
