# MD Viewer

Minimal Markdown viewer for Windows.

- Opens `.md` files rendered (formatted) by default.
- Optional plain-text editing (`View > Edit plain text`, `Ctrl+E`).
- No dependencies: uses the .NET Framework 4.x already included in Windows 10/11.

## Build

```cmd
build.cmd
```

Output: `bin\MdViewer.exe`

## Usage

```cmd
bin\MdViewer.exe path\to\file.md
```

Or use `File > Open` (`Ctrl+O`). Save with `Ctrl+S`.

## Security

- No HTML, JavaScript or embedded RTF is interpreted; the document is rendered as styled text.
- Links and images are displayed as text only: nothing is opened, launched or downloaded.
- No network access.
- Files larger than 5 MB are rejected.
- Saves are atomic (temp file + replace) so a failed write never corrupts the document.
- Runs with the invoking user's privileges (`asInvoker`).
