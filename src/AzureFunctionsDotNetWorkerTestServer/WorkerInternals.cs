using System.Reflection;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;

namespace AzureFunctionsDotNetWorkerTestServer;

/// <summary>
/// Centralizes resolution of Azure Functions Worker internal types and methods via reflection.
/// All types and method handles are resolved once at class initialization and cached for reuse,
/// avoiding repeated reflection lookups on the hot path.
/// </summary>
internal static class WorkerInternals
{
    // Internal types — not publicly accessible, resolved by full name from the worker assembly.
    public static readonly Type FunctionContextFactoryType;
    public static readonly Type FunctionsApplicationType;
    public static readonly Type FunctionBindingsFeatureType;
    public static readonly Type OutputBindingsInfoProviderType;

    // Cached MethodInfo objects — resolved once to avoid per-invocation reflection overhead.
    private static readonly MethodInfo CreateContextMethod;
    private static readonly MethodInfo InvokeFunctionAsyncMethod;
    private static readonly MethodInfo GetBindingsInfoMethod;
    private static readonly MethodInfo SetFeaturesMethod;
    private static readonly MethodInfo DispatchProxyCreateMethod;

    static WorkerInternals()
    {
        var workerAssembly = typeof(FunctionContext).Assembly;

        FunctionContextFactoryType = Require(
            workerAssembly.GetType("Microsoft.Azure.Functions.Worker.Pipeline.IFunctionContextFactory"),
            "IFunctionContextFactory");

        FunctionsApplicationType = Require(
            workerAssembly.GetType("Microsoft.Azure.Functions.Worker.IFunctionsApplication"),
            "IFunctionsApplication");

        FunctionBindingsFeatureType = Require(
            workerAssembly.GetType("Microsoft.Azure.Functions.Worker.Context.Features.IFunctionBindingsFeature"),
            "IFunctionBindingsFeature");

        OutputBindingsInfoProviderType = Require(
            workerAssembly.GetType("Microsoft.Azure.Functions.Worker.OutputBindings.IOutputBindingsInfoProvider"),
            "IOutputBindingsInfoProvider");

        CreateContextMethod = Require(FunctionContextFactoryType.GetMethod("Create"), "IFunctionContextFactory.Create");
        InvokeFunctionAsyncMethod = Require(FunctionsApplicationType.GetMethod("InvokeFunctionAsync"), "IFunctionsApplication.InvokeFunctionAsync");
        GetBindingsInfoMethod = Require(OutputBindingsInfoProviderType.GetMethod("GetBindingsInfo"), "IOutputBindingsInfoProvider.GetBindingsInfo");
        SetFeaturesMethod = Require(typeof(IInvocationFeatures).GetMethod(nameof(IInvocationFeatures.Set)), "IInvocationFeatures.Set");

        // Use explicit lookup to avoid AmbiguousMatchException — DispatchProxy has both generic and non-generic overloads.
        DispatchProxyCreateMethod = typeof(DispatchProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m is { Name: "Create", IsGenericMethodDefinition: true });
    }

    /// <summary>Creates a <see cref="FunctionContext"/> using the internal factory, populating it with the given features.</summary>
    public static FunctionContext CreateFunctionContext(IServiceProvider provider, IInvocationFeatures features)
    {
        var factory = provider.GetRequiredService(FunctionContextFactoryType);
        return (FunctionContext)CreateContextMethod.Invoke(factory, [features, CancellationToken.None])!;
    }

    /// <summary>Invokes the worker middleware pipeline for the given <see cref="FunctionContext"/>.</summary>
    public static Task InvokeFunctionAsync(IServiceProvider provider, FunctionContext context)
    {
        var app = provider.GetRequiredService(FunctionsApplicationType);
        return (Task)InvokeFunctionAsyncMethod.Invoke(app, [context])!;
    }

    /// <summary>
    /// Creates a <see cref="DispatchProxy"/>-based implementation of the internal
    /// <c>IFunctionBindingsFeature</c> interface, pre-populated with the correct
    /// <c>OutputBindingsInfo</c> resolved from DI for the given function definition.
    /// </summary>
    public static object CreateBindingsFeatureProxy(IServiceProvider provider, FunctionDefinition definition)
    {
        var infoProvider = provider.GetRequiredService(OutputBindingsInfoProviderType);
        var outputBindingsInfo = GetBindingsInfoMethod.Invoke(infoProvider, [definition]);

        var proxy = DispatchProxyCreateMethod
            .MakeGenericMethod(FunctionBindingsFeatureType, typeof(TestFunctionBindingsFeatureProxy))
            .Invoke(null, null)!;

        ((TestFunctionBindingsFeatureProxy)proxy).CachedOutputBindingsInfo = outputBindingsInfo;
        return proxy;
    }

    /// <summary>Sets the bindings feature on the invocation features dictionary using the runtime type as the generic key.</summary>
    public static void SetBindingsFeature(IInvocationFeatures features, object proxy)
    {
        SetFeaturesMethod
            .MakeGenericMethod(FunctionBindingsFeatureType)
            .Invoke(features, [proxy]);
    }

    private static T Require<T>(T? value, string name) where T : class =>
        value ?? throw new InvalidOperationException(
            $"Azure Functions Worker internal member '{name}' could not be resolved. " +
            "The worker assembly version may have changed.");
}
