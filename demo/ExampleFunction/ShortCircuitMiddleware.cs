using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace ExampleFunction;

public class ShortCircuitMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpContext = context.GetHttpContext();
        if (httpContext?.Request.Headers.TryGetValue("X-Short-Circuit", out var values) == true &&
            values.Any(v => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)))
        {
            httpContext.Response.StatusCode = StatusCodes.Status418ImATeapot;
            await httpContext.Response.WriteAsync("Short-circuited by middleware.");
            return;
        }

        await next(context);
    }
}
