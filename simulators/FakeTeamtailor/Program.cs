using FakeTeamtailor.State;
using FakeTeamtailor.Teamtailor;
using FakeTeamtailor.Testing;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Store>();
builder.Services.AddSingleton<CallLog>();
builder.Services.AddSingleton<AnomalyRules>();
builder.Services.AddHttpClient<WebhookSender>();

// The Admin app calls the simulators from the browser. UseCors comes first so a preflight never reaches the call log.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
app.UseCallLog();
app.MapApi();
app.MapRecruiter();
app.MapCalls();
app.MapAnomalies();

app.Run();
