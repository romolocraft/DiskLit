using System.Runtime.InteropServices;

namespace DiskLit;

internal static class RecycleBin
{
    internal sealed record Result(bool Started, bool Success, int Removed, long Bytes);

    internal static Result Move(IReadOnlyList<CleanupItem> items, Func<bool> validate, CancellationToken token)
    {
        IFileOperation? operation = null;
        var shellItems = new List<object>();
        var sink = new RecycleProgressSink(items, token);
        var started = false;
        try
        {
            operation = (IFileOperation)new FileOperationCom();
            operation.SetOperationFlags(0x00080000 | 0x00100000 | 0x20000000 | 0x0400 | 0x0004 | 0x2000);
            operation.Advise(sink, out _);
            var iid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
            foreach (var item in items)
            {
                token.ThrowIfCancellationRequested();
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(item.FullPath, IntPtr.Zero, ref iid, out var shellItem));
                shellItems.Add(shellItem);
                operation.DeleteItem(shellItem, IntPtr.Zero);
            }
            if (!validate() || token.IsCancellationRequested) return new(false, false, 0, 0);
            started = true;
            operation.PerformOperations();
            operation.GetAnyOperationsAborted(out var aborted);
            return new(true, !aborted && sink.Removed == items.Count, sink.Removed, sink.Bytes);
        }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return new(started, false, sink.Removed, sink.Bytes);
        }
        finally
        {
            foreach (var item in shellItems) Marshal.FinalReleaseComObject(item);
            if (operation is not null) Marshal.FinalReleaseComObject(operation);
            GC.KeepAlive(sink);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int SHCreateItemFromParsingName(string path, IntPtr bind, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object item);

    [ComImport, Guid("3AD05575-8857-4850-9277-11B85BDB8E09")]
    class FileOperationCom { }

    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileOperation
    {
        void Advise(IRecycleProgressSink sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(IntPtr dialog);
        void SetProperties(IntPtr properties);
        void SetOwnerWindow(IntPtr owner);
        void ApplyPropertiesToItem(IntPtr item);
        void ApplyPropertiesToItems(IntPtr items);
        void RenameItem(IntPtr item, IntPtr name, IntPtr sink);
        void RenameItems(IntPtr items, IntPtr name);
        void MoveItem(IntPtr item, IntPtr destination, IntPtr name, IntPtr sink);
        void MoveItems(IntPtr items, IntPtr destination);
        void CopyItem(IntPtr item, IntPtr destination, IntPtr name, IntPtr sink);
        void CopyItems(IntPtr items, IntPtr destination);
        void DeleteItem([MarshalAs(UnmanagedType.Interface)] object item, IntPtr sink);
        void DeleteItems(IntPtr items);
        void NewItem(IntPtr destination, uint attributes, IntPtr name, IntPtr templateName, IntPtr sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
}

[ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRecycleProgressSink
{
    [PreserveSig] int StartOperations();
    [PreserveSig] int FinishOperations(int result);
    [PreserveSig] int PreRenameItem(uint flags, IntPtr item, IntPtr name);
    [PreserveSig] int PostRenameItem(uint flags, IntPtr item, IntPtr name, int result, IntPtr created);
    [PreserveSig] int PreMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr name);
    [PreserveSig] int PostMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr name, int result, IntPtr created);
    [PreserveSig] int PreCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr name);
    [PreserveSig] int PostCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr name, int result, IntPtr created);
    [PreserveSig] int PreDeleteItem(uint flags, IntPtr item);
    [PreserveSig] int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr created);
    [PreserveSig] int PreNewItem(uint flags, IntPtr destination, IntPtr name);
    [PreserveSig] int PostNewItem(uint flags, IntPtr destination, IntPtr name, IntPtr templateName, uint attributes, int result, IntPtr created);
    [PreserveSig] int UpdateProgress(uint total, uint done);
    [PreserveSig] int ResetTimer();
    [PreserveSig] int PauseTimer();
    [PreserveSig] int ResumeTimer();
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class RecycleProgressSink : IRecycleProgressSink
{
    const int Abort = unchecked((int)0x80004004);
    readonly Dictionary<string, CleanupItem> items;
    readonly HashSet<string> completed = new(StringComparer.OrdinalIgnoreCase);
    readonly CancellationToken token;
    internal int Removed => completed.Count;
    internal long Bytes { get; private set; }

    internal RecycleProgressSink(IReadOnlyList<CleanupItem> items, CancellationToken token)
    {
        this.items = items.ToDictionary(item => Path.GetFullPath(item.FullPath), StringComparer.OrdinalIgnoreCase);
        this.token = token;
    }

    public int PreDeleteItem(uint flags, IntPtr item)
    {
        if (token.IsCancellationRequested || (flags & 0x80) == 0) return Abort;
        try { return CleanupInspection.SafePath(GetPath(item)) ? 0 : Abort; }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or ArgumentException) { return Abort; }
    }

    public int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr created)
    {
        if (result < 0) return result;
        if (created == IntPtr.Zero) return Abort;
        try
        {
            if (items.TryGetValue(GetPath(item), out var target) && completed.Add(target.FullPath)) Bytes += target.Size;
            return 0;
        }
        catch (COMException) { return Abort; }
    }

    static string GetPath(IntPtr item)
    {
        var shellItem = (IShellItem)Marshal.GetObjectForIUnknown(item);
        try
        {
            shellItem.GetDisplayName(0x80058000, out var name);
            try { return Marshal.PtrToStringUni(name) ?? ""; }
            finally { Marshal.FreeCoTaskMem(name); }
        }
        finally { Marshal.ReleaseComObject(shellItem); }
    }

    public int StartOperations() => token.IsCancellationRequested ? Abort : 0;
    public int FinishOperations(int result) => 0;
    public int PreRenameItem(uint flags, IntPtr item, IntPtr name) => Abort;
    public int PostRenameItem(uint flags, IntPtr item, IntPtr name, int result, IntPtr created) => 0;
    public int PreMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr name) => Abort;
    public int PostMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr name, int result, IntPtr created) => 0;
    public int PreCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr name) => Abort;
    public int PostCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr name, int result, IntPtr created) => 0;
    public int PreNewItem(uint flags, IntPtr destination, IntPtr name) => Abort;
    public int PostNewItem(uint flags, IntPtr destination, IntPtr name, IntPtr templateName, uint attributes, int result, IntPtr created) => 0;
    public int UpdateProgress(uint total, uint done) => token.IsCancellationRequested ? Abort : 0;
    public int ResetTimer() => 0;
    public int PauseTimer() => 0;
    public int ResumeTimer() => 0;

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr bind, ref Guid handler, ref Guid iid, out IntPtr result);
        void GetParent(out IntPtr parent);
        void GetDisplayName(uint kind, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IntPtr other, uint hint, out int order);
    }
}
