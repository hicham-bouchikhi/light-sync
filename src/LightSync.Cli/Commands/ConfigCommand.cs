using System.Text.Json;
using System.Text.Json.Nodes;
using LightSync.Core.Colors;
using LightSync.Core.Configuration;

namespace LightSync.Cli.Commands;

internal static class ConfigCommand
{
    internal static AppConfig Apply(AppConfig config, IEnumerable<string> assignments)
    {
        var json = JsonNode.Parse(ConfigurationLoader.Serialize(config))!.AsObject();
        foreach (var assignment in assignments)
        {
            var separator = assignment.IndexOf('=', StringComparison.Ordinal);
            if (separator < 1)
            {
                throw new ConfigurationException("Settings must use key=value, e.g. audio.gain=15.");
            }
            var path = assignment[..separator].Split('.');
            JsonObject current = json;
            for (var i = 0; i < path.Length - 1; i++)
            {
                if (current[path[i]] is not JsonObject child)
                {
                    throw new ConfigurationException($"Unknown configuration key '{assignment[..separator]}'.");
                }
                current = child;
            }
            var key = path[^1];
            if (!current.ContainsKey(key) && assignment[..separator] is not ("mapping.customOrder" or "capture.restoreToken"))
            {
                throw new ConfigurationException($"Unknown configuration key '{assignment[..separator]}'.");
            }
            var value = assignment[(separator + 1)..];
            if (assignment[..separator] is "audio.color" or "lighting.color")
            {
                var color = ParseColor(value);
                current[key] = new JsonObject { ["r"] = color.R, ["g"] = color.G, ["b"] = color.B };
            }
            else
            {
                try
                {
                    current[key] = JsonNode.Parse(value);
                }
                catch (JsonException)
                {
                    current[key] = JsonValue.Create(value);
                }
            }
        }
        var updated = ConfigurationLoader.Parse(json.ToJsonString(), "CLI settings");
        var problems = updated.Validate();
        if (problems.Count > 0)
        {
            throw new ConfigurationException(string.Join(Environment.NewLine, problems));
        }
        return updated;
    }

    internal static RgbColor ParseColor(string value)
    {
        if (ColorConstants.TryParse(value, out var color))
        {
            return color;
        }

        throw new ConfigurationException("Colour must be red/green/blue/white/black, #RRGGBB, or R,G,B (0–255).");
    }

    internal static async Task<int> ShowAsync(CommandContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine(ConfigurationLoader.Serialize(await context.LoadAsync(cancellationToken)));
        return 0;
    }

    internal static async Task<int> SetAsync(CommandContext context, string key, string value,
        CancellationToken cancellationToken)
    {
        var config = await ConfigurationLoader.LoadAsync(context.ConfigPath, cancellationToken);
        await context.SaveAsync(Apply(config, [key + "=" + value]), cancellationToken);
        ConsoleUI.Success($"Saved {key} to {context.ConfigPath}.");
        return 0;
    }
}
