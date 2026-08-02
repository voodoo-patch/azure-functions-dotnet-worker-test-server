using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace ExampleFunction.Http;

public class HttpExceptionMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpContext = context.GetHttpContext();

        try
        {
            await next(context);
        }
        catch (InvalidOperationException ex) when (httpContext is not null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await httpContext.Response.WriteAsync($"Handled by middleware: {ex.Message}");
        }
    }
}
