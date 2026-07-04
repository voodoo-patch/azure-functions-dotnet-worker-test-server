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
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", Route = "greeter")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }

    [Function("GreeterWithInput")]
    public IActionResult RunWithInput([HttpTrigger(AuthorizationLevel.Function, "post", Route = "greeter")] HttpRequest req,
        [FromBody] GreeterInput input)
    {
        var method = req.Method;
        _logger.LogInformation("C# HTTP trigger function processed a request with input.");
        return new OkObjectResult($"Welcome to Azure Functions, {input.Name}!");
    }
    
    [Function("GreeterWithService")]
    public IActionResult RunWithService(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "greeterWithService")] HttpRequest req,
        [FromServices] IDummyService dummyService)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        var data = dummyService.GetData();
        return new OkObjectResult($"Welcome to Azure Functions! Data: {data}");
    }

    public record GreeterInput(string Name);
}