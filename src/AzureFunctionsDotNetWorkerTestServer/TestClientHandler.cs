using System.Reflection;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Core.FunctionMetadata;
using Microsoft.Extensions.DependencyInjection;

namespace AzureFunctionsDotNetWorkerTestServer;

public class TestClientHandler(IServiceProvider provider) : DelegatingHandler
{
    private const string CorrelationHeader = "x-ms-invocation-id";

    private Lazy<Type[]> AssemblyTypes => new(() => AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => a.GetTypes())
        .ToArray());

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var invocationId = Guid.NewGuid().ToString();
        request.Headers.Add(CorrelationHeader, invocationId);

        var functionContext = await CreateFunctionContext(invocationId, request);
        // Drive worker side concurrently — it will block in SetFunctionContextAsync
        // until the ASP.NET side calls SetHttpContextAsync
        var workerTask = DriveWorkerAsync(invocationId, functionContext, ct);

        var response = await base.SendAsync(request, ct); // triggers ASP.NET middleware
        await workerTask;
        return response;
    }

    private async Task<FunctionContext> CreateFunctionContext(string invocationId, HttpRequestMessage request)
    {
        var functionContextFactoryType = AssemblyTypes.Value
            .FirstOrDefault(t => t.FullName == "Microsoft.Azure.Functions.Worker.Pipeline.IFunctionContextFactory");
        var functionContextFactory = provider.GetService(functionContextFactoryType);

        // create features 
        var (features, functionDefinition) = await CreateFeatures(invocationId, request);

        // Pre-compute OutputBindingsInfo via reflection (both types are internal)
        var outputBindingsInfoProviderType = typeof(FunctionContext).Assembly
            .GetType("Microsoft.Azure.Functions.Worker.OutputBindings.IOutputBindingsInfoProvider")!;
        var outputBindingsInfoProvider = provider.GetRequiredService(outputBindingsInfoProviderType);
        var outputBindingsInfo = outputBindingsInfoProviderType
            .GetMethod("GetBindingsInfo")!
            .Invoke(outputBindingsInfoProvider, [functionDefinition]);
        
        // Resolve the internal type at runtime
        var bindingsFeatureType = typeof(FunctionContext).Assembly
            .GetType("Microsoft.Azure.Functions.Worker.Context.Features.IFunctionBindingsFeature")!;

        // DispatchProxy.Create<TInterface, TProxy>() via reflection bypasses internal visibility
        var proxy = typeof(DispatchProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m is { Name: "Create", IsGenericMethodDefinition: true })
            .MakeGenericMethod(bindingsFeatureType, typeof(TestFunctionBindingsFeatureProxy))
            .Invoke(null, null)!;
        
        // Pass output bindings info to the proxy
        ((TestFunctionBindingsFeatureProxy)proxy).CachedOutputBindingsInfo = outputBindingsInfo;
        
        // Set on features using the runtime type as the generic parameter
        typeof(IInvocationFeatures)
            .GetMethod(nameof(IInvocationFeatures.Set))!
            .MakeGenericMethod(bindingsFeatureType)
            .Invoke(features, [proxy]);

        // Invoke Create via reflection
        var context = (FunctionContext)functionContextFactoryType!
            .GetMethod("Create")!
            .Invoke(functionContextFactory, [features, CancellationToken.None])!;

        return context;
    }

    private async Task<(TestInvocationFeatures, TestFunctionDefinition)> CreateFeatures(string invocationId, HttpRequestMessage request)
    {
        var features = new TestInvocationFeatures();
        var metadataProvider = provider.GetRequiredService<IFunctionMetadataProvider>();
        var functionMetadata = await metadataProvider.GetFunctionMetadataAsync("");
        var matchedFunction = MatchFunction(functionMetadata, request.RequestUri!);
        var testDefinition = new TestFunctionDefinition(matchedFunction);
        features.Set((FunctionDefinition)testDefinition);
        features.Set((FunctionInvocation)new TestFunctionInvocation(invocationId, matchedFunction.FunctionId));
        
        return (features, testDefinition);
    }

    private static IFunctionMetadata? MatchFunction(
        IEnumerable<IFunctionMetadata> allMetadata, Uri requestUri)
    {
        var path = requestUri.AbsolutePath; // e.g. "/api/Greeter"

        foreach (var metadata in allMetadata)
        {
            var httpBinding = metadata.RawBindings?
                .Select(b => JsonDocument.Parse(b).RootElement)
                .FirstOrDefault(e =>
                    e.TryGetProperty("type", out var t) &&
                    t.GetString()?.Equals("httpTrigger", StringComparison.OrdinalIgnoreCase) == true);

            if (httpBinding is null) continue;

            // Route from binding, fallback to function name
            string? route = null;
            if (httpBinding.Value.TryGetProperty("route", out var routeProp))
                route = routeProp.GetString();

            if (string.IsNullOrEmpty(route))
                route = metadata.Name;

            // Match: path must start with /api/{route}
            if (path.StartsWith($"/api/{route}", StringComparison.OrdinalIgnoreCase))
                return metadata;
        }

        return null;
    }

    private async Task DriveWorkerAsync(string invocationId, FunctionContext context, CancellationToken ct)
    {
        // var httpCoordinatorType = AssemblyTypes.Value
        //     .FirstOrDefault(t =>
        //         t.FullName == "Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore.IHttpCoordinator");
        // var httpCoordinator = provider.GetService(httpCoordinatorType);
        // // call SetFunctionContextAsync via reflection, which will unblock the worker to continue processing the function invocation
        // var setFunctionContextAsyncMethod = httpCoordinatorType!.GetMethod("SetFunctionContextAsync")!;
        // var httpContext =
        //     await (Task<HttpContext>)setFunctionContextAsyncMethod.Invoke(httpCoordinator, [invocationId, context])!;
        //
        var functionsApplicationType = AssemblyTypes.Value
            .FirstOrDefault(t => t.FullName == "Microsoft.Azure.Functions.Worker.IFunctionsApplication");
        var functionsApp = provider.GetService(functionsApplicationType);
        // call InvokeFunctionAsync
        var invokeFunctionAsyncMethod = functionsApplicationType!.GetMethod("InvokeFunctionAsync")!;
        await (Task)invokeFunctionAsyncMethod.Invoke(functionsApp, [context])!;
        //
        // // call CompleteFunctionInvocation on the coordinator to unblock the worker after the function invocation is complete
        // var completeFunctionInvocationMethod = httpCoordinatorType.GetMethod("CompleteFunctionInvocation")!;
        // completeFunctionInvocationMethod.Invoke(httpCoordinator, [invocationId]);
    }
}