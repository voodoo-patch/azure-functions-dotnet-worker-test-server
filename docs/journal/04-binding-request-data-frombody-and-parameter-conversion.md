# Binding request data: `[FromBody]` and parameter conversion

**Goal:** Make function parameters beyond the bare trigger request get populated the same way
production would populate them — starting with `[FromBody]`, and establishing the pattern that
`[FromServices]`-style parameter DI is deliberately *not* supported (constructor injection is the
isolated-worker convention).

**Commits:** `b5f5ed1` — *"Add support to Input Binding"*, `f4d24c8` — *"Add `[FromBody]` Binding
support"*, `8f6f3ed` — *"refactor: parameter metadata parity, reflection fragility, caching, route
template matching"*.

This phase involved the longest debugging chain so far, because the worker's input-binding system
is layered: several internal features have to exist, in the right order, resolved from the right
places, before a converter is even considered. Each layer failing looks identical from the outside
("the parameter is `null`"), so this doc is written as a chronological trail — the order things
were discovered in is as important as the final answer, because the *next* missing-feature bug
will look the same and this trail shows how to diagnose it.

## Starting point: `HttpRequest req` itself was `null`

Before touching `[FromBody]` at all, the plain trigger parameter (`HttpRequest req`, no attribute)
was coming back `null`. That was the first sign that *something upstream* of parameter binding was
missing — since even the case with zero conversion logic involved was broken.

### Step 1 — `IFunctionInputBindingFeature` was never being invoked

