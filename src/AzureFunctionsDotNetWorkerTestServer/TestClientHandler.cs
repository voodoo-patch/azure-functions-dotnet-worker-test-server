using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Context.Features;
using Microsoft.Azure.Functions.Worker.Core.FunctionMetadata;
using Microsoft.Extensions.DependencyInjection;

namespace AzureFunctionsDotNetWorkerTestServer;

/// <summary>
/// A <see cref="DelegatingHandler"/> that drives the Azure Functions worker pipeline for each
/// HTTP request, enabling end-to-end in-process testing without the Azure Functions host or gRPC.
/// </summary>
internal class TestClientHandler(IServiceProvider provider) : DelegatingHandler
{
    private const string InvocationIdHeader = "x-ms-invocation-id";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var invocationId = Guid.NewGuid().ToString();
        request.Headers.Add(InvocationIdHeader, invocationId);

        var functionContext = await BuildFunctionContextAsync(invocationId, request);

        // Ensuring that the input binding feature is present in the function context features.
        ArgumentNullException.ThrowIfNull(functionContext.Features.Get<IFunctionInputBindingFeature>());
        
        // Drive the worker middleware pipeline concurrently — it will rendezvous with the
        // ASP.NET pipeline via DefaultHttpCoordinator once SetHttpContextAsync is called.
        var workerTask = WorkerInternals.InvokeFunctionAsync(provider, functionContext);

        var response = await base.SendAsync(request, ct);
        await workerTask;
        return response;
    }
    
    private async Task<FunctionContext> BuildFunctionContextAsync(string invocationId, HttpRequestMessage request)
    {
        var metadataProvider = provider.GetRequiredService<IFunctionMetadataProvider>();
        var allMetadata = await metadataProvider.GetFunctionMetadataAsync("");

        var matched = MatchFunction(allMetadata, request, out var routeValues)
            ?? throw new InvalidOperationException(
                $"No HTTP trigger function matched path '{request.RequestUri!.AbsolutePath}'. " +
                "Ensure the request URL matches a function's route or name.");

        var definition = new TestFunctionDefinition(matched);

        var features = new TestInvocationFeatures(provider);
        features.Set<FunctionDefinition>(definition);
        features.Set<FunctionInvocation>(new TestFunctionInvocation(invocationId, matched.FunctionId!));

        var bindingsProxy = WorkerInternals.CreateBindingsFeatureProxy(provider, definition, routeValues);
        WorkerInternals.SetBindingsFeature(features, WorkerInternals.FunctionBindingsFeatureType, bindingsProxy);
        
        return WorkerInternals.CreateFunctionContext(provider, features);
    }

    /// <summary>
    /// Caches parsed route templates keyed by the Functions route pattern (e.g. "api/items/{id}"),
    /// avoiding re-parsing the template on every incoming request.
    /// </summary>
    private static readonly ConcurrentDictionary<string, TemplateMatcher> RouteMatcherCache = new();

    private static IFunctionMetadata? MatchFunction(IEnumerable<IFunctionMetadata> allMetadata, 
        HttpRequestMessage request,
        out RouteValueDictionary routeValues)
    {
        routeValues = new RouteValueDictionary();
        PathString path = request.RequestUri!.AbsolutePath;

        foreach (var metadata in allMetadata)
        {
            if (metadata.RawBindings is null) continue;

            foreach (var raw in metadata.RawBindings)
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                if (!IsHttpTrigger(root)) continue;

                if (IsMethodMatching(root) && IsRouteMatching(root, metadata, out routeValues))
                    return metadata;
            }
        }

        return null;

        bool IsHttpTrigger(JsonElement root)
        {
            if (!root.TryGetProperty("type", out var typeProp)) return false;

            return typeProp.GetString()?.Equals("httpTrigger", StringComparison.OrdinalIgnoreCase) ?? false;
        }

        bool IsRouteMatching(JsonElement root, IFunctionMetadata metadata, out RouteValueDictionary routeValues)
        {
            routeValues = new RouteValueDictionary();
            string? route = null;
            if (root.TryGetProperty("route", out var routeProp))
                route = routeProp.GetString();

            // If the route is not specified, the function name is used as the default route.
            if (string.IsNullOrEmpty(route))
                route = metadata.Name;

            // Use ASP.NET Core's own route template matcher so parameterized segments
            // (e.g. "items/{id}") are supported, not just exact/prefix string comparisons.
            var matcher = RouteMatcherCache.GetOrAdd($"api/{route}",
                pattern => new TemplateMatcher(TemplateParser.Parse(pattern), new RouteValueDictionary()));

            return matcher.TryMatch(path, routeValues);
        }
        
        bool IsMethodMatching(JsonElement root)
        {
            if (!root.TryGetProperty("methods", out var methodsProp)) return true;

            if (methodsProp.ValueKind != JsonValueKind.Array) return true;
            
            return methodsProp.EnumerateArray()
                .Select(m => m.GetString())
                .Contains(request.Method.Method, StringComparer.OrdinalIgnoreCase);
        }
    }
}
