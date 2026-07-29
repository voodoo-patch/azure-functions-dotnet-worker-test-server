using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Routing;
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
    public static readonly Type InvocationFeatureProviderType;
    public static readonly Type OutputBindingsInfoProviderType;

    private static readonly Assembly WorkerAssembly = typeof(FunctionContext).Assembly;

    // Cached MethodInfo objects — resolved once to avoid per-invocation reflection overhead.
    private static readonly MethodInfo CreateContextMethod;
    private static readonly MethodInfo InvokeFunctionAsyncMethod;
    private static readonly MethodInfo GetBindingsInfoMethod;
    private static readonly MethodInfo SetFeaturesMethod;
    private static readonly MethodInfo DispatchProxyCreateMethod;
    private static readonly MethodInfo TryCreateMethod;


    static WorkerInternals()
    {
        FunctionContextFactoryType = Require(
            WorkerAssembly.GetType("Microsoft.Azure.Functions.Worker.Pipeline.IFunctionContextFactory"),
            "IFunctionContextFactory");

        FunctionsApplicationType = Require(
            WorkerAssembly.GetType("Microsoft.Azure.Functions.Worker.IFunctionsApplication"),
            "IFunctionsApplication");

        FunctionBindingsFeatureType = Require(
            WorkerAssembly.GetType("Microsoft.Azure.Functions.Worker.Context.Features.IFunctionBindingsFeature"),
            "IFunctionBindingsFeature");

        OutputBindingsInfoProviderType = Require(
            WorkerAssembly.GetType("Microsoft.Azure.Functions.Worker.OutputBindings.IOutputBindingsInfoProvider"),
            "IOutputBindingsInfoProvider");

        InvocationFeatureProviderType = Require(
            WorkerAssembly.GetType("Microsoft.Azure.Functions.Worker.IInvocationFeatureProvider"),
            "IInvocationFeatureProvider");

        CreateContextMethod = Require(FunctionContextFactoryType.GetMethod("Create"), "IFunctionContextFactory.Create");
        InvokeFunctionAsyncMethod = Require(FunctionsApplicationType.GetMethod("InvokeFunctionAsync"),
            "IFunctionsApplication.InvokeFunctionAsync");
        GetBindingsInfoMethod = Require(OutputBindingsInfoProviderType.GetMethod("GetBindingsInfo"),
            "IOutputBindingsInfoProvider.GetBindingsInfo");
        SetFeaturesMethod = Require(typeof(IInvocationFeatures).GetMethod(nameof(IInvocationFeatures.Set)),
            "IInvocationFeatures.Set");
        TryCreateMethod = Require(InvocationFeatureProviderType.GetMethod("TryCreate"),
            "IInvocationFeatureProvider.TryCreate");

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
    public static object CreateBindingsFeatureProxy(IServiceProvider provider, FunctionDefinition definition,
        RouteValueDictionary routeValues)
    {
        var infoProvider = provider.GetRequiredService(OutputBindingsInfoProviderType);
        var outputBindingsInfo = GetBindingsInfoMethod.Invoke(infoProvider, [definition]);

        var proxy = DispatchProxyCreateMethod
            .MakeGenericMethod(FunctionBindingsFeatureType, typeof(TestFunctionBindingsFeatureProxy))
            .Invoke(null, null)!;

        ((TestFunctionBindingsFeatureProxy)proxy).CachedOutputBindingsInfo = outputBindingsInfo;
        ((TestFunctionBindingsFeatureProxy)proxy).RouteValues = routeValues;

        return proxy;
    }

    /// <summary>Sets the bindings feature on the invocation features dictionary using the runtime type as the generic key.</summary>
    public static void SetBindingsFeature(IInvocationFeatures features, Type type, object proxy)
    {
        SetFeaturesMethod
            .MakeGenericMethod(type)
            .Invoke(features, [proxy]);
    }

    /// <summary>
    /// Caches, per feature type, the provider interface, its <c>TryCreate</c> method, and the
    /// concrete feature type that were discovered to successfully create the feature — avoids
    /// repeating the assembly-wide scan in <see cref="TryCreateFeatureViaProvider{T}"/> on every call.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, (Type ProviderType, MethodInfo TryCreate, Type ConcreteType)>
        ResolvedProviders = new();

    /// <summary>
    /// Creates a feature by structurally discovering a "TryCreate(Type, out TFeature)"-shaped
    /// provider interface in the worker assembly (e.g. <c>IInputConversionFeatureProvider</c>),
    /// rather than assuming a "{FeatureName}Provider" naming convention. This mirrors how the
    /// worker itself locates specialized providers beyond the generic <c>IInvocationFeatureProvider</c>.
    /// </summary>
    public static bool TryCreateFeatureViaProvider<T>(IServiceProvider serviceProvider,
        [NotNullWhen(true)] out T? feature)
    {
        var featureType = typeof(T);

        // Fast path: reuse the provider/method/concrete-type combination discovered previously.
        if (ResolvedProviders.TryGetValue(featureType, out var cached) &&
            serviceProvider.GetService(cached.ProviderType) is { } cachedProvider &&
            TryInvoke(cachedProvider, cached.TryCreate, cached.ConcreteType, out feature))
        {
            return true;
        }

        // Discover candidate concrete types assignable to the requested feature type.
        var candidateTypes = WorkerAssembly.GetTypes()
            .Where(t => featureType.IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .Concat([featureType])
            .ToList();

        // Discover candidate provider interfaces exposing bool TryCreate(Type, out TFeature).
        var providerTypes = WorkerAssembly.GetTypes()
            .Where(t => t.IsInterface && GetMatchingTryCreate(t, featureType) is not null);

        foreach (var providerType in providerTypes)
        {
            var tryCreateMethod = GetMatchingTryCreate(providerType, featureType)!;

            if (serviceProvider.GetService(providerType) is not { } providerInstance) continue;

            foreach (var candidate in candidateTypes)
            {
                if (!TryInvoke(providerInstance, tryCreateMethod, candidate, out feature)) continue;

                ResolvedProviders[featureType] = (providerType, tryCreateMethod, candidate);
                return true;
            }
        }

        feature = default!;
        return false;

        static bool TryInvoke(object providerInstance, MethodInfo tryCreateMethod, Type candidate,
            [NotNullWhen(true)] out T? created)
        {
            var parameters = new object?[] { candidate, null };
            var success = (bool)tryCreateMethod.Invoke(providerInstance, parameters)!;

            if (success && parameters[1] is T result)
            {
                created = result;
                return true;
            }

            created = default!;
            return false;
        }
    }

    /// <summary>
    /// Returns the provider's <c>TryCreate</c> method if it is shaped like
    /// <c>bool TryCreate(Type type, out TFeature feature)</c> where TFeature exactly matches
    /// <paramref name="featureType"/>, or <see langword="null"/> otherwise. The <c>object</c>-typed
    /// <c>IInvocationFeatureProvider</c> is intentionally excluded by the exact-match check —
    /// that generic provider is handled by <see cref="TryCreateFeatureViaInvocationProvider{T}"/>.
    /// </summary>
    private static MethodInfo? GetMatchingTryCreate(Type providerType, Type featureType)
    {
        var method = providerType.GetMethod("TryCreate");
        if (method is null) return null;

        var parameters = method.GetParameters();
        if (parameters.Length != 2) return null;
        if (parameters[0].ParameterType != typeof(Type)) return null;
        if (!parameters[1].ParameterType.IsByRef) return null;

        var outType = parameters[1].ParameterType.GetElementType();
        return outType == featureType ? method : null;
    }

    public static bool TryCreateFeatureViaInvocationProvider<T>(IServiceProvider serviceProvider,
        [NotNullWhen(true)] out T? feature)
    {
        var featureType = typeof(T);
        var invocationFeatureProviders = serviceProvider.GetServices(InvocationFeatureProviderType);

        foreach (var invocationFeatureProvider in invocationFeatureProviders)
        {
            var parameters = new object?[] { featureType, null };
            var success = (bool)TryCreateMethod.Invoke(invocationFeatureProvider, parameters)!;

            if (success && parameters[1] is T createdFeature)
            {
                feature = createdFeature;
                return true;
            }
        }

        feature = default!;
        return false;
    }

    private static T Require<T>(T? value, string name) where T : class =>
        value ?? throw new InvalidOperationException(
            $"Azure Functions Worker internal member '{name}' could not be resolved. " +
            "The worker assembly version may have changed.");
}
