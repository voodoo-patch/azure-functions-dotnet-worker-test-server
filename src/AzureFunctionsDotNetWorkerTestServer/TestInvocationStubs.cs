using System.Collections.Immutable;
using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.Functions.Worker;

namespace AzureFunctionsDotNetWorkerTestServer;

internal sealed class TestFunctionInvocation(string id, string functionId) : FunctionInvocation
{
    public override string Id { get; } = id;
    public override string FunctionId { get; } = functionId;
    public override TraceContext TraceContext { get; } = new TestTraceContext();
}

internal sealed class TestTraceContext : TraceContext
{
    public override string TraceParent { get; } = string.Empty;
    public override string TraceState { get; } = string.Empty;
}

internal sealed class TestBindingMetadata(string name, string type, BindingDirection direction) : BindingMetadata
{
    public override string Name { get; } = name;
    public override string Type { get; } = type;
    public override BindingDirection Direction { get; } = direction;
}

/// <summary>
/// A <see cref="DispatchProxy"/>-based implementation of the internal <c>IFunctionBindingsFeature</c>.
/// <para>
/// <c>DispatchProxy.Create()</c> always uses a parameterless constructor, so dependencies
/// cannot be passed via constructor arguments — use <see cref="CachedOutputBindingsInfo"/>
/// after creation instead.
/// </para>
/// </summary>
internal class TestFunctionBindingsFeatureProxy : DispatchProxy
{
    private readonly Dictionary<string, object?> _outputData = new();
    private object? _invocationResult;

    public object? CachedOutputBindingsInfo { get; set; }
    public RouteValueDictionary RouteValues { get; set; } = new();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name switch
        {
            "get_TriggerMetadata"    => ImmutableDictionary<string, object?>.Empty,
            "get_InputData"          => RouteValues.ToImmutableDictionary(),
            "get_OutputBindingData"  => _outputData,
            "get_OutputBindingsInfo" => CachedOutputBindingsInfo,
            "get_InvocationResult"   => _invocationResult,
            "set_InvocationResult"   => SetResult(args),
            _                        => null,
        };

    private object? SetResult(object?[]? args)
    {
        _invocationResult = args?[0];
        return null;
    }
}
