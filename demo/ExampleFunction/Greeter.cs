using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using FromBodyAttribute = Microsoft.Azure.Functions.Worker.Http.FromBodyAttribute;

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
    [Function("Greeter")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", Route = "greeter")] HttpRequest req,
        FunctionContext executionContext)
    {
        executionContext
            .GetLogger<ILogger<Greeter>>()
            .LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }

    [Function("GreeterById")]
    public IActionResult RunById(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "greeter/{id:int}")] HttpRequest req, int id,
        FunctionContext executionContext)
    {
        executionContext
            .GetLogger<ILogger<Greeter>>()
            .LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult($"Welcome to Azure Functions! Id: {id}");
    }

    [Function("GreeterWithInput")]
    public IActionResult RunWithInput(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "greeter")] HttpRequest req,
        [FromBody] GreeterInput input, FunctionContext executionContext)
    {
        executionContext
            .GetLogger<ILogger<Greeter>>()
            .LogInformation("C# HTTP trigger function processed a request with input.");
        return new OkObjectResult($"Welcome to Azure Functions, {input.Name}!");
    }

    [Function("GreeterWithService")]
    public IActionResult RunWithService(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "greeterWithService")]
        HttpRequest req, FunctionContext executionContext)
    {
        executionContext
            .GetLogger<ILogger<Greeter>>()
            .LogInformation("C# HTTP trigger function processed a request.");

        var data = executionContext.InstanceServices
            .GetRequiredService<IDummyService>()
            .GetData();
        return new OkObjectResult($"Welcome to Azure Functions! Data: {data}");
    }

    public record GreeterInput(string Name);
}