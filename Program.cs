//Program.cs
using LineraOrchestrator.Services;
using LineraOrchestrator.Models;

var builder = WebApplication.CreateBuilder(args);

//=========DI=============
builder.Services.AddSingleton<LineraCliRunner>();
builder.Services.AddScoped<LineraOrchestratorService>();
builder.Services.AddSingleton<LineraConfig>();
//=========DI=============

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();
app.MapGet("/health", () => new { status = "ok", message = "Linera Orchestrator is running" });

app.Run();
