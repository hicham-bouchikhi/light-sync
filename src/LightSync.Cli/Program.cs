using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using LightSync.Cli.Commands;
using LightSync.Core.Audio;
using LightSync.Core.Capture;
using LightSync.Core.Capture.Wayland;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;

namespace LightSync.Cli;

internal static class Program
{
    internal static Task<int> Main(string[] args) => CreateCommand().Parse(args).InvokeAsync();

    internal static RootCommand CreateCommand()
    {
        var configOption = new Option<string>("--config")
        {
            Description = "Path to the configuration file.",
            DefaultValueFactory = _ => ConfigurationPaths.ConfigFile,
            Recursive = true,
        };
        var profilesOption = new Option<string?>("--profiles")
        {
            Description = "Device profiles file; defaults to devices.json beside --config.",
            Recursive = true,
        };
        var deviceOption = new Option<string[]>("--device")
        {
            Description = "Profile ID or exact name. Repeat for multiple sync outputs; default: all enabled profiles.",
            Recursive = true,
        };
        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Preview output in the terminal without connecting to any device.",
            Recursive = true,
        };
        var aiHelpOption = new Option<bool>("--ai-help") { Description = "Print a task-oriented operator guide." };
        var root = new RootCommand("Ambient-light synchronization for Linux/Wayland.");
        root.Options.Add(configOption);
        root.Options.Add(profilesOption);
        root.Options.Add(deviceOption);
        root.Options.Add(dryRunOption);
        root.Options.Add(aiHelpOption);

        void Bind(Command command, Func<ParseResult, CommandContext, CancellationToken, Task<int>> action)
        {
            command.SetAction((parse, token) => Run(new CommandContext(parse.GetValue(configOption)!,
                parse.GetValue(profilesOption), parse.GetValue(deviceOption)), context =>
                {
                    if (parse.GetValue(dryRunOption) && command != root && command.Name != "run")
                    {
                        throw new ConfigurationException("--dry-run is supported alone or with 'run'.");
                    }
                    return action(parse, context, token);
                }));
        }
        Command Add(string name, string description)
        {
            var command = new Command(name, description);
            root.Subcommands.Add(command);
            return command;
        }
        Bind(root, (parse, context, token) => parse.GetValue(aiHelpOption) ? Task.FromResult(AiHelp.Write(root))
            : parse.GetValue(dryRunOption) ? RunCommand.RunAsync(context, new RunOptions { Source = "screen", Preview = true }, token)
            : Task.FromResult(ShowUsage(root)));
        Bind(Add("list-displays", "Show detected displays."), (_, context, token) => ListCommands.ListDisplaysAsync(context, token));
        Bind(Add("list-adapters", "Show available device adapters."), (_, context, _) => Task.FromResult(ListCommands.ListAdapters(context)));
        Bind(Add("list-audio-sources", "Show playback monitor sources used by audio sync and screen intensity."), async (_, _, token) =>
        {
            foreach (var source in await PulseAudioSources.ListAsync(token))
            {
                Console.WriteLine(source);
            }
            return 0;
        });

        var discover = Add("discover", "Find Nanoleaf lights or OpenRGB PC components.");
        var discoveryAdapter = new Option<string>("--adapter") { DefaultValueFactory = _ => "nanoleaf", Description = "nanoleaf or openrgb." };
        var discoveryHost = new Option<string?>("--host") { Description = "OpenRGB SDK server host." };
        var discoveryPort = new Option<int?>("--port") { Description = "OpenRGB SDK server port." };
        var saveDiscovered = new Option<bool>("--save") { Description = "Add discovered profiles to the desktop/CLI profile store." };
        discover.Options.Add(discoveryAdapter);
        discover.Options.Add(discoveryHost);
        discover.Options.Add(discoveryPort);
        discover.Options.Add(saveDiscovered);
        Bind(discover, (parse, context, token) => DiscoverCommand.RunAsync(context, parse.GetValue(discoveryAdapter)!,
            parse.GetValue(discoveryHost), parse.GetValue(discoveryPort), parse.GetValue(saveDiscovered), token));

        var pair = Add("pair", "Pair a Nanoleaf profile, with its configured host, port and token storage.");
        var host = new Option<string?>("--host") { Description = "Nanoleaf host; defaults to the selected profile or discovery." };
        var saveToken = new Option<bool>("--save") { Description = "Save the token locally with owner-only permissions." };
        pair.Options.Add(host);
        pair.Options.Add(saveToken);
        Bind(pair, (parse, context, token) => NanoleafCommands.PairAsync(context, parse.GetValue(host), parse.GetValue(saveToken), token));
        Bind(Add("setup", "Choose the capture area and validate the selected device."), (_, context, token) => SetupCommand.RunAsync(context, false, token));
        Bind(Add("select-area", "Choose and save a new screen / region."), (_, context, token) => SetupCommand.RunAsync(context, true, token));

