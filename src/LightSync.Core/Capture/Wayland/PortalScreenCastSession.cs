using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus.Protocol;

namespace LightSync.Core.Capture.Wayland;

/// <summary>
/// What the desktop portal handed back after the user chose a source.
/// </summary>
/// <param name="NodeId">
/// PipeWire node id. Only meaningful while the session is alive, and reused across sessions,
/// so it must never be cached as an identity.
/// </param>
/// <param name="X">
/// Stream origin as reported by the portal. Note that xdg-desktop-portal-hyprland reports
/// (0, 0) even for a region selection, because a region is exposed as a cropped virtual output
/// starting at its own origin — the region's desktop offset is not recoverable.
/// </param>
public sealed record PortalSelection(
    uint NodeId,
    int X,
    int Y,
    int Width,
    int Height,
    PortalSourceType SourceType,
    string? RestoreToken)
{
    public CaptureArea Area => new(X, Y, Width, Height);

    /// <summary>
    /// True when the stream is already exactly the region the user chose, so no cropping is
    /// needed. A region selection arrives as a virtual source sized to the region.
    /// </summary>
    public bool IsPreCropped => SourceType == PortalSourceType.Virtual;

    public override string ToString() =>
        $"node {NodeId}, {Width}x{Height}+{X}+{Y}, source {SourceType}";
}

/// <summary>
/// Portal source kinds. Declared as flags because the protocol uses these same values as a
/// bitmask in the SelectSources <c>types</c> option, though <c>source_type</c> reports one.
/// </summary>
[Flags]
public enum PortalSourceType
{
    None = 0,
    Monitor = 1,
    Window = 2,
    Virtual = 4,
}

/// <summary>
/// Drives <c>org.freedesktop.portal.ScreenCast</c> over D-Bus.
/// </summary>
/// <remarks>
/// The session must stay alive for as long as the stream is consumed: closing it destroys the
/// PipeWire node, and the node then reports "Device or resource busy" to any consumer. That is
/// why this type is disposable and owns its connection rather than opening one per call.
/// </remarks>
public sealed partial class PortalScreenCastSession : IAsyncDisposable, IDisposable
{
    private const int FcntlDuplicateFileDescriptor = 0;
    private const string PortalService = "org.freedesktop.portal.Desktop";
    private const string PortalObject = "/org/freedesktop/portal/desktop";
    private const string ScreenCastInterface = "org.freedesktop.portal.ScreenCast";
    private const string RequestInterface = "org.freedesktop.portal.Request";
    private const string SessionInterface = "org.freedesktop.portal.Session";

    /// <summary>
    /// The source picker appears during SelectSources on xdg-desktop-portal-hyprland, not
    /// during Start as the generic portal documentation implies, so that call has to allow for
    /// however long a person takes to choose.
    /// </summary>
    private static readonly TimeSpan UserInteractionTimeout = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(20);

    private DBusConnection? connection;
    private string? senderToken;
    private string? sessionHandle;

    /// <summary>
    /// The portal-authorized PipeWire connection. Some backends, including KDE, require this
    /// descriptor rather than allowing clients to attach to a selected stream on the global
    /// PipeWire socket.
    /// </summary>
    public SafeFileHandle? PipeWireRemote { get; private set; }

    private delegate void ArgumentWriter(ref MessageWriter writer, string handleToken);

