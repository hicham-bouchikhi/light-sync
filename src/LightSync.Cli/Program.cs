using System.CommandLine;
using LightSync.Cli.Commands;
using LightSync.Core.Capture;
using LightSync.Core.Capture.Wayland;
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

        var aiHelpOption = new Option<bool>("--ai-help")
        {
            Description = "Print a task-oriented guide for an AI agent or unfamiliar operator.",
        };

        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Capture and process only; print zone colours and frame rate, contact no device.",
        };

        var root = new RootCommand("Ambient-light synchronization for Linux/Wayland.");
        root.Options.Add(configOption);
        root.Options.Add(dryRunOption);
        root.Options.Add(aiHelpOption);

        var listDisplays = new Command("list-displays", "Show detected displays.");
        var listAdapters = new Command("list-adapters", "Show available device adapters.");
        var discover = new Command("discover", "Find Nanoleaf devices on the local network.");

        var hostOption = new Option<string?>("--host")
        {
            Description = "Device host or IP. Discovered automatically when omitted.",
        };
        var saveTokenOption = new Option<bool>("--save")
        {
            Description = "Save the token to the local secrets file instead of printing it.",
        };
        var pair = new Command("pair", "Obtain an auth token from a Nanoleaf device.");
        pair.Options.Add(hostOption);
        pair.Options.Add(saveTokenOption);

        var setup = new Command("setup", "Choose the capture area and validate the device.");
        var selectArea = new Command("select-area", "Choose the capture area again.");
        var run = new Command("run", "Start the synchronization pipeline.");
        var stop = new Command("stop", "Stop a running instance.");
        var diagnostics = new Command("diagnostics", "Probe the environment, configuration and device.");
        var testDevice = new Command("test-device", "Connect to the configured device and report capabilities.");

        var colorArgument = new Argument<string>("colour")
        {
            Description = "One of: " + string.Join(", ", LightSync.Core.Colors.ColorConstants.NamedColors),
        };
        var testColor = new Command("test-color", "Set the device to a static colour.");
        testColor.Arguments.Add(colorArgument);

        var secondsOption = new Option<int>("--seconds")
        {
            Description = "How long to stream for.",
            DefaultValueFactory = _ => 10,
        };
        var testStream = new Command("test-stream", "Stream a moving pattern to the device.");
        testStream.Options.Add(secondsOption);

        root.Subcommands.Add(listDisplays);
        root.Subcommands.Add(listAdapters);
        root.Subcommands.Add(discover);
        root.Subcommands.Add(pair);
        root.Subcommands.Add(setup);
        root.Subcommands.Add(selectArea);
        root.Subcommands.Add(run);
        root.Subcommands.Add(stop);
        root.Subcommands.Add(diagnostics);
        root.Subcommands.Add(testDevice);
        root.Subcommands.Add(testColor);
        root.Subcommands.Add(testStream);

        root.SetAction((parse, token) =>
            Run(parse.GetValue(configOption)!, context =>
                (parse.GetValue(aiHelpOption), parse.GetValue(dryRunOption)) switch
                {
                    (true, _) => Task.FromResult(AiHelp.Write(root)),
                    (_, true) => DryRunCommand.RunAsync(context, token),
                    _ => Task.FromResult(ShowUsage(root)),
                }));

        listDisplays.SetAction((parse, token) =>
            Run(parse.GetValue(configOption)!, context => ListCommands.ListDisplaysAsync(context, token)));

        listAdapters.SetAction((parse, _) =>
            Run(parse.GetValue(configOption)!, context => Task.FromResult(ListCommands.ListAdapters(context))));

        discover.SetAction((parse, token) =>
            Run(parse.GetValue(configOption)!, _ => NanoleafCommands.DiscoverAsync(token)));

        setup.SetAction((parse, token) =>
            Run(parse.GetValue(configOption)!, context =>
                SetupCommand.RunAsync(context, areaOnly: false, token)));

        selectArea.SetAction((parse, token) =>
            Run(parse.GetValue(configOption)!, context =>
                SetupCommand.RunAsync(context, areaOnly: true, token)));

        run.SetAction((parse, token) =>
            Run(parse.GetValue(configOption)!, context => RunCommand.RunAsync(
                context,
                config => new PortalScreenCapturer(config.Capture.RestoreToken),
                token)));

        stop.SetAction((_, _) => Task.FromResult(StopCommand.Run()));

        diagnostics.SetAction((parse, token) =>
            Run(parse.GetValue(configOption)!, context =>
                DiagnosticsCommand.RunAsync(context, token)));

        testDevice.SetAction((parse, token) =>
            Run(parse.GetValue(configOption)!, context =>
                TestCommands.TestDeviceAsync(context, token)));

        testColor.SetAction((parse, token) =>
            Run(parse.GetValue(configOption)!, context =>
                TestCommands.TestColorAsync(context, parse.GetValue(colorArgument)!, token)));

        testStream.SetAction((parse, token) =>
            Run(parse.GetValue(configOption)!, context =>
                TestCommands.TestStreamAsync(context, parse.GetValue(secondsOption), token)));

        pair.SetAction((parse, token) =>
            Run(parse.GetValue(configOption)!, context => NanoleafCommands.PairAsync(
                context,
                parse.GetValue(hostOption),
                parse.GetValue(saveTokenOption),
                token)));

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

