using FakeTeamtailor.State;
using FakeTeamtailor.Teamtailor;
using FakeTeamtailor.Testing;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Store>();
builder.Services.AddSingleton<CallLog>();
builder.Services.AddSingleton<AnomalyRules>();
builder.Services.AddHttpClient<WebhookSender>();

var app = builder.Build();
app.UseCallLog();
app.MapApi();
app.MapRecruiter();
app.MapCalls();
app.MapAnomalies();

app.Run();
