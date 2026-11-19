using KolArbitrage.Core.Accounts;
using KolArbitrage.Core.Integration;


namespace LibKolibri;

/// <summary>
/// Builds the MCP read runtime from the encrypted role sessions configured by
/// the host. Tool arguments carry only the role name; cookies and tokens stay
/// inside the authenticated HTTP clients created here.
/// </summary>
public static class McpRuntimeBootstrap
{
    private static readonly Uri DefaultEndpoint =
        new("https://www.kingdomofloathing.com/", UriKind.Absolute);

    /// <summary>
    /// Loads configured Main and Mallbot sessions and registers one account and
    /// Mall reader per role. Missing profiles or credentials are skipped so an
    /// MCP process can still serve local planning tools.
    /// </summary>
    public static async Task<RoleScopedRuntime> CreateConfiguredAsync(
        AccountSessionManager? sessions = null,
        CancellationToken cancellationToken = default)
    {
        sessions ??= AccountSessionManager.CreateDefault();
        var runtime = new RoleScopedRuntime();

        foreach (var role in Enum.GetValues<AccountRole>())
        {
            var session = await sessions.GetSessionAsync(role, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (session is null || string.IsNullOrWhiteSpace(session.Credentials.CookieHeader))
                continue;

            var endpoint = session.Profile.Endpoint ?? DefaultEndpoint;
            if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps)
                continue;

            var http = KoLLoginClient.CreateAuthenticatedClient(session.Credentials, endpoint);
            var account = new KoLAccountReadClient(http, endpoint);
            var mall = new KoLMallReadClient(http, endpoint);
            runtime.Register<IAccountReadClient>(role, account);
            runtime.Register<IMallReadClient>(role, mall);
            runtime.Register<IAccountGateway>(role, new CoreAccountGateway(account));
            runtime.Register<IMallGateway>(role, new CoreMallGateway(mall));
        }

        return runtime;
    }

    /// <summary>Compatibility spelling for hosts that call this a factory.</summary>
    public static Task<RoleScopedRuntime> CreateAsync(
        AccountSessionManager? sessions = null,
        CancellationToken cancellationToken = default)
        => CreateConfiguredAsync(sessions, cancellationToken);
}
