# Cleanup and catalog validation

## Guarantees and boundaries

Analysis is read-only. The application manifest remains `asInvoker`. Registry access is
read-only and used solely to correlate installed applications; no registry cleaning exists.

Every cleanup candidate has size, file count, newest modification time and a SHA-256 digest
of metadata (relative paths, types/attributes, individual timestamps, file identities and sizes).
This catches renames and changes to files other than the newest one. The digest is not a hash
of file contents: deliberately preserving metadata or changes after the last check can evade it.

Inspection makes one traversal per candidate, including empty directories. Missing entries,
denied access, offline data, junctions, symbolic links and other reparse points invalidate the
candidate instead of yielding a partial fingerprint. Ancestor paths are checked too. Native
handles use OPEN_REPARSE_POINT and BACKUP_SEMANTICS, without enabling privileges.

Before dispatch, every selected file and directory must open with read/delete access and no
sharing. Invalid roots, invalid patterns, overlaps, duplicate selections, missing fingerprints,
changed metadata and unavailable deep inventory cancel the entire batch. No Shell operation
is performed on this path. After queuing Shell items, the full batch is validated again.

`IFileOperation` replaces `SHFileOperation`. RECYCLEONDELETE requests recycling, EARLYFAILURE
and NOERRORUI stop subsequent operations on error, and connected HTML companion deletion is
disabled. The progress sink vetoes deletion callbacks without the recycle flag. No permanent
delete API, fallback or elevation flag is present. Completion counts come from successful
recycle callbacks, not from files merely disappearing. Unsupported locations fail closed.

This is a conservative batch preflight, **not an atomic filesystem transaction**. Exclusive
handles must close before the Shell can open/rename the items. Another process can change or
open them in that interval, replace an ancestor, or create a reparse point after inspection.
Applications can also hold memory mappings or use data without an observable open handle.
The Shell offers no all-or-nothing rollback. An error after dispatch may leave some items in
the Recycle Bin; the UI reports this separately and advises inspection before retrying.
Closing the app cancels work and waits for an active recycle operation to return.

## Catalog policy

`KnownDataCatalog.cs` contains typed, ordered rules with full root boundaries, data category,
likely creator, recreatability, risk, explanation and recommendation. More specific roots win;
personal-data markers override cache rules. A match is a location-based inference, not proof
of authorship. Descriptions are available in Portuguese, English, Spanish and Russian.

Caches are allowlisted at specific paths. Product/profile roots are preserved. Windows Update,
DriverStore and Store package data are explained but not directly cleaned. Spotify Storage is
preserved because it can hold offline content. Riot data is preserved where no sufficiently
narrow cache path is known. Deep candidates are never selected by default; unknown old folders
require manual review. The installed-program inventory cannot prove absence: portable programs,
custom libraries and inaccessible process paths may not be identified. Personal markers are
conservative heuristics, not a complete detector of personal data.

Browser profiles and default launcher paths are discovered when the catalog is initialized.
Restart after adding profiles. Custom installation locations without a known cache rule remain
unclassified/protected rather than being guessed as disposable.

## Dark theme rendering

List and tree controls are themed with `SetWindowTheme(handle, "DarkMode_Explorer")`, which is what
makes their scrollbars dark. Measured on Windows 11 with a probe reading the control's own device
context: under `Explorer` the scrollbar track is RGB(240, 240, 240) with a RGB(133, 133, 133) thumb,
and under `DarkMode_Explorer` it is RGB(23, 23, 23) with a RGB(149, 149, 149) thumb. Nothing is
custom-painted over the non-client area, so scrollbar geometry, hit testing, drag behavior, keyboard
input, accessibility and ListView virtualization are entirely native and identical in both themes.
No undocumented uxtheme ordinals and no process-wide patches are used.

Switching theme recolors existing controls rather than recreating them: the file-type legend is
repainted in place and rebuilt only when the scan data or the interface language changes, and the
theme button label is updated on its own instead of reapplying every localized string. Brushes,
pens and fonts used while painting rows, cells and headers come from a keyed cache, so drawing a
list does not allocate GDI objects per row. Path classification is memoized, because it runs once
per visible row on every repaint. Measured with a reflection harness driving the real window and a
10,000-row list, repaint fell from 72.1 ms to 33.5 ms per scroll, and GDI, USER and managed memory
stayed flat across 200 consecutive theme switches.

Switching theme still costs roughly 0.4 s once a scan has filled the folder tree, and the cost is
in Windows rather than in this code: re-theming a TreeView is linear in its node count, measured at
0.06 ms per node, so the 2,000-node filter tree alone accounts for about 170 ms and its background
and foreground assignments for another 40 ms. With an empty tree the same switch takes 23 ms.
Suppressing the work with `BeginUpdate` or `WM_SETREDRAW` was measured and made it worse.

`WS_EX_COMPOSITED` was measured and rejected. It makes a theme switch look atomic, but it also
made list scrolling 164 ms instead of 75 ms per repaint in two independent A/B runs, without
making the switch itself faster.

## Repeatable validation without cleanup

```powershell
dotnet build -c Release -warnaserror
dotnet run --project tests/DiskLit.Tests.csproj -c Release
dotnet run --project tests/DiskLit.Tests.csproj -c Release -- --visual
git diff --check
```

The test runner creates and retains uniquely named synthetic fixtures under the temporary
directory. It checks preflight and progress-sink rejection without calling Execute, Move,
PerformOperations or any delete API. The visual harness uses production controls, 10,000
synthetic file rows, 100 folders, unchecked deep candidates and read-only process monitoring.
It does not save user settings. Never click the cleanup action as part of validation.

Native interop references:

- [IFileOperation flags](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags)
- [PreDeleteItem cancellation](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperationprogresssink-predeleteitem)
- [Transfer source flags](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_transfer_source_flags)
