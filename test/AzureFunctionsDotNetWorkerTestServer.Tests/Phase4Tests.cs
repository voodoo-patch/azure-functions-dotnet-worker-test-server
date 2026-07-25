using ExampleFunction;
using Microsoft.AspNetCore.Http;
using System.Net;

namespace AzureFunctionsDotNetWorkerTestServer.Tests;

[Trait("Suite", "Harness")]
[Trait("Speed", "Fast")]
public class Phase4Tests(FunctionAppFactory<Program> factory)
    : IClassFixture<FunctionAppFactory<Program>>
{
    [Fact]
    public async Task Function_CanInvokeStampMiddleware()
    {
        var client = factory.CreateFunctionsClient();

        var response = await client.GetAsync("/api/greeter");
        response.EnsureSuccessStatusCode();

        Assert.True(response.Headers.Contains("X-Stamp"));
    }

    [Fact]
    public async Task Function_MiddlewareCanShortCircuit()
    {
        var client = factory.CreateFunctionsClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/greeter");
        request.Headers.Add("X-Short-Circuit", "true");

        var response = await client.SendAsync(request);

        Assert.Equal((HttpStatusCode)StatusCodes.Status418ImATeapot, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Function-Executed"));
    }

    [Fact]
    public async Task Function_MiddlewareCanHandleFunctionException()
    {
        var client = factory.CreateFunctionsClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/greeter");
        request.Headers.Add("X-Throw-Function", "true");

        var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("Handled by middleware: Forced function failure.", content);
    }
}