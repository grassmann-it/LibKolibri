using KolArbitrage.Core.Accounts;
using KolArbitrage.Core.Integration;
using KolArbitrage.Core.Pricing;

namespace LibKolibri;

/// <summary>
/// Resolves LibKolibri services for one of the configured KoL account roles.
///
/// The existing <see cref="LibKolibriRuntime"/> remains the process-wide
/// fallback. Role lookups through <see cref="TryGet{T}"/> are overlays:
/// services registered for a role win, while services registered on the base
/// runtime continue to work for both roles. This keeps old hosts
/// source-compatible while allowing Main and Mallbot to use different
/// authenticated read clients.
/// </summary>
public sealed class RoleScopedRuntime
{
    private readonly LibKolibriRuntime _fallback;
    private readonly Dictionary<AccountRole, LibKolibriRuntime> _scopes = [];
    private readonly object _gate = new();

    /// <summary>Creates a role facade over an optional process-wide runtime.</summary>
    public RoleScopedRuntime(LibKolibriRuntime? fallback = null)
        => _fallback = fallback ?? new LibKolibriRuntime();

    /// <summary>The unscoped runtime used when a role has no override.</summary>
    public LibKolibriRuntime Fallback => _fallback;

    /// <summary>Returns the explicitly registered runtime overlay for a role.</summary>
    public LibKolibriRuntime ForRole(AccountRole role)
    {
        lock (_gate)
        {
            if (_scopes.TryGetValue(role, out var scope))
                return scope;
            return _fallback;
        }
    }

    /// <summary>Alias used by hosts that call account roles "accounts".</summary>
    public LibKolibriRuntime ForAccount(AccountRole role) => ForRole(role);

    /// <summary>
    /// Registers or replaces one service in a role overlay. The supplied
    /// instance is retained by the runtime; credentials remain inside the
    /// adapter/session boundary and are never part of this facade.
    /// </summary>
    public RoleScopedRuntime Register<T>(AccountRole role, T service) where T : class
    {
        ArgumentNullException.ThrowIfNull(service);
        lock (_gate)
        {
            if (!_scopes.TryGetValue(role, out var scope))
                _scopes[role] = scope = new LibKolibriRuntime();
            scope.Register(service);
            RegisterKnownContracts(scope, service);
        }
        return this;
    }

    /// <summary>Registers a complete runtime overlay for a role.</summary>
    public RoleScopedRuntime Register(AccountRole role, LibKolibriRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        lock (_gate) _scopes[role] = runtime;
        return this;
    }

    /// <summary>Checks for a role-specific service, then the fallback service.</summary>
    public bool TryGet<T>(AccountRole role, out T? service) where T : class
    {
        lock (_gate)
        {
            if (_scopes.TryGetValue(role, out var scope)
                && scope.TryGet<T>(out service))
                return true;
        }

        return _fallback.TryGet(out service);
    }

    /// <summary>Gets a role-specific service or throws the normal runtime error.</summary>
    public T Get<T>(AccountRole role) where T : class
        => TryGet<T>(role, out var service)
            ? service!
            : throw new InvalidOperationException($"LibKolibri service {typeof(T).Name} is not registered for the {role} role.");

    /// <summary>Returns all explicitly configured role overlays.</summary>
    public IReadOnlyCollection<AccountRole> Roles
    {
        get
        {
            lock (_gate) return _scopes.Keys.ToArray();
        }
    }

    private static void RegisterKnownContracts<T>(LibKolibriRuntime scope, T service)
        where T : class
    {
        // Most callers naturally register a concrete fake/client. Preserve
        // that key and also expose the stable contract keys consumed by MCP.
        if (service is IAccountReadClient accountRead)
            scope.Register<IAccountReadClient>(accountRead);
        if (service is IMallReadClient mallRead)
            scope.Register<IMallReadClient>(mallRead);
        if (service is IAccountGateway account)
            scope.Register<IAccountGateway>(account);
        if (service is IMallGateway mall)
            scope.Register<IMallGateway>(mall);
        if (service is IAdventureGateway adventure)
            scope.Register<IAdventureGateway>(adventure);
        if (service is ICombatGateway combat)
            scope.Register<ICombatGateway>(combat);
        if (service is IPriceProvider prices)
            scope.Register<IPriceProvider>(prices);
        if (service is PricingEngine pricing)
            scope.Register<PricingEngine>(pricing);
    }
}

/// <summary>
/// Short facade name for applications that expose a runtime/account boundary
/// rather than the lower-level role-scoped implementation name.
/// </summary>
public sealed class KolibriRuntimeFacade
{
    private readonly RoleScopedRuntime _runtime;

    public KolibriRuntimeFacade(RoleScopedRuntime runtime)
        => _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public KolibriRuntimeFacade(LibKolibriRuntime? runtime = null)
        : this(new RoleScopedRuntime(runtime))
    {
    }

    public RoleScopedRuntime Roles => _runtime;
    public LibKolibriRuntime Fallback => _runtime.Fallback;
    public LibKolibriRuntime ForRole(AccountRole role) => _runtime.ForRole(role);
    public LibKolibriRuntime ForAccount(AccountRole role) => _runtime.ForAccount(role);
    public bool TryGet<T>(AccountRole role, out T? service) where T : class => _runtime.TryGet(role, out service);
    public T Get<T>(AccountRole role) where T : class => _runtime.Get<T>(role);

    public KolibriRuntimeFacade Register<T>(AccountRole role, T service) where T : class
    {
        _runtime.Register(role, service);
        return this;
    }
}

/// <summary>Convenience extensions for building a role-scoped facade.</summary>
public static class RoleScopedRuntimeExtensions
{
    public static RoleScopedRuntime ScopeByRole(this LibKolibriRuntime runtime)
        => new(runtime);

    public static RoleScopedRuntime ForRoles(this LibKolibriRuntime runtime)
        => new(runtime);
}
