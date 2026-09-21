# DiskLit

**Find out what is filling your disk. Read-only, no administrator rights, open source.**

![DiskLit scanning a drive](docs/screenshot-dark.png)

### [Download the latest release](https://github.com/romolocraft/DiskLit/releases/latest)

One file. No installer. No runtime to set up. Requires 64-bit Windows 10 or 11.

---

## Why another disk analyzer

DiskLit does not try to be the fastest tool available. It tries to be the one you can run
immediately, on any machine, without handing it administrator rights, and whose source you can read
in an afternoon.

| | DiskLit | MFT-based analyzers (WizTree and similar) | Explorer folder properties |
|---|---|---|---|
| Administrator rights | **Not required** | Required — reading the raw MFT needs elevation | Not required |
| Speed on a large volume | Seconds | Fastest available | Minutes |
| Source code | **Open, MIT** | Varies by product | Closed |
| Explains Windows system files | **Yes** | No | No |
| Network access | **None** | Varies | None |
| Deletes anything on its own | **No** | No | No |

**Be clear about the trade-off:** tools that parse the NTFS Master File Table directly are
genuinely faster than DiskLit, and if raw speed with elevation is what you need, use one of those.
DiskLit walks the filesystem with `FindFirstFileEx`, which costs some speed and buys you a scan
that never asks for administrator rights.

In practice it is still quick. The scan in the screenshot above covered **942,834 files across
140,663 folders on a 446 GB NTFS SSD in three seconds** — the counts, the elapsed time and the rate
are all in the status bar of that image. The first scan after a reboot is slower, because the
filesystem cache is cold.

## What it does

**Files.** Finds the largest files on any drive and shows where they live, with a folder breakdown,
a per-type size legend, and a filter that narrows the list to a single folder.

Windows system files are flagged and explained instead of merely listed. Select `hiberfil.sys` and
DiskLit tells you it mirrors your RAM for hibernation, that Fast Startup uses it too, and that the
way to reclaim the space is `powercfg /h off` — not deleting the file. The same applies to
`pagefile.sys`, `swapfile.sys`, crash dumps, `Windows.old`, `WinSxS`, `System Volume Information`
and the installer cache. These are exactly the entries that dominate a "what is eating my disk"
list and exactly the ones a user should not remove by hand.

**Active.** A live process list with CPU, memory, executable path and an origin column that marks
processes served from protected Windows directories. Backed by a single `NtQuerySystemInformation`
call rather than one performance counter per process. It is read-only — DiskLit never ends a
process — and it stops sampling entirely when the tab is not visible.

**Cleanup.** Reviews known temporary locations and narrowly identified application caches.
The central typed catalog distinguishes Windows components, NTFS metadata, registry hives,
DriverStore, Windows Update, Store packages, GPU and launcher caches, application profiles
and game data. A vendor name by itself is never a cleanup rule. System update stores are
explained but must be managed through Windows Settings.

Deep analysis inspects old, large folders under LocalAppData and RoamingAppData. These are
only possible leftovers, not proof that an application was uninstalled. They start unchecked.
Known profiles, saves, mods, projects and screenshots are protected. Installed-program names,
installation locations, active-process names and accessible executable paths are cross-checked.

Every candidate is inspected in one metadata traversal. Inaccessible descendants and reparse
points reject the whole candidate. All selected candidates must pass exclusive-open checks,
path checks and metadata fingerprint comparison before the Shell is allowed to start.
Items can only be sent to the **Recycle Bin**; the application vetoes permanent deletion.

Using it is deliberate by design. Every group expands so you can pick individual entries, each one
shows its size and full path, and selecting an entry explains what created it, whether it can be
rebuilt and what the risk of removing it is. Recommended groups come checked; deep candidates never
do. Nothing runs until you confirm a dialog that states the count and the total size.

Windows Shell operations are not atomic. A concurrent change after preflight or a Shell error
can leave part of a batch in the Recycle Bin. The UI distinguishes preflight cancellation from
an operation that started and then failed. See [cleanup design and validation](docs/cleanup-safety.md)
for the exact guarantees and limits.

**Appearance.** Follows the Windows light/dark setting and reacts to theme changes while running.
Available in English, Portuguese, Spanish and Russian. Language, theme and search engine are saved
to a plain text file in `%APPDATA%\DiskLit\settings.txt`.

**Removable drives.** Plugging in or ejecting a drive updates the list on its own — DiskLit listens
for `WM_DEVICECHANGE` and refreshes, so you never have to restart it to see a USB disk. Closing the
window cancels any scan or cleanup in progress instead of leaving work running in the background.

| Light | Dark |
|---|---|
| ![Light theme](docs/screenshot-light.png) | ![Dark theme](docs/screenshot-dark.png) |

## Privacy and security

A disk analyzer sees your whole filesystem, so it is fair to ask what it does with that.

- **No network access.** There is no HTTP client, no telemetry and no update check anywhere in this
  repository. The only thing that ever leaves the machine is a web search you explicitly ask for by
  right-clicking a process, and that opens in your browser with the engine you picked.
- **Analysis is strictly read-only.** Scanning opens directories for enumeration and nothing else.
- **Nothing is deleted without you.** Cleanup requires selecting entries and confirming a dialog,
  and it moves items to the Recycle Bin rather than erasing them.
- **No administrator rights.** The manifest requests `asInvoker`. Folders your account cannot read
  are skipped and counted as inaccessible.
