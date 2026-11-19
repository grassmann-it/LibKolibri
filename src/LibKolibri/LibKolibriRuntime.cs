namespace LibKolibri;

/// <summary>
/// Dependency-injection friendly composition root for LibKolibri consumers.
/// The first implementation delegates to KolArbitrage.Core; future adapters
/// can replace individual gateways without changing CLI/UI/MCP contracts.
/// </summary>
public sealed class LibKolibriRuntime
{
    private readonly Dictionary<Type, object> _services = [];

    /// <summary>Registers or replaces a service by its contract type.</summary>
    public LibKolibriRuntime Register<T>(T service) where T : class
    {
        ArgumentNullException.ThrowIfNull(service);
        _services[typeof(T)] = service;
        return this;
    }

    /// <summary>Returns a registered service or throws when it is unavailable.</summary>
    public T Get<T>() where T : class
        => _services.TryGetValue(typeof(T), out var service)
            ? (T)service
            : throw new InvalidOperationException($"LibKolibri service {typeof(T).Name} is not registered.");

    /// <summary>Attempts to resolve a service without throwing.</summary>
    public bool TryGet<T>(out T? service) where T : class
    {
        if (_services.TryGetValue(typeof(T), out var value))
        {
            service = (T)value;
            return true;
        }

        service = null;
        return false;
    }

    /// <summary>Returns the runtime name, version and registered modules.</summary>
    public KolibriRuntimeDescriptor Describe(string version = "0.1.0")
        => new("LibKolibri", version, _services.Values.OfType<IKolibriModule>().ToArray());
}
