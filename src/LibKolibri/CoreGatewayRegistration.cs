using KolArbitrage.Core.Adventure;
using KolArbitrage.Core.Integration;

namespace LibKolibri;

/// <summary>Convenience composition helpers for the current Core adapters.</summary>
public static class CoreGatewayRegistration
{
    /// <summary>
    /// Registers an explicitly constructed approval-bound combat gateway.
    /// Combat is intentionally separate from read-adapter composition.
    /// </summary>
    public static LibKolibriRuntime RegisterCombatGateway(
        this LibKolibriRuntime runtime,
        ManualCombatGateway gateway)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(gateway);
        return runtime.Register<ICombatGateway>(new CoreCombatGateway(gateway));
    }

    /// <summary>
    /// Registers the read-only Core adapters under their stable LibKolibri
    /// contracts. The caller owns the underlying HTTP clients and their
    /// session lifetime.
    /// </summary>
    public static LibKolibriRuntime RegisterCoreReadAdapters(
        this LibKolibriRuntime runtime,
        IAccountReadClient account,
        IMallReadClient mall,
        ForeignBribePlanner coinmasters,
        DailyAdventurePlanner? adventure = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(mall);
        ArgumentNullException.ThrowIfNull(coinmasters);

        return runtime
            .Register<IAccountGateway>(new CoreAccountGateway(account))
            .Register<IMallGateway>(new CoreMallGateway(mall))
            .Register<IAdventureGateway>(new CoreAdventureGateway(adventure))
            .Register<ICoinmasterGateway>(new CoreCoinmasterGateway(coinmasters))
            // Keep the composition root safe by wiring only the local,
            // preview/read adapters. A live shop or transfer transport is
            // never inferred from a read client.
            .RegisterCoreM0M1Adapters(new CoreM0M1RegistrationOptions
            {
                Mall = mall,
                MallGateway = runtime.Get<IMallGateway>()
            });
    }

    /// <summary>
    /// Registers read adapters and the optional M0/M1 workflow services in a
    /// single call. The options default to preview-only mutation gateways.
    /// </summary>
    public static LibKolibriRuntime RegisterCoreReadAdapters(
        this LibKolibriRuntime runtime,
        IAccountReadClient account,
        IMallReadClient mall,
        ForeignBribePlanner coinmasters,
        DailyAdventurePlanner? adventure,
        CoreM0M1RegistrationOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);
        RegisterCoreReadAdapters(runtime, account, mall, coinmasters, adventure);
        // The existing overload has already installed safe defaults. Reapply
        // supplied options so callers can add catalog/snapshot/audit services.
        return runtime.RegisterCoreM0M1Adapters(options with { Mall = options.Mall ?? mall });
    }

    /// <summary>Read-adapter overload with M0/M1 options and default adventure policy.</summary>
    public static LibKolibriRuntime RegisterCoreReadAdapters(
        this LibKolibriRuntime runtime,
        IAccountReadClient account,
        IMallReadClient mall,
        ForeignBribePlanner coinmasters,
        CoreM0M1RegistrationOptions options)
        => RegisterCoreReadAdapters(runtime, account, mall, coinmasters, null, options);
}
