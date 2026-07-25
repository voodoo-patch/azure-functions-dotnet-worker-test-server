using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace ExampleFunction;

public class StampHttpHeaderMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        await next(context);

        if (context.GetHttpContext() is { Response: { } response })
        {
            response.Headers["X-Stamp"] = DateTime.UtcNow.ToString("o");
        }
    }
}