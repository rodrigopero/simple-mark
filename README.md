# Simple Mark

Minimal Markdown viewer for Windows.

- Opens `.md` files rendered (formatted) by default.
- Optional plain-text editing (`View > Edit plain text`, `Ctrl+E`).
- Runs on the .NET Framework 4.x already included in Windows 10/11.

## Build

```cmd
build.cmd
```

Output: `bin\SimpleMark.exe` plus the libraries from `lib\`. The build refuses to run if any
library in `lib\` does not match its pinned hash in `lib\SHA256SUMS`.

## Usage

```cmd
bin\SimpleMark.exe path\to\file.md
```

Or use `File > Open` (`Ctrl+O`). Save with `Ctrl+S`.

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
- `msftedit.dll` is loaded only from System32; the app runs with the invoking user's privileges (`asInvoker`).

## Third-party libraries

Taken from nuget.org (`lib/net462` builds), package hashes verified against the NuGet catalog.

| Library | Version | License |
|---|---|---|
| Markdig | 1.4.1 | BSD-2-Clause |
| System.Memory | 4.6.3 | MIT |
| System.Buffers | 4.6.1 | MIT |
| System.Numerics.Vectors | 4.6.1 | MIT |
| System.Runtime.CompilerServices.Unsafe | 6.1.2 | MIT |
