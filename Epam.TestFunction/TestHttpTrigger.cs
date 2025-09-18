using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Epam.TestFunction;

public class TestHttpTrigger
{
    private readonly ILogger<TestHttpTrigger> _logger;

    public TestHttpTrigger(ILogger<TestHttpTrigger> logger)
    {
        _logger = logger;
    }

    [Function("TestHttpTrigger")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
        
    }

}