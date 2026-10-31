using System.Globalization;
using System.Runtime.InteropServices;

namespace Win4DaveMediaPlayer;

public sealed class MpvEngine : IDisposable
{
    private nint handle;
    private string? lastDrop;

    public event Action? FileLoaded;
    public event Action<string>? PlaybackError;
    public event Action<string>? FileDropped;

    public MpvEngine(nint window = 0, bool headless = false)
    {
        handle = Native.mpv_create();
        if (handle == 0)
            throw new InvalidOperationException("Unable to create the video playback engine.");
        try
        {
            Option("config", "no");
            Option("load-scripts", "no");
            Option("osc", "no");
            Option("input-default-bindings", "no");
            Option("input-vo-keyboard", "no");
            Option("input-cursor", "no");
            Option("input-builtin-drag-and-drop", "no");
            Option("osd-level", "0");
            Option("idle", "yes");
            Option("keep-open", "always");
            Option("pause", "yes");
            Option("hr-seek", "yes");
            Option("audio-pitch-correction", "yes");
            if (headless)
            {
                Option("vo", "null");
                Option("ao", "null");
            }
            else
            {
                Option("wid", window.ToString(CultureInfo.InvariantCulture));
                Option("vo", "gpu");
                Option("gpu-api", "d3d11");
                Option("hwdec", "auto-safe");
            }
            Check(Native.mpv_initialize(handle), "Initialize playback");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void Option(string name, string value) =>
        Check(Native.mpv_set_option_string(handle, name, value), $"Set {name}");

    public void Set(string name, string value) =>
        Check(Native.mpv_set_property_string(handle, name, value), $"Set {name}");

    public void Set(string name, double value) =>
        Set(name, value.ToString("R", CultureInfo.InvariantCulture));

    public string? Get(string name)
    {
        var value = Native.mpv_get_property_string(handle, name);
        if (value == 0)
            return null;
        try
        {
            return Marshal.PtrToStringUTF8(value);
        }
        finally
        {
            Native.mpv_free(value);
        }
    }

    public double? Number(string name) =>
        double.TryParse(Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value) ? value : null;

    public void Command(params string[] arguments)
    {
        var strings = arguments.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
        var array = Marshal.AllocHGlobal((strings.Length + 1) * nint.Size);
        try
        {
            for (var index = 0; index < strings.Length; index++)
                Marshal.WriteIntPtr(array, index * nint.Size, strings[index]);
            Marshal.WriteIntPtr(array, strings.Length * nint.Size, 0);
            Check(Native.mpv_command(handle, array), arguments[0]);
        }
        finally
        {
            Marshal.FreeHGlobal(array);
            foreach (var text in strings)
                Marshal.FreeCoTaskMem(text);
        }
    }

    public void PollEvents()
    {
        while (handle != 0)
        {
            var nativeEvent = Marshal.PtrToStructure<MpvEvent>(Native.mpv_wait_event(handle, 0));
            if (nativeEvent.Id == 0)
                break;
            if (nativeEvent.Id == 8)
                FileLoaded?.Invoke();
            if (nativeEvent.Id == 7 && nativeEvent.Data != 0)
            {
                var end = Marshal.PtrToStructure<EndFile>(nativeEvent.Data);
                if (end.Reason == 4)
                    PlaybackError?.Invoke(ErrorText(end.Error));
            }
        }
        var drop = Get("dropped-files/time");
        if (drop is not null && drop != lastDrop)
        {
            lastDrop = drop;
            if (Get("dropped-files/files/0") is { } path && Get("dropped-files/files/1") is null)
                FileDropped?.Invoke(path);
        }
    }

    public void Dispose()
    {
        if (handle == 0)
            return;
        Native.mpv_terminate_destroy(handle);
        handle = 0;
    }

    private static string ErrorText(int code) =>
        Marshal.PtrToStringUTF8(Native.mpv_error_string(code)) ?? $"Playback error {code}";

    private static void Check(int code, string operation)
    {
        if (code < 0)
            throw new InvalidOperationException($"{operation}: {ErrorText(code)}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MpvEvent
    {
        public int Id;
        public int Error;
        public ulong Reply;
        public nint Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EndFile
    {
        public int Reason;
        public int Error;
    }

    private static class Native
    {
        private const string Library = "libmpv-2.dll";

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint mpv_create();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int mpv_initialize(nint context);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void mpv_terminate_destroy(nint context);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int mpv_set_option_string(nint context,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int mpv_set_property_string(nint context,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint mpv_get_property_string(nint context,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int mpv_command(nint context, nint arguments);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint mpv_wait_event(nint context, double timeout);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint mpv_error_string(int error);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void mpv_free(nint data);
    }
}