        var run = Add("run", "Start audio, screen or lighting sync on the selected profiles.");
        var source = new Option<string>("--source") { DefaultValueFactory = _ => "audio", Description = "audio, screen or ambient." };
        var audioSource = new Option<string?>("--audio-source") { Description = "Playback monitor for audio sync or screen intensity." };
        var demo = new Option<bool>("--demo") { Description = "Use the desktop's generated screen demo." };
        var preview = new Option<bool>("--preview") { Description = "Show the visualizer / LED strip without contacting devices." };
        var pick = new Option<bool>("--select-area") { Description = "Open a fresh screen picker, use the whole selection and save it." };
        var runSeconds = new Option<int>("--seconds") { Description = "Stop and black out after this many seconds; 0 runs until stopped." };
        var watch = new Option<bool>("--watch") { DefaultValueFactory = _ => true, Description = "Reload live audio tuning, audio device selection and screen intensity from saved settings." };
        var overrides = new Option<string[]>("--set") { Description = "Override a configuration key for this run, e.g. --set audio.gain=15. Repeatable." };
        run.Options.Add(source);
        run.Options.Add(audioSource);
        run.Options.Add(demo);
        run.Options.Add(preview);
        run.Options.Add(pick);
        run.Options.Add(runSeconds);
        run.Options.Add(watch);
        run.Options.Add(overrides);
        Bind(run, (parse, context, token) => RunCommand.RunAsync(context, new RunOptions
        {
            Source = parse.GetValue(source)!, AudioSource = parse.GetValue(audioSource),
            Demo = parse.GetValue(demo), Preview = parse.GetValue(preview) || parse.GetValue(dryRunOption),
            SelectArea = parse.GetValue(pick), Seconds = parse.GetValue(runSeconds),
            Watch = parse.GetValue(watch), Settings = parse.GetValue(overrides) ?? [],
        }, token));
        Bind(Add("stop", "Stop the running CLI instance and black out its outputs."), (_, context, _) => Task.FromResult(StopCommand.Run(context.PidPath)));
        Bind(Add("blackout", "Black out and disconnect selected devices."), (_, context, token) => ProfileCommands.BlackoutAsync(context, token));
        Bind(Add("diagnostics", "Probe the environment, configuration and selected device."), (_, context, token) => DiagnosticsCommand.RunAsync(context, token));
        Bind(Add("test-device", "Connect to the selected profile and report capabilities."), (_, context, token) => TestCommands.TestDeviceAsync(context, token));

        var brightness = Add("brightness", "Apply and save the selected profile's master brightness.");
        var percent = new Argument<int>("percent") { Description = "0–100; separate from audio and screen RGB brightness." };
        brightness.Arguments.Add(percent);
        Bind(brightness, (parse, context, token) => ProfileCommands.BrightnessAsync(context, parse.GetValue(percent), token));

        var testColor = Add("test-color", "Stream exact RGB to all LEDs or a selected LED; bypasses colour processing.");
        var color = new Argument<string>("colour") { Description = "Named colour, #RRGGBB, or R,G,B." };
        var zone = new Option<int?>("--zone") { Description = "LED number (1-based). Omit to set every LED." };
        var isolate = new Option<bool>("--isolate") { Description = "Black out every other LED." };
        var background = new Option<string>("--background") { DefaultValueFactory = _ => "black", Description = "Colour for the other LEDs in a selected-LED test." };
        var holdSeconds = new Option<int>("--seconds") { DefaultValueFactory = _ => 10, Description = "Hold the test frame for this many seconds; 0 until stopped." };
        var leds = new Option<string[]>("--led") { Description = "Set individual LEDs as NUMBER=COLOUR (1-based), repeatable for an exact multi-LED frame." };
        testColor.Arguments.Add(color);
        testColor.Options.Add(zone);
        testColor.Options.Add(isolate);
        testColor.Options.Add(background);
        testColor.Options.Add(holdSeconds);
        testColor.Options.Add(leds);
        Bind(testColor, (parse, context, token) => TestCommands.TestColorAsync(context, parse.GetValue(color)!,
            parse.GetValue(zone), parse.GetValue(isolate), parse.GetValue(background)!, parse.GetValue(holdSeconds),
            parse.GetValue(leds) ?? [], token));
        var testStream = Add("test-stream", "Stream a moving rainbow to the selected profile.");
        var streamSeconds = new Option<int>("--seconds") { DefaultValueFactory = _ => 10, Description = "Stream duration in seconds." };
        testStream.Options.Add(streamSeconds);
        Bind(testStream, (parse, context, token) => TestCommands.TestStreamAsync(context, parse.GetValue(streamSeconds), token));
        var chase = Add("test-chase", "Isolate each LED in order to verify physical addresses and RGB channels.");
        var chaseColor = new Option<string>("--colour") { DefaultValueFactory = _ => "white", Description = "Named colour, #RRGGBB, or R,G,B." };
        var milliseconds = new Option<int>("--milliseconds") { DefaultValueFactory = _ => 700, Description = "Hold time per LED; frames refresh every 100 ms." };
        chase.Options.Add(chaseColor);
        chase.Options.Add(milliseconds);
        Bind(chase, (parse, context, token) => TestCommands.ChaseAsync(context, parse.GetValue(chaseColor)!, parse.GetValue(milliseconds), token));

