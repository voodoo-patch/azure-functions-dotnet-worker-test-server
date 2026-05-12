using System.Collections;
using Microsoft.Azure.Functions.Worker;

namespace AzureFunctionsDotNetWorkerTestServer;

public class TestInvocationFeatures : IInvocationFeatures
{
    private readonly Dictionary<Type, object> _features = new();

    public T? Get<T>() => _features.TryGetValue(typeof(T), out var f) ? (T)f : default;

    public void Set<T>(T instance) => _features[typeof(T)] = instance!;

    public IEnumerator<KeyValuePair<Type, object>> GetEnumerator() => _features.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _features.GetEnumerator();
}