using PokerCoach.Api.Errors;
using PokerCoach.Api.Health;
using PokerCoach.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddApiProblemDetails();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    // Exposed in Development only until authentication exists (Phase 1).
    app.MapOpenApi();
}

app.MapHealthEndpoints();

app.Run();

// Exposes the entry point to WebApplicationFactory in tests.
public partial class Program;
