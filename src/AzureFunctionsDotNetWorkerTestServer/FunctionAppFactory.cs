using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using AzureFunctionsDotNetWorkerTestServer.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Core.FunctionMetadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AzureFunctionsDotNetWorkerTestServer;

public class FunctionAppFactory<TProgram>
    : WebApplicationFactory<TProgram>
    where TProgram : class, IFunctionAppServices
{
    private readonly Action<IServiceCollection>? _serviceOverrides;

    public FunctionAppFactory()
    {
    }

    protected FunctionAppFactory(Action<IServiceCollection>? serviceOverrides = null)
    {
        _serviceOverrides = serviceOverrides;
    }

    protected override IHostBuilder CreateHostBuilder()
    {
        // Your custom host — Program is just used as assembly marker
        var hostBuilder = Host
            .CreateDefaultBuilder(["--Functions:Worker:HostEndpoint=http://localhost:0/"])
            .ConfigureFunctionsWebApplication();

        return hostBuilder;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable(
            "FUNCTIONS_APPLICATION_DIRECTORY",
            Path.GetDirectoryName(typeof(TProgram).Assembly.Location));

        builder
            .ConfigureServices(services =>
            {
                var hostedServiceDescriptor = services
                    .First(d => d.ImplementationType?.Name == "WorkerHostedService");
                services.Remove(hostedServiceDescriptor);

                // Discover and register the generated metadata provider from TProgram's assembly
                var assembly = typeof(TProgram).Assembly;
                // Find IFunctionMetadataProvider implementation in that assembly
                var metadataProviderType = assembly.GetTypes()
                    .FirstOrDefault(t => t.IsAssignableTo(typeof(IFunctionMetadataProvider))
                                         && t is { IsInterface: false, IsAbstract: false });
                if (metadataProviderType is not null)
                {
                    services.RemoveAll<IFunctionMetadataProvider>();
                    services.AddSingleton(typeof(IFunctionMetadataProvider), metadataProviderType);
                }

                // Register Program's services by calling the ConfigureServices method
                TProgram.ConfigureServices(services);

                // overrides run AFTER all registrations
                _serviceOverrides?.Invoke(services);
            });
    }
}

internal class TestFunctionBindingsFeatureProxy : DispatchProxy
{
    private readonly Dictionary<string, object?> _outputData = new();
    private object? _invocationResult;
    
    // Set after creation — cannot pass via constructor with DispatchProxy
    public object? CachedOutputBindingsInfo { get; set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        switch (targetMethod?.Name)
        {
            case "get_TriggerMetadata": return ImmutableDictionary<string, object?>.Empty;
            case "get_InputData": return ImmutableDictionary<string, object?>.Empty;
            case "get_OutputBindingData": return _outputData;
            case "get_OutputBindingsInfo": return CachedOutputBindingsInfo;
            case "get_InvocationResult": return _invocationResult;
            case "set_InvocationResult":
                _invocationResult = args?[0];
                return null;
            default: return null;
        }
    }
}

internal sealed class TestFunctionInvocation(string id, string functionId) : FunctionInvocation
{
    public override string Id { get; } = id;
    public override string FunctionId { get; } = functionId;
    public override TraceContext TraceContext { get; } = new TestTraceContext();
}

internal sealed class TestTraceContext : TraceContext
{
    public override string TraceParent { get; }
    public override string TraceState { get; }
}
internal sealed class TestBindingMetadata(string name, string type, BindingDirection direction) : BindingMetadata
{
    public override string Name { get; } = name;
    public override string Type { get; } = type;
    public override BindingDirection Direction { get; } = direction;
}

internal sealed class TestFunctionDefinition : FunctionDefinition
{
    public override string Id { get; }
    public override string Name { get; }
    public override string PathToAssembly { get; }
    public override string EntryPoint { get; }
    public override ImmutableArray<FunctionParameter> Parameters { get; }
    public override IImmutableDictionary<string, BindingMetadata> InputBindings { get; }
    public override IImmutableDictionary<string, BindingMetadata> OutputBindings { get; }

    public TestFunctionDefinition(IFunctionMetadata metadata)
    {
        Id = metadata.FunctionId!;
        Name = metadata.Name!;
        // append scriptfile to the assembly path to ensure that the path is absolute
        PathToAssembly = Path.Combine(Path.GetDirectoryName((typeof(TestFunctionDefinition)).Assembly.Location)!,
            metadata.ScriptFile!);
        EntryPoint = metadata.EntryPoint!;
        Parameters = BuildParameters(metadata);
        InputBindings = BuildBindings(metadata.RawBindings, "In");
        OutputBindings = BuildBindings(metadata.RawBindings, "Out");
    }
    
    private static IImmutableDictionary<string, BindingMetadata> BuildBindings(
        IList<string>? rawBindings, string direction)
    {
        if (rawBindings is null) return ImmutableDictionary<string, BindingMetadata>.Empty;

        var builder = ImmutableDictionary.CreateBuilder<string, BindingMetadata>();
        foreach (var raw in rawBindings)
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (!root.TryGetProperty("direction", out var dir)) continue;
            if (!dir.GetString()?.Equals(direction, StringComparison.OrdinalIgnoreCase) ?? true) continue;

            var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            builder[name] = new TestBindingMetadata(name, type, BindingDirection.In);
        }
        return builder.ToImmutable();
    }
    
    private static ImmutableArray<FunctionParameter> BuildParameters(IFunctionMetadata metadata)
    {
        if (metadata.EntryPoint is null || metadata.ScriptFile is null)
            return ImmutableArray<FunctionParameter>.Empty;

        // EntryPoint format: "Namespace.ClassName.MethodName"
        var lastDot = metadata.EntryPoint.LastIndexOf('.');
        var typeName = metadata.EntryPoint[..lastDot];
        var methodName = metadata.EntryPoint[(lastDot + 1)..];

        // Find the already-loaded assembly matching the ScriptFile name
        var assemblyName = Path.GetFileNameWithoutExtension(metadata.ScriptFile);
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == assemblyName);

        if (assembly is null) return ImmutableArray<FunctionParameter>.Empty;

        var method = assembly.GetType(typeName)?.GetMethod(methodName);
        if (method is null) return ImmutableArray<FunctionParameter>.Empty;

        return [
            ..method.GetParameters()
                .Where(p => p.Name is not null)
                .Select(p => new FunctionParameter(
                    p.Name!,
                    p.ParameterType,
                    p.HasDefaultValue,
                    p.DefaultValue,
                    ImmutableDictionary<string, object>.Empty))
        ];
    }
}