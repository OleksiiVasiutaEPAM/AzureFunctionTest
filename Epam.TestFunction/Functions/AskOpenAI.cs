using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Messaging.ServiceBus;
using Epam.TestFunction.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class AskOpenAI
{
    private readonly HttpClient _http;
    private readonly OpenAIOptions _opt;
    private readonly ILogger<AskOpenAI> _log;
    private readonly ServiceBusClient _sbClient;

    public AskOpenAI(
        IHttpClientFactory f,
        IOptions<OpenAIOptions> opt,
        ILogger<AskOpenAI> log,
        ServiceBusClient sbClient)
    {
        _http = f.CreateClient();
        _opt = opt.Value;
        _log = log;
        _sbClient = sbClient;
    }

    public record Input([property: JsonPropertyName("prompt")] string Prompt, double? Temperature);
    public record OutMessage(string prompt, string answer, DateTime createdUtc);

    [Function("AskOpenAI")]
    public async Task<IResult> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        // 1) читаем вход
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

        // 2) запрос в OpenAI
        var url = "https://api.openai.com/v1/chat/completions";

        var payload = new
        {
            model = _opt.Model,
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

        // 3) парсим ответ
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var answer = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(answer))
            return Results.Problem("OpenAI returned empty response", statusCode: StatusCodes.Status502BadGateway);

        // 4) отправляем в Service Bus
        var outgoing = new OutMessage(input.Prompt, answer!, DateTime.UtcNow);
        var messageJson = JsonSerializer.Serialize(outgoing);

        await using var sender = _sbClient.CreateSender("openai-results");
        await sender.SendMessageAsync(new ServiceBusMessage(messageJson));

        // 5) HTTP-ответ клиенту
        return Results.Accepted(value: new
        {
            status = "queued",
            prompt = input.Prompt,
            preview = answer!.Length > 240 ? answer[..240] + "…" : answer
        });
    }
}