Binding breakpoints revealed `TestFunctionBindingsFeatureProxy.Invoke` (phase 3's proxy) was
**only** hit for `get_InvocationResult`/`get_OutputBindingsInfo`, and only *after* the function
already ran. `get_InputData` was never called at all. That meant parameter binding — whatever
does it — wasn't running before the function method itself.

The responsible type is `Context.Features.IFunctionInputBindingFeature`
(`DefaultFunctionInputBindingFeature.BindFunctionInputAsync`), which the worker's own function
executor calls before invoking the method. `functionContext.Features.Get<IFunctionInputBindingFeature>()`
was returning `null` — nothing had ever put one into the features bag.

### Step 2 — feature resolution only works through a provider pipeline

The instinct was "just create one and `Set` it" — but that requires knowing how to construct
`DefaultFunctionInputBindingFeature` (it needs an internal `IConverterContextFactory`). The real
mechanism, found by reading `Microsoft.Azure.Functions.Worker.Context.Features.InvocationFeatures`
(the production `IInvocationFeatures` implementation) in the worker source, is:

```csharp
// production InvocationFeatures.Get<T>() (worker source, for reference)
public T? Get<T>()
{
    var type = typeof(T);
    if (!_features.TryGetValue(type, out object? feature))
    {
        if (_featureProviders.Any(t => t.TryCreate(type, out feature)) && !_features.TryAdd(type, feature!))
            feature = _features[type];
    }
    return feature is null ? default : (T)feature;
}
```

Production `Get<T>()` is **lazy and provider-backed**: if a feature isn't already cached, it asks
every registered `IInvocationFeatureProvider` (DI-registered, e.g. `DefaultBindingFeatureProvider`
for `IFunctionInputBindingFeature`) to try to create it, on demand, the first time it's asked for.
Our `TestInvocationFeatures` was a plain dictionary with no provider fallback at all — that's the
actual root cause, not a missing one-off `Set` call.

**Resolution:** give `TestInvocationFeatures.Get<T>()` the same two-step shape — check the local
cache, then fall back to provider-based creation — and cache what gets created so repeated `Get<T>()`
calls for the same invocation return the same instance (this matters: `DefaultFunctionInputBindingFeature`
caches its own bind result keyed by identity, and other worker code assumes `Get<T>()` is stable
within an invocation).

### Step 3 — one generic provider isn't enough: `IInputConversionFeature` needs a *different* provider

With `IFunctionInputBindingFeature` now resolvable, a **new** exception appeared:
`context.Features.Get<IInputConversionFeature>()` returning `null` from inside
`DefaultFunctionInputBindingFeature.BindFunctionInputAsync` itself. This looked like the same bug
class, but it wasn't quite — and this distinction is worth remembering:

- `IFunctionInputBindingFeature` is created by `DefaultBindingFeatureProvider`, which **does**
  implement the generic `IInvocationFeatureProvider` — so the phase-3-style provider loop finds it.
- `IInputConversionFeature` is created by `DefaultInputConversionFeatureProvider`, which implements
  a **different, strongly-typed interface**: `IInputConversionFeatureProvider.TryCreate(Type type,
  out IInputConversionFeature? feature)` — it is *not* an `IInvocationFeatureProvider` at all.

In production, nothing generic ever creates `IInputConversionFeature` — the gRPC
`InvocationHandler` explicitly resolves `IInputConversionFeatureProvider` from DI and seeds
`IInputConversionFeature` into the invocation features *before* invoking the pipeline, every time:

```csharp
// production InvocationHandler (worker source, for reference)
if (_inputConversionFeatureProvider.TryCreate(typeof(DefaultInputConversionFeature), out var conversion))
    invocationFeatures.Set<IInputConversionFeature>(conversion!);
```

Two options were on the table: hardcode a call to `IInputConversionFeatureProvider` specifically
(simple, but brittle — every future strongly-typed provider would need its own bespoke code path),
or **generalize**: since `TryCreate(Type, out TFeature)` is a *shape*, not a single named
interface, discover any provider matching that shape structurally, by reflection, without
depending on `IInputConversionFeatureProvider` by name.

**Resolution:** `WorkerInternals.TryCreateFeatureViaProvider<T>` — probes the worker assembly for
concrete types assignable to the requested feature interface and calls `TryCreate` for each
against a provider located by convention/discovery — as a second-tier fallback, tried only if the
generic `IInvocationFeatureProvider` loop (`TryCreateFeatureViaInvocationProvider<T>`) didn't find
anything. `TestInvocationFeatures.Get<T>()`'s final shape tries both, in that order, and caches
whichever succeeds.

## `[FromBody]` still didn't bind after all three features were in place

With `IFunctionInputBindingFeature` and `IInputConversionFeature` both resolvable,
`get_InputData` was finally observed firing — progress — but the `[FromBody] GreeterInput input`
parameter was still `null`. Two more issues, found in sequence:

### Step 4 — wrong `[FromBody]` attribute

The demo function used `Microsoft.AspNetCore.Mvc.FromBodyAttribute` (an ASP.NET Core MVC model-
binding attribute) instead of `Microsoft.Azure.Functions.Worker.Http.FromBodyAttribute` (the
isolated-worker attribute, which derives from `InputConverterAttribute` and points at
`FromBodyConverter`). The two attributes have the same short name, so this is an easy mistake to
repeat — the fix was a `using FromBodyAttribute = Microsoft.Azure.Functions.Worker.Http.FromBodyAttribute;`
alias so the compiler enforces the right one is used at every call site.

### Step 5 — parameter metadata carried no converter hint at all

Even with the right attribute, `FromBodyConverter` was never invoked. The reason traces back to
`TestFunctionDefinition.BuildParameters`: it built `FunctionParameter` instances with an **empty**
`Properties` bag for every parameter. `FunctionParameter.Properties` isn't where argument *values*
live (that's a common misreading of the type — it only carries **metadata hints** consumed during
conversion, such as `converterType`); but an empty bag means the conversion pipeline has no signal
at all that this parameter wants a specific converter, so it falls through to "unhandled" and the
parameter is left at its default (`null` for a reference type).

Production (`GrpcFunctionDefinition.GetAdditionalPropertiesDictionary`, from gRPC-supplied
metadata) populates this bag from the parameter's attributes:
- If `[InputConverter]`-derived (which `[FromBody]` is), set `PropertyBagKeys.ConverterType`.
- Otherwise, if there's an `[InputBindingAttribute]`/`[TriggerBindingAttribute]`, carry the
  binding attribute itself plus any `[InputConverter]` attributes declared *on the binding
  attribute's type*, and the configured `ConverterFallbackBehavior`.

**Resolution:** `TestFunctionDefinition.BuildParameterProperties` was rewritten to mirror this
exactly, reflecting over each parameter's attributes the same way `GrpcFunctionDefinition` does
over gRPC metadata.

### Step 6 — `converterType` has to be a `string`, not a `Type`

Even after populating `converterType`, conversion still silently skipped the custom converter. The
final bug: `properties["converterType"] = inputConverterAttr.ConverterType;` was storing a `Type`
instance. `DefaultInputConversionFeature` does:

```csharp
if (context.Properties.TryGetValue(PropertyBagKeys.ConverterType, out var converterTypeAssemblyQualifiedNameObj)
    && converterTypeAssemblyQualifiedNameObj is string converterTypeAssemblyQualifiedName)
```

— an explicit `is string` check. A `Type` value fails that check silently (no exception, just
"unhandled", which looks identical to "no converter configured at all"). The fix was one line:
store `inputConverterAttr.ConverterType.AssemblyQualifiedName!` instead of the `Type` itself.

With that, `[FromBody]` bound correctly end to end — `Phase3Tests.Function_CanPostWithJson` passed.

## A dead end worth recording: `IFromBodyConversionFeature` does not need manual seeding

While investigating step 4/5, `Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore.FromBodyConversionFeature`
looked like another candidate for the "missing feature" pattern from steps 1–3. It wasn't: reading
`FunctionsHttpProxyingMiddleware` (the worker's own ASP.NET Core integration middleware) showed it
already does `context.Features.Set<IFromBodyConversionFeature>(FromBodyConversionFeature.Instance)`
itself, unconditionally, once the `HttpContext` is attached — before our code or the function ever
runs. Manually seeding it again would have been redundant at best. **Not every missing-value bug
is a missing-feature bug** — sometimes the feature is already wired by existing middleware and the
actual defect is elsewhere (as it was here — the real defects were steps 4–6).

## `[FromServices]` was investigated and intentionally dropped

The original demo function used `[FromServices] IDummyService dummyService` on a method parameter,
mirroring ASP.NET Core MVC convention. There is no isolated-worker equivalent of parameter-level
service injection — searching the worker source and samples turned up nothing. The demo function
was changed to use **constructor injection** instead (`Greeter(ILogger<Greeter> logger, IDummyService
dummyService)`), which is the actual, supported isolated-worker pattern, and is already exercised
correctly by the DI container built in [phase 2](02-hosting-the-worker-inside-webapplicationfactory.md).
This is now a documented constraint rather than an open gap — see the cheat sheet in the journal
[README](README.md).

## Refactor pass: hardening what already worked

Once `[FromBody]` worked, a review pass (commit `8f6f3ed`) tightened several things before moving
on to the next binding scenario:

- **Reflection fragility in `TryCreateFeatureViaProvider`.** The first working version guessed the
  provider's type name by string convention (`$"{featureType.Namespace}.{featureType.Name}Provider"`).
  This is fragile — it depends on an undocumented worker naming convention that could change. It
  was replaced with **structural discovery**: scan the worker assembly for any interface exposing
  a method shaped exactly like `bool TryCreate(Type, out TFeature)` (matched by parameter count,
  parameter types, and an exact `out` type match), rather than assuming a name. The `object`-typed
  `IInvocationFeatureProvider.TryCreate` is excluded by construction, since an exact type match
  against a concrete feature type will never equal `object` — that generic case stays owned by
  `TryCreateFeatureViaInvocationProvider`.
- **Caching the discovery.** The structural scan is somewhat expensive (assembly-wide `GetTypes()`
  twice). A `ConcurrentDictionary<Type, (Type ProviderType, MethodInfo TryCreate, Type ConcreteType)>`
  cache means the scan only happens once per feature type for the process lifetime; subsequent
  calls go straight to the cached provider/method/concrete-type triple.
- **Route template matching.** `MatchFunction`'s route comparison was an exact/`StartsWith` string
  check against `/api/{route}`, which cannot match parameterized routes like `items/{id}`. This was
  replaced with ASP.NET Core's own `Microsoft.AspNetCore.Routing.Template.TemplateMatcher` /
  `TemplateParser` (available for free via the existing `Microsoft.AspNetCore.App` framework
  reference) — using a real routing engine instead of hand-rolled string comparisons, with parsed
  templates cached per route pattern. **Note:** this only fixed *matching* a templated route to a
  function; it does not yet populate route values (e.g. `id`) into `IFunctionBindingsFeature.InputData`
  or bind them into function parameters — that remains open, see below.
- **Demo function cleanup.** Removed leftover debug-only statements (`var method = req.Method;`)
  and narrative comments that were only useful during the debugging session itself.

## Outcome

- `[FromBody]` binds correctly, via the *real* worker conversion pipeline (not a shortcut/mock of
  conversion) — meaning any future `[FromBody]`-adjacent behavior (custom converters, converter
  fallback behavior, etc.) should "just work" the same way production does, as long as
  `TestFunctionDefinition` keeps populating parameter metadata correctly.
- `TestInvocationFeatures` now mirrors production's lazy, provider-backed feature resolution
  instead of being a static dictionary — this is a meaningful behavioral upgrade from phase 3, not
  just an implementation detail, since it's what makes *any* worker-internal feature resolvable
  without bespoke code per feature going forward.
- `[FromServices]` is confirmed unsupported by design (not a gap to fill later).

## Known follow-ups for the next binding-related session

- **Route parameter binding.** Template *matching* works; the matched route values still need to
  flow into `IFunctionBindingsFeature.InputData` (keyed by parameter/binding name) so a parameter
  like `int id` from `items/{id}` actually converts, the same way `[FromBody]` now does.
- **Query string / header binding**, if/when a scenario needs it — likely the same
  `InputData`-population mechanism as route values.
- Consider whether the `ResolvedProviders` cache in `WorkerInternals` needs any invalidation story
  if a test ever swaps out DI registrations of a provider mid-run (unlikely given one `IHost` per
  factory instance, but worth a note if that assumption ever changes).
