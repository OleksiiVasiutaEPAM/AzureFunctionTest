using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Epam.TestFunction.Models;
using Epam.TestFunction.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Epam.TestFunction.Functions;

public class AskDialAI
{
    private readonly HttpClient _http;
    private readonly OpenAIOptions _opt;
    private readonly ILogger<AskOpenAI> _log;
    private readonly ServiceBusClient _sbClient;

    [Function("AskDialAI")]
    public async Task<IResult> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        Input? input;
        try
        {
            input = await JsonSerializer.DeserializeAsync<Input>(req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return Results.BadRequest(new { error = "Invalid JSON body" });
        }
        
        if (string.IsNullOrWhiteSpace(input?.Prompt))
            return Results.BadRequest(new { error = "Body must be { \"prompt\": \"...\" }" });
        
        // TODO: move to env variables (or settings)
        var url = $"https://ai-proxy.lab.epam.com/openai/deployments/{_opt.Model}/chat/completions?api-version=2024-02-01";
        
        var payload = new
        {
            messages = new object[]
            {
                new { role = "system", content = "You are a helpful assistant." },
                new { role = "user",   content = input.Prompt }
            },
            temperature = input.Temperature ?? 0.2
        };
        
        var msg = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _opt.ApiKey);

        var res = await _http.SendAsync(msg);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync();
            _log.LogError("OpenAI error {Status}: {Body}", (int)res.StatusCode, body);
            return Results.Problem($"OpenAI error {(int)res.StatusCode}", statusCode: StatusCodes.Status502BadGateway);
        }
        
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var answer = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(answer))
            return Results.Problem("OpenAI returned empty response", statusCode: StatusCodes.Status502BadGateway);

        var outgoing = new OutMessage(input.Prompt, answer!, DateTime.UtcNow);
        var messageJson = JsonSerializer.Serialize(outgoing);

        await using var sender = _sbClient.CreateSender("openai-results");
        await sender.SendMessageAsync(new ServiceBusMessage(messageJson));
        
        return Results.Accepted(value: new
        {
            status = "queued",
            prompt = input.Prompt,
            preview = answer.Length > 240 ? answer[..240] + "…" : answer
        });
    }
}