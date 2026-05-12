using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
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

        var matched = MatchFunction(allMetadata, request.RequestUri!)
            ?? throw new InvalidOperationException(
                $"No HTTP trigger function matched path '{request.RequestUri!.AbsolutePath}'. " +
                "Ensure the request URL matches a function's route or name.");

        var definition = new TestFunctionDefinition(matched);

        var features = new TestInvocationFeatures();
        features.Set((FunctionDefinition)definition);
        features.Set((FunctionInvocation)new TestFunctionInvocation(invocationId, matched.FunctionId!));

        var bindingsProxy = WorkerInternals.CreateBindingsFeatureProxy(provider, definition);
        WorkerInternals.SetBindingsFeature(features, bindingsProxy);

        return WorkerInternals.CreateFunctionContext(provider, features);
    }

    private static IFunctionMetadata? MatchFunction(IEnumerable<IFunctionMetadata> allMetadata, Uri requestUri)
    {
        var path = requestUri.AbsolutePath;

        foreach (var metadata in allMetadata)
        {
            if (metadata.RawBindings is null) continue;

            foreach (var raw in metadata.RawBindings)
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeProp)) continue;
                if (!typeProp.GetString()?.Equals("httpTrigger", StringComparison.OrdinalIgnoreCase) == true) continue;

                string? route = null;
                if (root.TryGetProperty("route", out var routeProp))
                    route = routeProp.GetString();

                if (string.IsNullOrEmpty(route))
                    route = metadata.Name;

                if (path.StartsWith($"/api/{route}", StringComparison.OrdinalIgnoreCase))
                    return metadata;
            }
        }

        return null;
    }
}
