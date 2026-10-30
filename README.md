# Simple Mark

Minimal, secure Markdown viewer for Windows.

- Opens `.md` files rendered (formatted) by default.
- Optional plain-text editing (`View > Edit plain text`, `Ctrl+E`).
- Light and dark mode (`View > Dark mode`, `Ctrl+D`); starts with the Windows app theme.
- Small and dependency-free: runs on the .NET Framework 4.8 already included in Windows 10/11.

## Download

1. Download `SimpleMark-<version>-win.zip` from the [latest release](https://github.com/rodrigopero/simple-mark/releases/latest).
2. Optionally verify it against the `.sha256` file published next to it:
   ```powershell
   Get-FileHash .\SimpleMark-<version>-win.zip -Algorithm SHA256
   ```
3. Extract the folder anywhere and run `SimpleMark.exe`. No installation is needed.

The executable is not code-signed, so the first time you run it Windows SmartScreen may show
"Windows protected your PC". Choose **More info > Run anyway**. Release packages are built from
the tagged source by GitHub Actions, see [`.github/workflows/release.yml`](.github/workflows/release.yml).

To open `.md` files with a double click, use **Open with > Choose another app** on any `.md` file
and pick `SimpleMark.exe`.

## Usage

```cmd
SimpleMark.exe path\to\file.md
```

Or use `File > Open` (`Ctrl+O`). Save with `Ctrl+S`.

## Build

Requires only Windows: the C# compiler of the built-in .NET Framework is used.

```cmd
build.cmd
```

Output: `bin\SimpleMark.exe` plus the libraries from `lib\`. The build refuses to run if any
library in `lib\` does not match its pinned hash in `lib\SHA256SUMS`.

## How it works

[Markdig](https://github.com/xoofx/markdig) parses the document (CommonMark, pipe tables,
strikethrough, task lists). The viewer walks the resulting syntax tree and renders it as RTF
in a RichEdit 4.1 control, so no browser engine is involved.

## Security

- Raw HTML is disabled in the parser and shown as plain text; nothing is executed.
- All document text is escaped before it reaches the RTF stream, so a document cannot inject formatting commands.
- Links are not clickable; nothing is opened or launched.
- Images are loaded only from local disks: URLs, UNC/network paths, devices and alternate data streams are refused,
  and every image is re-encoded before display.
- No network access.
- Files larger than 5 MB (documents) or 20 MB (images) are rejected; parser nesting depth is limited.
- Saves are atomic (temp file + replace) so a failed write never corrupts the document.
- System DLLs are loaded only from System32; the app runs with the invoking user's privileges (`asInvoker`).

## Releasing

1. Update the version in `src/AssemblyInfo.cs`.
2. Commit, then push an annotated tag whose message becomes the release notes
   (`--cleanup=verbatim` keeps Markdown `#` headings, which git would otherwise strip as comments):
   ```cmd
   git tag -a v1.2.3 --cleanup=verbatim -F notes.md
   git push origin v1.2.3
   ```
3. The release workflow checks that the tag matches the version, builds, and publishes the zip and its SHA-256.

## License

[MIT](LICENSE). Third-party libraries and their licenses are listed in
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt):

| Library | Version | License |
|---|---|---|
| Markdig | 1.4.1 | BSD-2-Clause |
| System.Memory | 4.6.3 | MIT |
| System.Buffers | 4.6.1 | MIT |
| System.Numerics.Vectors | 4.6.1 | MIT |
| System.Runtime.CompilerServices.Unsafe | 6.1.2 | MIT |

Library binaries were taken from nuget.org (`lib/net462` builds) and verified against the NuGet catalog hashes.