    /// <summary>
    /// Runs the whole handshake. When <paramref name="restoreToken"/> is supplied the portal may
    /// skip the picker entirely.
    /// </summary>
    public async Task<PortalSelection> OpenAsync(string? restoreToken, CancellationToken cancellationToken)
    {
        // DBusConnection.Session is an autoconnect connection and refuses to expose UniqueName,
        // which is needed to predict the Request object paths, so connect explicitly.
        var address = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")
            ?? throw new CaptureException(
                "DBUS_SESSION_BUS_ADDRESS is not set, so the desktop portal cannot be reached.");

        var bus = new DBusConnection(address);
        connection = bus;

        try
        {
            await bus.ConnectAsync();
        }
        catch (Exception ex) when (ex is DBusExceptionBase or IOException)
        {
            throw new CaptureException("Could not connect to the D-Bus session bus.", ex);
        }

        // ":1.42" becomes "1_42" in Request paths.
        senderToken = (bus.UniqueName ?? throw new CaptureException("The bus did not assign a unique name."))
            .TrimStart(':')
            .Replace('.', '_');

        var createResults = await CallPortalAsync(
            "CreateSession",
            "a{sv}",
            (ref MessageWriter writer, string handleToken) =>
            {
                var dictionary = writer.WriteDictionaryStart();
                WriteString(ref writer, "handle_token", handleToken);
                WriteString(ref writer, "session_handle_token", NewToken());
                writer.WriteDictionaryEnd(dictionary);
            },
            CallTimeout,
            cancellationToken);

        sessionHandle = createResults.TryGetValue("session_handle", out var handle)
            ? handle.GetString()
            : throw new CaptureException("The portal did not return a session handle.");

        await CallPortalAsync(
            "SelectSources",
            "oa{sv}",
            (ref MessageWriter writer, string handleToken) =>
            {
                writer.WriteObjectPath(sessionHandle!);
                var dictionary = writer.WriteDictionaryStart();
                WriteString(ref writer, "handle_token", handleToken);
                // Region selections are virtual sources. Requesting only monitors and windows
                // hides the Region tab in portal implementations such as Hyprland.
                WriteUInt32(
                    ref writer,
                    "types",
                    (uint)(PortalSourceType.Monitor | PortalSourceType.Window | PortalSourceType.Virtual));
                WriteBool(ref writer, "multiple", false);
                WriteUInt32(ref writer, "cursor_mode", 1u);

                // Ask for a restore token. Whether one is actually issued is up to the portal
                // and, on Hyprland, to the user ticking the picker's checkbox.
                WriteUInt32(ref writer, "persist_mode", 2u);

                if (!string.IsNullOrWhiteSpace(restoreToken))
                {
                    WriteString(ref writer, "restore_token", restoreToken);
                }

                writer.WriteDictionaryEnd(dictionary);
            },
            UserInteractionTimeout,
            cancellationToken);

        var startResults = await CallPortalAsync(
            "Start",
            "osa{sv}",
            (ref MessageWriter writer, string handleToken) =>
            {
                writer.WriteObjectPath(sessionHandle!);
                writer.WriteString(string.Empty);
                var dictionary = writer.WriteDictionaryStart();
                WriteString(ref writer, "handle_token", handleToken);
                writer.WriteDictionaryEnd(dictionary);
            },
            UserInteractionTimeout,
            cancellationToken);

        var selection = ReadSelection(startResults);
        PipeWireRemote = await OpenPipeWireRemoteAsync(cancellationToken);
        return selection;
    }

    private async Task<SafeFileHandle> OpenPipeWireRemoteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var message = BuildCall(
                connection!,
                PortalObject,
                "OpenPipeWireRemote",
                "oa{sv}",
                (ref MessageWriter writer, string _) =>
                {
                    // The portal API currently defines no options for this call.
                    writer.WriteObjectPath(sessionHandle!);
                    var options = writer.WriteDictionaryStart();
                    writer.WriteDictionaryEnd(options);
                },
                NewToken());

            using var remote = await connection!.CallMethodAsync(
                message,
                static (Message reply, object? _) => reply.GetBodyReader().ReadHandle<SafeFileHandle>(),
                null);

            // D-Bus descriptors are close-on-exec. GStreamer is an external child process, so
            // duplicate the portal connection with that flag cleared before passing it to
            // pipewiresrc via its fd property.
            var descriptor = Fcntl(
                checked((int)remote.DangerousGetHandle()),
                FcntlDuplicateFileDescriptor,
                3);
            if (descriptor < 0)
            {
                throw new CaptureException("Could not prepare the portal PipeWire connection for GStreamer.");
            }

