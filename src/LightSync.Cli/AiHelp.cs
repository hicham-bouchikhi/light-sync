using System.CommandLine;
using System.Text;
using LightSync.Core.Configuration;

namespace LightSync.Cli;

/// <summary>
/// Emits an operator's guide aimed at an AI agent or an unfamiliar operator: what to run, in
/// what order, and what will bite.
/// </summary>
/// <remarks>
/// Deliberately prose rather than JSON. A nested object graph is harder for a language model to
/// act on than terse task-oriented text, and the useful content here is ordering, prerequisites
/// and caveats — none of which a schema expresses well. The command list is generated from the
/// live command tree so it cannot drift from reality.
/// </remarks>
internal static class AiHelp
{
    public static int Write(RootCommand root)
    {
        var text = new StringBuilder();

        text.AppendLine("# light-sync");
        text.AppendLine();
        text.AppendLine("Audio-reactive lighting for Linux. Default run follows computer playback; the");
        text.AppendLine("optional screen source reduces a region to colour zones, and --source ambient");
        text.AppendLine("runs a self-running static, pulse or rainbow effect.");
        text.AppendLine();

        AppendSetupOrder(text);
        AppendCommands(text, root);
        AppendConfiguration(text);
        AppendConstraints(text);
        AppendTroubleshooting(text);

        Console.Write(text.ToString());
        return 0;
    }

    private static void AppendSetupOrder(StringBuilder text)
    {
        text.AppendLine("## Do these in order");
        text.AppendLine();
        text.AppendLine("1. `light-sync diagnostics` — check the environment first. It names exactly");
        text.AppendLine("   what is missing, so start here rather than guessing.");
        text.AppendLine("2. `light-sync discover --save` — find and save profiles; `devices list` prints IDs.");
        text.AppendLine("3. `light-sync pair --device ID --save` — get a profile token. REQUIRES A HUMAN: someone");
        text.AppendLine("   must physically arm pairing on the device. For Nanoleaf Matter Essentials");
        text.AppendLine("   models that means the Nanoleaf app's \"Connect to API\" button, which opens");
        text.AppendLine("   a 30-second window. Holding a button does nothing on that line. You cannot");
        text.AppendLine("   complete this step alone; ask the user.");
        text.AppendLine("4. `light-sync test-device` — confirm the device answers and report how many");
        text.AppendLine("   LEDs it has. Sync maps each device’s full LED count automatically.");
        text.AppendLine("5. For screen sync only: `light-sync setup` — choose a capture area. The");
        text.AppendLine("   compositor shows a source picker that must be answered by hand. Tell the");
        text.AppendLine("   user to pick the \"Region\" tab, drag a rectangle, and tick \"Allow a");
        text.AppendLine("   restore token\" so the dialog does not reappear every run.");
        text.AppendLine("6. `light-sync run` — audio sync; `run --source screen` — screen sync;");
        text.AppendLine("   `run --source ambient` — a self-running static, pulse or rainbow effect. `stop` ends any.");
        text.AppendLine();
        text.AppendLine("Audio needs parec and a playback monitor; step 5 is unnecessary for audio.");
        text.AppendLine();
    }

    private static void AppendCommands(StringBuilder text, RootCommand root)
    {
        text.AppendLine("## Commands");
        text.AppendLine();

        foreach (var command in root.Subcommands)
        {
            AppendCommand(text, command, "light-sync");
        }

        text.AppendLine("Global:");
        foreach (var option in root.Options)
        {
            text.AppendLine($"    {option.Name}  {option.Description}");
        }

        text.AppendLine();
        text.AppendLine("`--dry-run` captures and processes but contacts no device. Use it to check");
        text.AppendLine("capture and colour output without touching the lights.");
        text.AppendLine();
    }

    private static void AppendCommand(StringBuilder text, Command command, string prefix)
    {
        var invocation = prefix + " " + command.Name;
        var arguments = string.Join(" ", command.Arguments.Select(a => $"<{a.Name}>"));
        text.AppendLine($"`{invocation}{(arguments.Length == 0 ? string.Empty : " " + arguments)}`");
        text.AppendLine($"    {command.Description}");
        foreach (var option in command.Options)
        {
            text.AppendLine($"    {option.Name}  {option.Description}");
        }
        text.AppendLine();
        foreach (var child in command.Subcommands)
        {
            AppendCommand(text, child, invocation);
        }
    }

    private static void AppendConfiguration(StringBuilder text)
    {
        text.AppendLine("## Configuration");
        text.AppendLine();
        text.AppendLine($"Lives at {ConfigurationPaths.ConfigFile}. Use `config set KEY VALUE`; every command");
        text.AppendLine("accepts `--config <path>` to use a different file.");
        text.AppendLine();
        text.AppendLine("- `audio`       source, mode (rainbow|spectrum|volume|bass), gain, brightness, motion, smoothing, noiseGate, color");
        text.AppendLine("- `lighting`    mode (static|pulse|rainbow), color, brightness, speed, direction");
        text.AppendLine("- `screen`      brightness (default 0.75), audioIntensityEnabled, audioSource");
        text.AppendLine("- `capture`     displayId, x, y, width, height, fps, restoreToken");
        text.AppendLine("- `mapping`     zoneCount, layout (vertical|horizontal),");
        text.AppendLine("                direction (left-to-right|right-to-left|top-to-bottom|bottom-to-top),");
        text.AppendLine("                reverse, customOrder");
        text.AppendLine("- `processing`  brightness, gamma, saturation, smoothing, blackLevel,");
        text.AppendLine("                averaging (mean|luminance-weighted|colour-weighted)");
        text.AppendLine("- `device`      adapter (nanoleaf|fake|wled|hue|openrgb) and a settings map");
        text.AppendLine();
        text.AppendLine("Tokens are NEVER stored in this file. They come from the environment variable");
        text.AppendLine($"named by `device.settings.tokenEnvironmentVariable` (default NANOLEAF_TOKEN),");
        text.AppendLine($"or the profile’s `secretsFilePath`; legacy fallback: {ConfigurationPaths.SecretsFile}.");
        text.AppendLine("Do not print a token, log one, or paste one into a commit or an issue.");
        text.AppendLine();
    }

