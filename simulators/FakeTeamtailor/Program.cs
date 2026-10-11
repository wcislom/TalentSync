using FakeTeamtailor;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Store>();
builder.Services.AddHttpClient<WebhookSender>();

var app = builder.Build();
app.MapApi();
app.MapSim();

app.Run();