            return new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        }
        catch (DBusExceptionBase ex)
        {
            throw new CaptureException("The desktop portal could not open its PipeWire connection.", ex);
        }
    }

    private static PortalSelection ReadSelection(Dictionary<string, VariantValue> results)
    {
        if (!results.TryGetValue("streams", out var streams) || streams.Count == 0)
        {
            throw new CaptureException("The portal returned no streams; nothing was selected.");
        }

        var stream = streams.GetItem(0);
        var nodeId = stream.GetItem(0).GetUInt32();
        var properties = stream.GetItem(1).GetDictionary<string, VariantValue>();

        var (x, y) = ReadPair(properties, "position");
        var (width, height) = ReadPair(properties, "size");

        if (width <= 0 || height <= 0)
        {
            throw new CaptureException(
                $"The portal reported an unusable stream size of {width}x{height}.");
        }

        var sourceType = properties.TryGetValue("source_type", out var type)
            ? (PortalSourceType)type.GetUInt32()
            : PortalSourceType.None;

        var restoreToken = results.TryGetValue("restore_token", out var token)
            ? token.GetString()
            : null;

        return new PortalSelection(nodeId, x, y, width, height, sourceType, restoreToken);
    }

    private static (int First, int Second) ReadPair(
        Dictionary<string, VariantValue> properties,
        string key) =>
        properties.TryGetValue(key, out var value) && value.Count >= 2
            ? (value.GetItem(0).GetInt32(), value.GetItem(1).GetInt32())
            : (0, 0);

    /// <summary>
    /// Issues a portal method and waits for the matching Request.Response signal, which is how
    /// every portal method reports its actual result.
    /// </summary>
    private async Task<Dictionary<string, VariantValue>> CallPortalAsync(
        string member,
        string signature,
        ArgumentWriter writeArguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var bus = connection!;
        var handleToken = NewToken();
        var expectedPath = $"{PortalObject}/request/{senderToken}/{handleToken}";

        var completion = new TaskCompletionSource<(uint Code, Dictionary<string, VariantValue> Results)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Subscribed before the call is issued, because the Response can arrive immediately.
        //
        // The sender filter must be null. The Response comes from xdg-desktop-portal's unique
        // name (":1.23"), and the library compares the sender string literally, so filtering on
        // the well-known name silently never matches. The request path already embeds our own
        // unique name, so it is unambiguous on its own.
        using var subscription = await bus.WatchSignalAsync(
            sender: null,
            path: expectedPath,
            @interface: RequestInterface,
            signal: "Response",
            reader: static (Message message, object? _) =>
            {
                var reader = message.GetBodyReader();
                return (reader.ReadUInt32(), reader.ReadDictionaryOfStringToVariantValue());
            },
            handler: (Notification<(uint Code, Dictionary<string, VariantValue> Results)> notification) =>
            {
                // Notification.Exception throws unless IsCompletion is set, and an exception
                // escaping this handler tears down the whole connection — which then surfaces
                // as an unrelated timeout.
                if (notification.HasValue)
                {
                    completion.TrySetResult(notification.Value);
                }
                else if (notification.IsCompletion)
                {
                    completion.TrySetException(
                        notification.Exception ?? new CaptureException("The portal signal subscription ended."));
                }
            },
            flags: ObserverFlags.None,
            emitOnCapturedContext: false,
            state: null);

        string actualPath;
        try
        {
            var message = BuildCall(bus, PortalObject, member, signature, writeArguments, handleToken);
            actualPath = await bus.CallMethodAsync(
                message,
                static (Message m, object? _) => m.GetBodyReader().ReadObjectPathAsString(),
                null);
        }
        catch (DBusExceptionBase ex)
        {
            throw new CaptureException($"The portal rejected {member}: {ex.Message}", ex);
        }

        if (!string.Equals(actualPath, expectedPath, StringComparison.Ordinal))
        {
            // Harmless in itself, but it means the path prediction is wrong and a future
            // portal version may need different handling.
            using var pathSubscription = await bus.WatchSignalAsync(
                sender: null,
                path: actualPath,
                @interface: RequestInterface,
                signal: "Response",
                reader: static (Message message, object? _) =>
                {
                    var reader = message.GetBodyReader();
                    return (reader.ReadUInt32(), reader.ReadDictionaryOfStringToVariantValue());
                },
                handler: (Notification<(uint Code, Dictionary<string, VariantValue> Results)> notification) =>
                {
                    if (notification.HasValue)
                    {
                        completion.TrySetResult(notification.Value);
                    }
                },
                flags: ObserverFlags.None,
                emitOnCapturedContext: false,
                state: null);
        }

        var finished = await Task.WhenAny(completion.Task, Task.Delay(timeout, cancellationToken));
        if (finished != completion.Task)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new CaptureException(
                $"The desktop portal did not answer {member} within {timeout.TotalSeconds:0} seconds.");
        }

        var (code, results) = await completion.Task;

        return code switch
        {
            0 => results,
            1 => throw new PortalCancelledException("The screen-capture selection was cancelled."),
            _ => throw new CaptureException($"The desktop portal reported an error for {member} (code {code})."),
        };
    }

    /// <summary>
    /// Builds the message in a synchronous scope. <see cref="MessageWriter"/> is a ref struct
    /// and cannot cross an await.
    /// </summary>
    private static MessageBuffer BuildCall(
        DBusConnection bus,
        string path,
        string member,
        string? signature,
        ArgumentWriter? writeArguments,
        string handleToken)
    {
        var writer = bus.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(PortalService, path, ScreenCastInterface, member, signature);
            writeArguments?.Invoke(ref writer, handleToken);
            return writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
    }

    /// <summary>Must be a valid D-Bus path element: letters, digits and underscores only.</summary>
    private static string NewToken() => "ls" + Guid.NewGuid().ToString("N")[..12];

    [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int Fcntl(int fileDescriptor, int command, int argument);

    private static void WriteString(ref MessageWriter writer, string key, string value)
    {
        writer.WriteDictionaryEntryStart();
        writer.WriteString(key);
        writer.WriteVariantString(value);
    }

    private static void WriteUInt32(ref MessageWriter writer, string key, uint value)
    {
        writer.WriteDictionaryEntryStart();
        writer.WriteString(key);
        writer.WriteVariantUInt32(value);
    }

    private static void WriteBool(ref MessageWriter writer, string key, bool value)
    {
        writer.WriteDictionaryEntryStart();
        writer.WriteString(key);
        writer.WriteVariantBool(value);
    }

    public async ValueTask DisposeAsync()
    {
        if (connection is { } bus)
        {
            if (sessionHandle is { } handle)
            {
                try
                {
                    var writer = bus.GetMessageWriter();
                    MessageBuffer close;
                    try
                    {
                        writer.WriteMethodCallHeader(PortalService, handle, SessionInterface, "Close", null);
                        close = writer.CreateMessage();
                    }
                    finally
                    {
                        writer.Dispose();
                    }

                    await bus.CallMethodAsync(close);
                }
                catch (Exception ex) when (ex is DBusExceptionBase or IOException or ObjectDisposedException)
                {
                    // The portal may already have torn the session down; nothing to recover.
                }

                sessionHandle = null;
            }

            Dispose();
        }
    }

    /// <summary>
    /// Drops the connection without asking the portal to close the session first. Prefer
    /// <see cref="DisposeAsync"/>, which closes it politely.
    /// </summary>
    public void Dispose()
    {
        PipeWireRemote?.Dispose();
        PipeWireRemote = null;
        connection?.Dispose();
        connection = null;
        sessionHandle = null;
    }
}

/// <summary>The user dismissed the portal's picker.</summary>
public sealed class PortalCancelledException : Exception
{
    public PortalCancelledException()
    {
    }

    public PortalCancelledException(string message)
        : base(message)
    {
    }

    public PortalCancelledException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