    private static void AppendConstraints(StringBuilder text)
    {
        text.AppendLine("## Constraints that surprise people");
        text.AppendLine();
        text.AppendLine("- A region selection is delivered as its own cropped stream, and Hyprland");
        text.AppendLine("  reports its position as (0,0). The region's absolute desktop coordinates are");
        text.AppendLine("  NOT recoverable, so saved x and y are 0. This is normal, not a bug.");
        text.AppendLine("- A restore token is only issued if the user ticks the picker's checkbox.");
        text.AppendLine("  Without one, the picker appears on every run. `diagnostics` reports whether");
        text.AppendLine("  a token is stored.");
        text.AppendLine("- Both frontends use devices.json. `run` uses every enabled profile; repeat");
        text.AppendLine("  `--device ID` to choose outputs. Inspection/tests/pairing require one profile.");
        text.AppendLine("- Device outputs use their full LED counts. mapping.zoneCount sets preview size.");
        text.AppendLine("- Audio tuning and audio profile membership reload live. Screen brightness and");
        text.AppendLine("  intensity reload live; restart for screen source/layout/colour changes.");
        text.AppendLine("- Ambient lighting tuning applies live; changing lighting profiles restarts the");
        text.AppendLine("  effect for the new selection.");
        text.AppendLine("- Screen uses screen.brightness, with optional playback boost; inner processing");
        text.AppendLine("  brightness is 1. Master brightness is restored before final blackout.");
        text.AppendLine("- Local OpenRGB discovery/connections start its SDK server when needed.");
        text.AppendLine("- `run --source screen --demo --preview` tests screen output without hardware.");
        text.AppendLine("- `run --source ambient --preview` tests lighting modes without hardware; no");
        text.AppendLine("  capture, playback monitor or saved device is needed.");
        text.AppendLine("- Lighting master brightness is held at 100% during the effect and restored");
        text.AppendLine("  from the profile before blackout. `lighting.color black` is an off switch.");
        text.AppendLine("- Exact RGB tests use names, #RRGGBB or R,G,B; --zone is 1-based. Stop sync first.");
        text.AppendLine("- Default frame rate is 30. That is ample for ambient light; higher rates cost");
        text.AppendLine("  more and gain little.");
        text.AppendLine("- Wayland capture is damage-driven, so a static screen produces fewer frames and");
        text.AppendLine("  the reported rate drops. That is correct, not a fault.");
        text.AppendLine("- Every config field is optional; `{}` is valid and gets defaults.");
        text.AppendLine("- Requires Wayland, PipeWire, a desktop portal with ScreenCast, and GStreamer");
        text.AppendLine("  with the pipewiresrc plugin. Display detection currently uses hyprctl.");
        text.AppendLine();
    }

    private static void AppendTroubleshooting(StringBuilder text)
    {
        text.AppendLine("## When something fails");
        text.AppendLine();
        text.AppendLine("Run `light-sync diagnostics` first; it checks the environment, the config, and");
        text.AppendLine("the device in one pass and names the failing item.");
        text.AppendLine();
        text.AppendLine("- \"No Nanoleaf token found\"        → step 3 above, needs a human.");
        text.AppendLine("- \"rejected the auth token\"        → the token is stale; pair again.");
        text.AppendLine("- \"No capture area configured\"     → step 5 above, needs a human.");
        text.AppendLine("- \"selection was cancelled\"        → the user dismissed the picker; ask again.");
        text.AppendLine("- \"did not answer ... within\"      → the picker was never answered.");
        text.AppendLine("- device unreachable              → check the host is right and on the network.");
        text.AppendLine("- \"cannot render N zones\"          → set mapping.zoneCount to the reported count.");
        text.AppendLine();
        text.AppendLine("If the user says the light looks dim or washed out, that is a tuning matter, not");
        text.AppendLine("a fault: a zone covering a lot of screen averages down. Keep");
        text.AppendLine("processing.averaging at luminance-weighted, raise screen.brightness toward 1, and lower");
        text.AppendLine("gamma below 1 to lift mid-tones. Raise smoothing if it looks twitchy.");
        text.AppendLine();
        text.AppendLine("To verify the light works without involving screen capture, use");
        text.AppendLine("`light-sync test-color red` or `light-sync test-stream`. To verify capture and");
        text.AppendLine("colour processing without involving the light, use `light-sync --dry-run`.");
        text.AppendLine("Narrowing to one half like that resolves most problems quickly.");
        text.AppendLine();
        text.AppendLine("See docs/CLI.md, docs/ARCHITECTURE.md and docs/NANOLEAF_PROTOCOL.md for controls and");
        text.AppendLine("hardware-verified protocol notes.");
    }
}
