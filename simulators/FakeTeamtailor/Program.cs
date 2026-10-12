using FakeTeamtailor;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Store>();
builder.Services.AddSingleton<CallLog>();
builder.Services.AddSingleton<ChaosRules>();
builder.Services.AddHttpClient<WebhookSender>();

var app = builder.Build();
app.UseCallLog();
app.MapApi();
app.MapSim();
app.MapCalls();
app.MapChaos();

app.Run();
