using System.Text.Json.Serialization;
using FakeMatchingPlatform.MatchingPlatform;
using FakeMatchingPlatform.State;
using FakeMatchingPlatform.Testing;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Store>();
builder.Services.AddSingleton<CallLog>();
builder.Services.AddSingleton<AnomalyRules>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The Admin app calls the simulators from the browser. UseCors comes first so a preflight never reaches the call log.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
app.UseCallLog();
app.MapProfiles();
app.MapProfileList();
app.MapCalls();
app.MapAnomalies();

app.Run();
