using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using FromBodyAttribute = Microsoft.Azure.Functions.Worker.Http.FromBodyAttribute;

namespace ExampleFunction.Http;

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

public class HttpTriggerExamples
{
    [Function("HttpTriggerExamplesGet")]
    public IActionResult RunGet([HttpTrigger(AuthorizationLevel.Function, "get", Route = "http-trigger-examples")] HttpRequest req,
        FunctionContext executionContext)
    {
        if (req.Headers.TryGetValue("X-Throw-Function", out var values) &&
            values.Any(v => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Forced function failure.");
        }

        req.HttpContext.Response.Headers["X-Function-Executed"] = "true";

        executionContext
            .GetLogger<ILogger<HttpTriggerExamples>>()
            .LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }

    [Function("HttpTriggerExamplesGetById")]
    public IActionResult RunGetById(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "http-trigger-examples/{id:int}")] HttpRequest req, int id,
        FunctionContext executionContext)
    {
        executionContext
            .GetLogger<ILogger<HttpTriggerExamples>>()
            .LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult($"Welcome to Azure Functions! Id: {id}");
    }

    [Function("HttpTriggerExamplesGetByQueryParam")]
    public IActionResult RunGetByQueryParam(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "http-trigger-examples/by-query")] HttpRequest req,
        FunctionContext executionContext)
    {
        executionContext
            .GetLogger<ILogger<HttpTriggerExamples>>()
            .LogInformation("C# HTTP trigger function processed a request.");
        var id = req.Query["id"];
        return new OkObjectResult($"Welcome to Azure Functions! Id: {id}");
    }

    [Function("HttpTriggerExamplesPostWithInput")]
    public IActionResult RunPostWithInput(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "http-trigger-examples")] HttpRequest req,
        [FromBody] HttpTriggerExampleInput input, FunctionContext executionContext)
    {
        executionContext
            .GetLogger<ILogger<HttpTriggerExamples>>()
            .LogInformation("C# HTTP trigger function processed a request with input.");
        return new OkObjectResult($"Welcome to Azure Functions, {input.Name}!");
    }

    [Function("HttpTriggerExamplesGetWithService")]
    public IActionResult RunGetWithService(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "http-trigger-examples/with-service")]
        HttpRequest req, FunctionContext executionContext)
    {
        executionContext
            .GetLogger<ILogger<HttpTriggerExamples>>()
            .LogInformation("C# HTTP trigger function processed a request.");

        var data = executionContext.InstanceServices
            .GetRequiredService<IDummyService>()
            .GetData();
        return new OkObjectResult($"Welcome to Azure Functions! Data: {data}");
    }

    public record HttpTriggerExampleInput(string Name);
}
