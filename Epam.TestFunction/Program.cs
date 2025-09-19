using Azure.Identity;
using Azure.Storage.Blobs;
using Epam.TestFunction.Options;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.AddHttpClient();

builder.Services.AddOptions<OpenAIOptions>()
    .Configure<IConfiguration>((opt, cfg) =>
    {
        opt.ApiKey = cfg["OPENAI_API_KEY"] ?? "";
        opt.Model  = cfg["OPENAI_MODEL"]    ?? "gpt-4o-mini";
    });

builder.Services.AddOptions<DialAIOptions>()
    .Configure<IConfiguration>((opt, cfg) =>
    {
        opt.ApiKey = cfg["DIAL_API_KEY"] ?? "";
        opt.Model = cfg["DIAL_MODEL"] ?? "";
    });


builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var conn = cfg["ServiceBusConnection"] 
               ?? throw new InvalidOperationException("ServiceBusConnection is missing");
    return new Azure.Messaging.ServiceBus.ServiceBusClient(conn);
});

builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var cs = cfg["BLOB_CONNECTION_STRING"];
    if (!string.IsNullOrWhiteSpace(cs))
        return new BlobServiceClient(cs);

    var url = cfg["BLOB_ACCOUNT_URL"]
              ?? throw new InvalidOperationException("Set BLOB_ACCOUNT_URL or BLOB_CONNECTION_STRING");
    return new BlobServiceClient(new Uri(url), new DefaultAzureCredential());
});

builder.Services.AddOptions<BlobReaderOptions>()
    .Configure<IConfiguration>((opt, cfg) =>
    {
        if (long.TryParse(cfg["BLOB_MAX_BYTES"], out var max)) opt.MaxBytes = max;
        opt.DefaultContainer = cfg["BLOB_DEFAULT_CONTAINER"];
    });

builder.Build().Run();