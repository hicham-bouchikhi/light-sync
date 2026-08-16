using System.CommandLine;
using LightSync.Cli.Commands;
using LightSync.Core.Capture;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;

namespace LightSync.Cli;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        var configOption = new Option<string>("--config")
        {
            Description = "Path to the configuration file.",
            DefaultValueFactory = _ => ConfigurationPaths.ConfigFile,
        };

        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Capture and process only; print zone colours and frame rate, contact no device.",
        };

        var root = new RootCommand("Ambient-light synchronization for Linux/Wayland.");
        root.Options.Add(configOption);
        root.Options.Add(dryRunOption);

        var listDisplays = new Command("list-displays", "Show detected displays.");
        var listAdapters = new Command("list-adapters", "Show available device adapters.");

        root.Subcommands.Add(listDisplays);
        root.Subcommands.Add(listAdapters);

        using var lifetime = new ConsoleLifetime();

        root.SetAction((parse, _) =>
            Run(parse.GetValue(configOption)!, context =>
                parse.GetValue(dryRunOption)
                    ? DryRunCommand.RunAsync(context, new SyntheticScreenCapture(), lifetime.Token)
                    : Task.FromResult(ShowUsage(root))));

        listDisplays.SetAction((parse, _) =>
            Run(parse.GetValue(configOption)!, context => ListCommands.ListDisplaysAsync(context, lifetime.Token)));

        listAdapters.SetAction((parse, _) =>
            Run(parse.GetValue(configOption)!, context => Task.FromResult(ListCommands.ListAdapters(context))));

        return await root.Parse(args).InvokeAsync();
    }

    private static async Task<int> Run(string configPath, Func<CommandContext, Task<int>> action)
    {
        try
        {
            return await action(new CommandContext(configPath));
        }
        catch (ConfigurationException ex)
        {
            ConsoleUI.Error(ex.Message);
            return 1;
        }
        catch (DeviceException ex)
        {
            ConsoleUI.Error(ex.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
    }

    private static int ShowUsage(RootCommand root)
    {
        Console.WriteLine(root.Description);
        Console.WriteLine();
        Console.WriteLine("Run 'light-sync --help' to see the available commands.");
        return 0;
    }
}

/// <summary>
/// Turns Ctrl+C into cancellation instead of an abrupt exit, so the pipeline can shut down
/// and the device can be released cleanly.
/// </summary>
internal sealed class ConsoleLifetime : IDisposable
{
    private readonly CancellationTokenSource cts = new();

    public ConsoleLifetime()
    {
        Console.CancelKeyPress += OnCancelKeyPress;
    }

    public CancellationToken Token => cts.Token;

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        // Handle the first Ctrl+C gracefully; a second one is left to terminate the process.
        if (!cts.IsCancellationRequested)
        {
            e.Cancel = true;
            cts.Cancel();
        }
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
        cts.Dispose();
    }
}
