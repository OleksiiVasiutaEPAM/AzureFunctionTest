using Epam.TestFunction.Options;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.AddHttpClient();

// Опции для OpenAI
builder.Services.AddOptions<OpenAIOptions>()
    .Configure<IConfiguration>((opt, cfg) =>
    {
        opt.ApiKey = cfg["OPENAI_API_KEY"] ?? "";
        opt.Model  = cfg["OPENAI_MODEL"]    ?? "gpt-4o-mini";
    });

// ServiceBusClient для отправки сообщений
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var conn = cfg["ServiceBusConnection"] 
               ?? throw new InvalidOperationException("ServiceBusConnection is missing");
    return new Azure.Messaging.ServiceBus.ServiceBusClient(conn);
});

// Никаких MapGet/MapPost здесь — это не WebApplication
builder.Build().Run();