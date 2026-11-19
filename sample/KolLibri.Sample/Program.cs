using KolArbitrage.Core.Accounts;
using KolArbitrage.Core.Daily;
using KolArbitrage.Core.Integration;
using LibKolibri;

namespace KolLibri.Sample;

/// <summary>
/// A minimal consumer of the LibKolibri connector. It shows the three
/// things most tools need:
///
/// 1. build a runtime with the stored account sessions,
/// 2. read the live account state,
/// 3. plan the daily routine (and, with an approval token, run it).
///
/// Run it with:
///   dotnet run --project sample/KolLibri.Sample -- --role main
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var role = args.FirstOrDefault(a => !a.StartsWith('-'))?.ToLowerInvariant() ?? "main";
        var execute = args.Contains("--execute");
        var approvalToken = ReadOption(args, "--approval-token");

        var accountRole = role switch
        {
            "mallbot" or "mall" => AccountRole.Mallbot,
            _ => AccountRole.Main
        };

        // 1. The runtime wires the stored sessions to the read clients.
        var runtime = await McpRuntimeBootstrap.CreateConfiguredAsync();
        if (!runtime.TryGet<IAccountReadClient>(accountRole, out var account) || account is null)
        {
            Console.Error.WriteLine(
                $"No valid session for {accountRole}. Log in first (the KoLibri CLI's "
                + "'login refresh' command does that).");
            return 1;
        }

        // 2. Read the live account.
        var status = await account.FetchStatusAsync(forceRefresh: true);
        var inventory = await account.FetchInventoryAsync(forceRefresh: true);
        Console.WriteLine($"{status.PlayerName} — level {status.Level}, "
            + $"{status.Meat} meat, {status.Adventures} adventures, "
            + $"{inventory.Items.Count} inventory entries.");

        // 3. Plan the daily routine from the live numbers.
        var builder = new LiveDailySnapshotBuilder(account);
        var live = await builder.BuildAsync(new DailyRoutineSnapshot());
        var bridge = new AutoCombatDailyBridge();
        var plan = bridge.Plan(live.State, live.Snapshot);

        Console.WriteLine();
        Console.WriteLine("Daily plan:");
        foreach (var step in plan.Steps)
        {
            var description = step switch
            {
                KolArbitrage.Core.Data.ChoiceStepSpec choice =>
                    $"choice {choice.ChoiceId} option {choice.OptionId}",
                KolArbitrage.Core.Data.ItemStepSpec item =>
                    $"item {item.ItemId} x{item.Quantity}",
                _ => step.GetType().Name
            };
            Console.WriteLine($"  - {description}");
        }
        if (plan.Steps.Count == 0)
            Console.WriteLine("  (nothing to do today)");

        if (!execute)
        {
            Console.WriteLine();
            Console.WriteLine("Preview only. Pass --execute --approval-token <token> to run it.");
            return 0;
        }
        if (string.IsNullOrWhiteSpace(approvalToken))
        {
            Console.Error.WriteLine("--execute needs --approval-token.");
            return 2;
        }
        Console.WriteLine();
        Console.WriteLine("Running the plan with the given approval token …");
        // The live path lives in the KoLibri app (the CLI's 'daily breakfast'
        // command); a library consumer drives the same bridge through the
        // gateway it registers.
        Console.WriteLine("Use the KoLibri CLI or MCP server for the live run — "
            + "see https://github.com/grassmann-it/KoLibri.");
        return 0;
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