- **Reproducible releases.** Every binary is built from this source by
  [GitHub Actions](.github/workflows/release.yml) on a clean runner, never uploaded from a
  developer machine. Each release ships a `SHA256SUMS.txt` so you can verify what you downloaded:

  ```powershell
  Get-FileHash .\DiskLit-win-x64.exe -Algorithm SHA256
  ```

The executable is not signed with a paid code-signing certificate, so Windows SmartScreen shows a
warning the first time you run it. If you would rather not dismiss that warning, build it yourself
from source below, which is the reason the source is public.

## Known limitations

- The search filter only covers the 10,000 largest files held in the list, not the whole disk.
- Sizes are logical. Compressed and sparse files report their nominal size, and hard links are
  counted once per path.
- Folders you cannot read are skipped and counted as inaccessible. In testing, the scanned total
  came within 95% to 99% of the space Windows reports as used.
- Cleanup does not empty the Recycle Bin. Emptying it is permanent by definition, which would
  contradict the guarantee that everything cleanup does can be undone.
- Deep candidates are a heuristic, not a verdict. The installed-program inventory cannot prove an
  application is gone: portable programs, custom install locations and process paths a normal
  account cannot read may go unidentified. This is why they always start unchecked and why the
  interface asks you to review them rather than trusting the match.
- The integrity check fingerprints metadata — relative paths, attributes, sizes, timestamps and
  file identities — not file contents. A change that deliberately preserves all of those can pass
  it, and an application can use data without an observable open handle.
- A cleanup batch is validated as a whole but is not an atomic filesystem transaction. If the Shell
  errors partway through, some items are already in the Recycle Bin; the app reports that case
  separately from a cancelled preflight instead of claiming success.
- Executable paths in the Active tab are blank for protected system processes, which a normal user
  account cannot open.
- The origin column marks processes by **location**, not by signature. A file under `System32` was
  almost certainly placed there by Windows, since writing there needs administrator rights — but it
  is not the same as verifying an Authenticode signature, which DiskLit does not do yet.
- No column sorting yet.

## Build from source

Requires the .NET 8 SDK.

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

`IncludeNativeLibrariesForSelfExtract` is not optional. Without it the publish leaves five native
DLLs beside the executable, and the `.exe` on its own refuses to start.

For a 320 KB build that reuses an installed .NET 8 Desktop Runtime
instead of bundling it:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o publish
```

## How it works

The scanner issues one native call per folder (`FindFirstFileEx` with `FindExInfoBasic` and
`FIND_FIRST_EX_LARGE_FETCH`), which already returns the name, size and timestamp of every entry.
There is no second trip to disk for metadata.

**Shared work queue.** Directories live in a concurrent stack that every worker pulls from, with a
pending counter for termination. Partitioning by top-level folder does not work: on real
installations a single folder holds most of the work. Measured on two machines, `C:\Users`
accounted for 81.6% of scan time and `E:\Windows` for 70.7%, which would cap any speedup at
1.2x–1.4x no matter how many workers you add.

**Media detection.** The volume is opened with `dwDesiredAccess = 0` to query
`IOCTL_STORAGE_QUERY_PROPERTY` for the seek penalty. Asking for `GENERIC_READ` here would require
administrator rights and fail with `ERROR_ACCESS_DENIED` on a normal run, silently forcing every
drive down the single-threaded path. Drives with a seek penalty are walked sequentially; SSDs and
NVMe use 2 to 6 workers depending on core count.

**Traversal loops.** Junctions (`IO_REPARSE_TAG_MOUNT_POINT`) and symbolic links
(`IO_REPARSE_TAG_SYMLINK`) are skipped to avoid cycles and crossing into other volumes. Every other
reparse point is traversed normally. Discarding all of them by the generic attribute would make the
scanner skip cloud folders such as the OneDrive root, which carries its own tag.

**Folder rollup in one pass.** Folders are stored as parallel arrays with a parent index rather
than a graph of objects. Because a child is always created after its parent, the parent's index is
always lower, so iterating the array backwards and adding each node into its parent produces the
full tree aggregation in O(n) with no recursion and no stack.

**Constant memory.** A 10,000-slot min-heap holds the largest files, so memory does not grow with
disk size. A 736,000-file volume peaks at about 60 MB.

Long paths are enumerated through the Win32 extended syntax (`\\?\`), and the manifest declares
`longPathAware`.

## Localization

The interface language comes from `CultureInfo.CurrentUICulture` unless you pick one in Options,
falling back to English for any unlisted language. Strings are a compile-time table indexed by an
enum rather than satellite assemblies, which keeps single-file publishing intact and makes lookup
an array index instead of a hash.

To add a language, extend the `Language` enum in `Localization.cs` and add one array in the same
order as the `UiText` enum. A debug assertion fails at startup if any table has the wrong length.

## Contributing

Issues and pull requests are welcome. The project has no external dependencies beyond the .NET 8
SDK, so `dotnet build` is the whole setup.

The test suite runs against synthetic fixtures in the temporary directory and never calls the
cleanup, Shell or delete paths, so it is safe to run on your own machine:

```powershell
dotnet run --project tests/DiskLit.Tests.csproj -c Release
```

Append `-- --visual` to open the production controls filled with synthetic rows and check theme,
DPI and theme rendering without scanning anything. [Cleanup design and
validation](docs/cleanup-safety.md) documents what the suite covers and what it deliberately
does not.

## Releasing

Pushing a tag builds the binary, generates the checksum file and publishes the release:

```powershell
git tag v1.3.0
git push origin v1.3.0
```

## License

MIT. See [LICENSE](LICENSE).
