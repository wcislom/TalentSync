using System.Text.Json.Serialization;
using FakeCrm.Crm;
using FakeCrm.State;
using FakeCrm.Testing;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Store>();
builder.Services.AddSingleton<CallLog>();
builder.Services.AddSingleton<AnomalyRules>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();
app.UseCallLog();
app.MapContacts();
app.MapContactList();
app.MapCalls();
app.MapAnomalies();

app.Run();