        var configuration = Add("config", "Show or change the same tuning configuration used by the desktop.");
        Bind(configuration, (_, context, token) => ConfigCommand.ShowAsync(context, token));
        var show = new Command("show", "Show the configuration with defaults applied.");
        configuration.Subcommands.Add(show);
        Bind(show, (_, context, token) => ConfigCommand.ShowAsync(context, token));
        var set = new Command("set", "Validate and save one key; live settings are picked up by a running CLI session.");
        var key = new Argument<string>("key") { Description = "Dotted key, e.g. audio.mode or screen.audioIntensityEnabled." };
        var value = new Argument<string>("value") { Description = "Value; fractions for smoothing/brightness, JSON arrays for orders." };
        set.Arguments.Add(key);
        set.Arguments.Add(value);
        configuration.Subcommands.Add(set);
        Bind(set, (parse, context, token) => ConfigCommand.SetAsync(context, parse.GetValue(key)!, parse.GetValue(value)!, token));

        var devices = Add("devices", "Manage the shared desktop and CLI device profiles.");
        Bind(devices, (_, context, token) => ProfileCommands.ListAsync(context, token));
        var list = new Command("list", "List IDs, names, adapters, sync selection and master brightness.");
        devices.Subcommands.Add(list);
        Bind(list, (_, context, token) => ProfileCommands.ListAsync(context, token));
        var profileDetails = new Command("show", "Show the selected profile's saved connection settings without connecting.");
        devices.Subcommands.Add(profileDetails);
        Bind(profileDetails, (_, context, token) => ProfileCommands.ShowAsync(context, token));
        foreach (var adding in new[] { true, false })
        {
            var edit = new Command(adding ? "add" : "update", adding ? "Add a profile." : "Edit the profile selected with --device.");
            var name = new Option<string?>("--name") { Description = "Display name." };
            var adapter = new Option<string?>("--adapter") { Description = "nanoleaf, openrgb, fake, wled or hue." };
            var settings = new Option<string[]>("--setting") { Description = "Adapter key=value, repeatable (host, port, ledMapping, controllerName, serial, location, etc.)." };
            var unset = new Option<string[]>("--unset-setting") { Description = "Remove an adapter setting, repeatable." };
            var enabled = new Option<bool?>("--enabled") { Description = "Include in default audio/screen sync." };
            var master = new Option<int?>("--brightness") { Description = "Saved master brightness, 0–100." };
            edit.Options.Add(name);
            edit.Options.Add(adapter);
            edit.Options.Add(settings);
            edit.Options.Add(unset);
            edit.Options.Add(enabled);
            edit.Options.Add(master);
            devices.Subcommands.Add(edit);
            Bind(edit, (parse, context, token) => ProfileCommands.SaveAsync(context, adding, parse.GetValue(name),
                parse.GetValue(adapter), parse.GetValue(settings) ?? [], parse.GetValue(unset) ?? [],
                parse.GetValue(enabled), parse.GetValue(master), token));
        }
        foreach (var enabling in new[] { true, false })
        {
            var toggle = new Command(enabling ? "enable" : "disable", "Change the selected profile's sync selection, including a running audio session.");
            devices.Subcommands.Add(toggle);
            Bind(toggle, (_, context, token) => ProfileCommands.SaveAsync(context, false, null, null, [], [], enabling, null, token));
        }
        var remove = new Command("remove", "Remove the selected profile; running audio releases it and continues on the remaining devices.");
        devices.Subcommands.Add(remove);
        Bind(remove, (_, context, token) => ProfileCommands.RemoveAsync(context, token));
        return root;
    }

    private static async Task<int> Run(CommandContext context, Func<CommandContext, Task<int>> action)
    {
        try
        {
            return await action(context);
        }
        catch (Exception ex) when (ex is ConfigurationException or DeviceException or CaptureException
            or IOException or UnauthorizedAccessException or JsonException or ArgumentException)
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
        Console.WriteLine("Run 'light-sync --help' to see the available commands.");
        return 0;
    }
}
