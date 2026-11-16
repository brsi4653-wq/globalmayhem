using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace OnRecord.Platform;

/// <summary>One entry of Windows' Apps folder (the Start menu's "All apps" list).</summary>
/// <param name="Name">The name shown in Start.</param>
/// <param name="Id">Windows' ID for it inside the Apps folder (an AppUserModelID or a known-folder path). Only for display and tests.</param>
public sealed record AppEntry(string Name, string Id)
{
    /// <summary>The item's absolute shell ID list, captured when the list was read. Launching uses this, never a string.</summary>
    internal byte[]? Pidl { get; init; }
}

/// <summary>
/// The installed apps, read from shell:AppsFolder (the same list as All apps in Start: desktop programs, Microsoft
/// Store apps, Steam and other Start shortcuts), and launching one exactly like clicking it in Start.
///
/// LAUNCH lines only ever start an entry of THIS list, by the shell ID list read from Windows. Nothing Claude writes
/// is ever used as a path, ID or command. All shell work runs on one background STA thread.
/// </summary>
public static class InstalledApps
{
    private static readonly BlockingCollection<Action> Work = new();
    private static Thread? _worker;
    private static readonly object Gate = new();
    private static Task<IReadOnlyList<AppEntry>>? _list;

    /// <summary>Reads the list again in the background (each Alt+Space open), so newly installed apps work right away.</summary>
    public static void Refresh()
    {
        lock (Gate)
        {
            if (_list is { IsCompleted: false }) return; // already reading
            _list = RunOnWorker(ReadAppsFolder);
        }
    }

    /// <summary>The current list (reads it first if it was never read). Empty if Windows refused.</summary>
    public static async Task<IReadOnlyList<AppEntry>> GetAsync()
    {
        Task<IReadOnlyList<AppEntry>>? list;
        lock (Gate) list = _list;
        if (list == null) { Refresh(); lock (Gate) list = _list!; }
        try { return await list; }
        catch (Exception ex) { Trace($"reading the Apps folder failed: {ex.Message}"); return []; }
    }

    /// <summary>Opens the app like a click in Start. Returns null on success, or what went wrong.</summary>
    public static Task<string?> LaunchAsync(AppEntry app)
    {
        if (app.Pidl == null) return Task.FromResult<string?>("it isn't in the installed apps list");
        if (Environment.GetEnvironmentVariable("ONRECORD_TEST_NO_EXTERNAL") == "1")
        {
            Trace($"would launch {app.Name}");
            return Task.FromResult<string?>(null);
        }
        return RunOnWorker(() => Launch(app.Pidl));
    }

    // ---------------------------------------------------------------- shell work (STA thread)

    private static Task<T> RunOnWorker<T>(Func<T> job)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        EnsureWorker();
        Work.Add(() =>
        {
            try { tcs.TrySetResult(job()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        return tcs.Task;
    }

    private static void EnsureWorker()
    {
        lock (Gate)
        {
            if (_worker != null) return;
            _worker = new Thread(() => { foreach (var job in Work.GetConsumingEnumerable()) job(); })
            { IsBackground = true, Name = "OnRecord apps", Priority = ThreadPriority.BelowNormal };
            _worker.SetApartmentState(ApartmentState.STA); // the shell expects STA
            _worker.Start();
        }
    }

    private static IReadOnlyList<AppEntry> ReadAppsFolder()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var apps = new List<AppEntry>();
        var iidItem = typeof(IShellItem).GUID;
        Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName("shell:AppsFolder", IntPtr.Zero, ref iidItem, out var folder));
        try
        {
            var bhid = BHID_EnumItems;
            var iidEnum = typeof(IEnumShellItems).GUID;
            Marshal.ThrowExceptionForHR(folder.BindToHandler(IntPtr.Zero, ref bhid, ref iidEnum, out var enumObj));
            var items = (IEnumShellItems)enumObj;
            try
            {
                while (items.Next(1, out var child, out uint fetched) == 0 && fetched == 1)
                {
                    try
                    {
                        string? name = DisplayName(child, SIGDN_NORMALDISPLAY);
                        string? id = DisplayName(child, SIGDN_PARENTRELATIVEPARSING);
                        byte[]? pidl = IdList(child);
                        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id) && pidl != null)
                            apps.Add(new AppEntry(name.Trim(), id) { Pidl = pidl });
                    }
                    finally { Marshal.ReleaseComObject(child); }
                }
            }
            finally { Marshal.ReleaseComObject(items); }
        }
        finally { Marshal.ReleaseComObject(folder); }
        Trace($"read {apps.Count} apps in {sw.ElapsedMilliseconds} ms");
        return apps;
    }

    private static string? DisplayName(IShellItem item, uint sigdn)
    {
        if (item.GetDisplayName(sigdn, out IntPtr p) != 0 || p == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    private static byte[]? IdList(IShellItem item)
    {
        if (SHGetIDListFromObject(item, out IntPtr pidl) != 0 || pidl == IntPtr.Zero) return null;
        try
        {
            var bytes = new byte[ILGetSize(pidl)];
            Marshal.Copy(pidl, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }

    private static string? Launch(byte[] pidlBytes)
    {
        IntPtr pidl = Marshal.AllocCoTaskMem(pidlBytes.Length);
        try
        {
            Marshal.Copy(pidlBytes, 0, pidl, pidlBytes.Length);
            var info = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                fMask = SEE_MASK_INVOKEIDLIST | SEE_MASK_FLAG_NO_UI,
                nShow = SW_SHOWNORMAL,
                lpIDList = pidl,
            };
            if (ShellExecuteEx(ref info)) return null;
            int err = Marshal.GetLastWin32Error();
            return err == 1223 ? "it was cancelled" : new System.ComponentModel.Win32Exception(err).Message;
        }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }

    // Diagnostics for tests only (ONRECORD_TRACE_LAUNCH=1): list size, matches and launches (app names only).
    internal static void Trace(string line)
    {
        if (Environment.GetEnvironmentVariable("ONRECORD_TRACE_LAUNCH") != "1") return;
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppPaths.Logs, "launch-trace.log"), $"{DateTime.Now:HH:mm:ss.fff} {line}\n"); } catch { }
    }

    // ---------------------------------------------------------------- interop

    private const uint SIGDN_NORMALDISPLAY = 0x00000000;
    private const uint SIGDN_PARENTRELATIVEPARSING = 0x80018001;
    private const int SEE_MASK_INVOKEIDLIST = 0x0000000C;
    private const int SEE_MASK_FLAG_NO_UI = 0x00000400;
    private const int SW_SHOWNORMAL = 1;
    private static readonly Guid BHID_EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig] int GetParent(out IShellItem ppsi);
        [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr ppszName);
        [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumShellItems
    {
        [PreserveSig] int Next(uint celt, out IShellItem rgelt, out uint pceltFetched);
        [PreserveSig] int Skip(uint celt);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumShellItems ppenum);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        public string? lpVerb, lpFile, lpParameters, lpDirectory;
        public int nShow;
        public IntPtr hInstApp, lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public int dwHotKey;
        public IntPtr hIconOrMonitor, hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetIDListFromObject([MarshalAs(UnmanagedType.IUnknown)] object punk, out IntPtr ppidl);

    [DllImport("shell32.dll")]
    private static extern uint ILGetSize(IntPtr pidl);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);
}
