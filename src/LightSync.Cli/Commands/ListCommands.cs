using LightSync.Core.Capture.Wayland;

namespace LightSync.Cli.Commands;

internal static class ListCommands
{
    public static async Task<int> ListDisplaysAsync(CommandContext context, CancellationToken cancellationToken)
    {
        try
        {
            var displays = await context.Displays.GetDisplaysAsync(cancellationToken);
            ConsoleUI.WriteDisplays(displays);
            return 0;
        }
        catch (DisplayEnumerationException ex)
        {
            ConsoleUI.Error(ex.Message);
            return 1;
        }
    }

    public static int ListAdapters(CommandContext context)
    {
        Console.WriteLine("Device adapters:");

        foreach (var adapter in context.Adapters.AvailableAdapters)
        {
            var status = adapter.IsImplemented ? "available" : "planned";
            Console.WriteLine($"  {adapter.Id,-10} {status,-10} {adapter.DisplayName}");
            Console.WriteLine($"  {string.Empty,-21} {adapter.Description}");
        }

        return 0;
    }
}
