# Img2PDF

## Project Overview
A single-purpose Windows utility: select image files in File Explorer, right-click
**Combine to PDF**, reorder pages as thumbnails in a grid, save as PDF. No PDF editor,
no OCR, no cloud — see `docs/pagemerge-spec.md` for the full spec (working title
"PageMerge" in that document; the project itself is named Img2PDF).

## Build
```
TBD — no .sln exists yet. Greenfield; build milestone M1 (spec §6) is a console app,
not yet started.
```

## Tests
```
TBD — no tests exist yet.
```

## Architecture
See `docs/CURRENT_APPLICATION.md` for a living summary and `docs/pagemerge-spec.md`
for full detail (this is the authoritative spec — read it before starting any
implementation work).

Three components, one MSIX package:
- `PageMerge.ShellExtension/` — C++/WinRT, `IExplorerCommand`, launches the app. Must
  return in well under 100ms and never throw (an unhandled exception here degrades
  Explorer itself).
- `PageMerge.App/` — C# / .NET 8 / WinUI 3. All real work: UI, image decode, PDF
  generation.
- `PageMerge.Package/` — Windows Application Packaging Project → MSIX.

Build order follows spec §6 milestones (M1 console PDF engine → M2 WinUI shell → M3
save flow → M4 shell extension → M5 MSIX packaging → M6 Store readiness). Each
milestone should be independently runnable — validate PDF output quality (M1) against
real scans before building any UI.

## Key Conventions

### C++ (ShellExtension)
- Allman braces — `{` always on its own line, no exceptions (if, for, while, functions, namespaces, classes)
- Single-statement `if` bodies always braced — never `if (x) return y;` on one line
- 4-space indentation
- One variable declaration per line — never `Type a = x, b = y;`
- Blank lines between sequential `if` blocks that handle distinct logical concerns
- Constructor initializer list: `:` on same line as closing `)`, members indented on following lines
- Namespace: `namespace foo\n{` (brace on own line), close with `} // namespace foo`
- Comments: `//` with space after `//`; explain *why*, not *what*
- No space before `(` in function calls
- Line length: up to 100 characters before wrapping
- No multi-line comment blocks or docstrings

### C# (App / Package)
- Allman braces — `{` always on its own line
- 4-space indentation
- PascalCase for types and public members; camelCase for private fields and locals
- XML doc comments (`///`) only on public API where the why is non-obvious
- No space before `(` in method calls
- Line length: up to 120 characters before wrapping

## Notes
- Never modify, move, or delete the user's source image files — read-only access only.
- No telemetry, no network calls, no auto-updater. The app must work identically offline.
- JPEG passthrough is a hard requirement for PDF output quality/size — verify PDFsharp
  is not silently re-encoding (spec §4.3).
- Full acceptance criteria: `docs/pagemerge-spec.md` §7.
