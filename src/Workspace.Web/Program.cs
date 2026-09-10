using Microsoft.EntityFrameworkCore;
using Workspace.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddDbContext<WorkspaceDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Workspace")));

var app = builder.Build();

app.MapHealthChecks("/health");
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
