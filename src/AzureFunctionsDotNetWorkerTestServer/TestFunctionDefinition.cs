using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Converters;
using Microsoft.Azure.Functions.Worker.Core.FunctionMetadata;

namespace AzureFunctionsDotNetWorkerTestServer;

/// <summary>
/// A test-only <see cref="FunctionDefinition"/> built from source-generated <see cref="IFunctionMetadata"/>.
/// Populates <see cref="InputBindings"/>, <see cref="OutputBindings"/>, and <see cref="Parameters"/>
/// by parsing the raw binding JSON and reflecting on the function's entry point method.
/// </summary>
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
        PathToAssembly = Path.Combine(
            Path.GetDirectoryName(typeof(TestFunctionDefinition).Assembly.Location)!,
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

        var bindingDirection = direction.Equals("In", StringComparison.OrdinalIgnoreCase)
            ? BindingDirection.In
            : BindingDirection.Out;

        var builder = ImmutableDictionary.CreateBuilder<string, BindingMetadata>();
        foreach (var raw in rawBindings)
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (!root.TryGetProperty("direction", out var dir)) continue;
            if (!dir.GetString()?.Equals(direction, StringComparison.OrdinalIgnoreCase) ?? true) continue;

            var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            builder[name] = new TestBindingMetadata(name, type, bindingDirection);
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<FunctionParameter> BuildParameters(IFunctionMetadata metadata)
    {
        if (!TryGetEndpointMethodInfo(metadata, out var method))
        {
            return ImmutableArray<FunctionParameter>.Empty;
        }

        return
        [
            ..method.GetParameters()
                .Where(p => p.Name is not null)
                .Select(p => new FunctionParameter(
                    p.Name!,
                    p.ParameterType,
                    p.HasDefaultValue,
                    p.DefaultValue,
                    BuildParameterProperties(p)))
        ];
    }

    private static IReadOnlyDictionary<string, object> BuildParameterProperties(ParameterInfo parameter)
    {
        var properties = new Dictionary<string, object>();

        // Add custom attributes as properties
        foreach (var attr in parameter.GetCustomAttributes())
        {
            // if InputConverterAttribute is present, then add the <see cref="PropertyBagKeys.ConverterType"> property to the dictionary
            if (attr is InputConverterAttribute inputConverterAttr)
            {
                properties["converterType"] = inputConverterAttr.ConverterType.AssemblyQualifiedName!;
            }
        }

        return properties.ToImmutableDictionary();
    }

    private static bool TryGetEndpointMethodInfo(IFunctionMetadata metadata, [NotNullWhen(true)] out MethodInfo? method)
    {
        if (metadata.EntryPoint is null || metadata.ScriptFile is null)
        {
            method = null;
            return false;
        }

        // EntryPoint format: "Namespace.ClassName.MethodName"
        var lastDot = metadata.EntryPoint.LastIndexOf('.');
        var typeName = metadata.EntryPoint[..lastDot];
        var methodName = metadata.EntryPoint[(lastDot + 1)..];

        var assemblyName = Path.GetFileNameWithoutExtension(metadata.ScriptFile);
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == assemblyName);

        if (assembly is null)
        {
            method = null;
            return false;
        }

        method = assembly.GetType(typeName)?.GetMethod(methodName);

        return method is not null;
    }
}