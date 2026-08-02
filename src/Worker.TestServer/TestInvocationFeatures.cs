using System.Collections;
using Microsoft.Azure.Functions.Worker;

namespace Azure.Functions.Worker.TestServer;

public class TestInvocationFeatures(IServiceProvider serviceProvider) : IInvocationFeatures
{
    private readonly Dictionary<Type, object> _features = new();

    public T? Get<T>()
    {
        if (_features.TryGetValue(typeof(T), out var f))
            return (T)f;

        if (WorkerInternals.TryCreateFeatureViaInvocationProvider<T>(serviceProvider, out var resolvedFeature) ||
            WorkerInternals.TryCreateFeatureViaProvider(serviceProvider, out resolvedFeature))
        {
            _features[typeof(T)] = resolvedFeature;
            return resolvedFeature;
        }

        return default;
    }

    public void Set<T>(T instance) => _features[typeof(T)] = instance!;

    public IEnumerator<KeyValuePair<Type, object>> GetEnumerator() => _features.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _features.GetEnumerator();
}
