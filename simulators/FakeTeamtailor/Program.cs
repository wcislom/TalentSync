using FakeTeamtailor;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<Store>();

var app = builder.Build();
app.MapApi();

app.Run();
