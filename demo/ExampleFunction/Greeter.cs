using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExampleFunction;

public interface IDummyService
{
    string GetData();
}
public class DummyService : IDummyService
{
    public string GetData()
    {
        return "Hello from DummyService!";
    }
}

public class Greeter
{
    private readonly ILogger<Greeter> _logger;

    public Greeter(ILogger<Greeter> logger)
    {
        _logger = logger;
    }

    [Function("Greeter")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }
}
