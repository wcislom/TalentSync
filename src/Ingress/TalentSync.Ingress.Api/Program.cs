using Microsoft.EntityFrameworkCore;
using TalentSync.Ingress.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);

var sqlConnectionString = builder.Configuration.GetConnectionString("Sql")
    ?? throw new InvalidOperationException("Missing configuration 'ConnectionStrings:Sql'.");

builder.Services.AddDbContext<IngressDbContext>(options => options.UseSqlServer(
    sqlConnectionString,
    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", IngressDbContext.Schema)));

var app = builder.Build();

app.Run();
