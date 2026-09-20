# DiskLit

A lightweight disk space analyzer for Windows. It uses a single-pass Win32 enumeration, never allocates a `FileInfo` per file, and keeps only the 10,000 largest results in memory.

Strictly read-only: it does not delete, move or modify anything.

## Install

Download **[DiskLit-win-x64.exe](https://github.com/romolocraft/DiskLit/releases/latest/download/DiskLit-win-x64.exe)** from the latest release and run it. There is no installer and no runtime to set up. Requires 64-bit Windows 10 or 11.

The first time you run it, Windows SmartScreen shows *"Windows protected your PC"* and hides the run button. This happens because the executable is not signed with a paid code-signing certificate, not because anything is wrong with it. Click **More info**, then **Run anyway**. If you would rather not take that on trust, build it yourself from source below.

## Features

- Finds the largest files on any drive and shows where they live.
- Folder breakdown with rolled-up totals, and a filter to narrow the list to one folder.
- Follows the Windows light/dark theme, with a manual override.
- English, Portuguese, Spanish and Russian, picked from the system language.

## Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
git clone https://github.com/romolocraft/DiskLit.git
cd DiskLit
dotnet run
```

To produce the same single-file binary the release ships:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o publish
```

`IncludeNativeLibrariesForSelfExtract` is not optional. Without it the publish leaves five native DLLs beside the executable, and the `.exe` on its own refuses to start.

For a 228 KB build that reuses an installed [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) instead of bundling it:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o publish
```

## Releasing

Pushing a tag builds the binary and publishes the release automatically:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

## How it works

The scanner issues one native call per folder (`FindFirstFileEx` with `FindExInfoBasic` and `FIND_FIRST_EX_LARGE_FETCH`), which already returns the name, size and timestamp of every entry. There is no second trip to disk for metadata.

**Shared work queue.** Directories live in a concurrent stack that every worker pulls from, with a pending counter for termination. Partitioning by top-level folder does not work: on real installations a single folder holds most of the work. Measured on two machines, `C:\Users` accounted for 81.6% of scan time and `E:\Windows` for 70.7%, which would cap any speedup at 1.2x–1.4x no matter how many workers you add.

**Media detection.** The volume is opened with `dwDesiredAccess = 0` to query `IOCTL_STORAGE_QUERY_PROPERTY` for the seek penalty. Asking for `GENERIC_READ` here would require administrator rights and fail with `ERROR_ACCESS_DENIED` on a normal run, silently forcing every drive down the single-threaded path. Drives with a seek penalty are walked sequentially; SSDs and NVMe use 2 to 6 workers depending on core count.

**Traversal loops.** Junctions (`IO_REPARSE_TAG_MOUNT_POINT`) and symbolic links (`IO_REPARSE_TAG_SYMLINK`) are skipped to avoid cycles and crossing into other volumes. Every other reparse point is traversed normally. Discarding all of them by the generic attribute would make the scanner skip cloud folders such as the OneDrive root, which carries its own tag.

**Folder rollup in one pass.** Folders are stored as parallel arrays with a parent index rather than a graph of objects. Because a child is always created after its parent, the parent's index is always lower, so iterating the array backwards and adding each node into its parent produces the full tree aggregation in O(n) with no recursion and no stack.

**Constant memory.** A 10,000-slot min-heap holds the largest files, so memory does not grow with disk size. A 736,000-file volume peaks at about 60 MB.

**Adaptive progress.** Counters are published every 650 ms; the partial result list is materialized every 2 s so it does not compete with the scan.

Long paths are enumerated through the Win32 extended syntax (`\\?\`), and the manifest declares `longPathAware`.

## Localization

The interface language comes from `CultureInfo.CurrentUICulture`, falling back to English for any unlisted language. Strings are a compile-time table indexed by an enum rather than satellite assemblies, which keeps single-file publishing intact and makes lookup an array index instead of a hash.

To add a language, extend the `Language` enum in `Localization.cs` and add one array in the same order as the `UiText` enum. A debug assertion fails at startup if any table has the wrong length.

## Known limitations

- The search filter only covers the 10,000 largest files in the list, not the whole disk.
- Sizes are logical. Compressed and sparse files report their nominal size, and hard links are counted once per path.
- Folders you cannot read are skipped and counted as inaccessible. In testing, the scanned total came within 95% to 99% of the space Windows reports as used.
- No column sorting yet.

## Privacy

- Sends nothing over the network.
- Does not modify or delete files.
- Runs without elevation (`asInvoker`).

## License

MIT. See [LICENSE](LICENSE).
