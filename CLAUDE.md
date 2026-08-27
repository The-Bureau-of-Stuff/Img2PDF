# Img2PDF

## Project Overview
A single-purpose Windows utility: select image files in File Explorer, right-click
**Combine to PDF**, reorder pages as thumbnails in a grid, save as PDF. No PDF editor,
no OCR, no cloud — see `docs/pagemerge-spec.md` for the full spec (working title
"PageMerge" in that document).

**Naming split, deliberate:** the codebase — repo, solution, all five projects
(`Img2PDF.App`/`.Core`/`.Cli`/`.ShellExtension`/`.Package`), namespaces, folder names —
stays `Img2PDF.*`. The product/store-facing name is **ClickTo: PDF**: window title, PDF
`/Producer` metadata, the MSIX manifest's `DisplayName`/`PublisherDisplayName`/
`SurrogateServer` name, and all `docs/store/*` submission copy. Don't "fix" this
mismatch by renaming code to match the product name (or vice versa) — it's an
intentional internal-codename-vs-brand split, not drift. The `Identity Name`/`Publisher`
in `Package.appxmanifest` are still `Img2PDF`/placeholder pending the real Partner
Center name reservation (see `docs/store/submission-checklist.md` §1).

## Build
```
Img2PDF.sln has four project types:
- Img2PDF.Core / Img2PDF.Cli / Img2PDF.App (csproj) — build via `dotnet build`/`dotnet run`.
- Img2PDF.ShellExtension (vcxproj, native WRL COM DLL) — needs VS2022's C++ desktop
  workload; build via VS2022's own MSBuild.exe, not `dotnet build`.
- Img2PDF.Package (wapproj, MSIX packaging) — needs the "Universal Windows Platform
  development" VS2022 workload; also built via VS2022's MSBuild.exe. Signing needs a
  local self-signed dev cert (Img2PDF.Package_TemporaryKey.pfx, gitignored, per-machine —
  regenerate via New-SelfSignedCertificate, Subject must match the manifest's Publisher
  exactly, and the cert must be imported into LocalMachine\TrustedPeople for
  Add-AppxPackage to accept it).

Local dev/test loop for the shell extension itself (fast, no MSIX rebuild needed):
regsvr32-register Img2PDF.ShellExtension.dll directly (HKCU, no admin) with
Img2PDF.App's build output copied alongside it — see build_environment memory for the
exact commands. This surfaces the command under "Show more options", not the main
context menu; main-menu placement only comes from the MSIX manifest (Img2PDF.Package).
```

## Tests
```
dotnet test — xunit projects under tests/ (Img2PDF.Core.Tests, Img2PDF.App.Tests).
```

## Architecture
See `docs/CURRENT_APPLICATION.md` for a living summary and `docs/pagemerge-spec.md`
for full detail (this is the authoritative spec — read it before starting any
implementation work).

Three components, one MSIX package:
- `Img2PDF.ShellExtension/` — native C++ (WRL), `IExplorerCommand`, launches the app. Must
  return in well under 100ms and never throw (an unhandled exception here degrades
  Explorer itself).
- `Img2PDF.App/` — C# / .NET 8 / WinUI 3. All real work: UI, image decode, PDF
  generation. Framework-dependent WindowsAppSDK deployment (not self-contained) — required
  for MSIX packaging to work; self-contained conflicts with packaged WinRT activation.
- `Img2PDF.Package/` — Windows Application Packaging Project → MSIX.

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

## Code standards

These are generation-time rules. They are deliberately short because they cost context on
every turn. The full review criteria live in the `slop-review` agent and are only paid for
when a review runs.

**Search before you write.** Grep for an existing helper, type, constant, or utility that
already does this before adding a new one. Adding a near-duplicate is a defect, not a
neutral choice. If you looked and found nothing, that is worth one line in your summary.

**Name things by what they hold or do, not what they are about.**
- A name says what it returns or what it changes: `activeUserEmails`, not `userData`.
- Never a category noun on its own: Manager, Handler, Helper, Util, Service, Processor,
  Info, Wrapper, Data, Item.
- Never a name that lies: no `get*` that mutates or does I/O, no `validate*` that also
  saves, no `is*`/`has*` returning a non-boolean, no `*All` that silently paginates.
- Don't stutter against the container: `Users.create` not `UserService.createUser`;
  `user.name` not `user.userName`.
- The surrounding file's existing vocabulary beats every rule above. If this module says
  `client`, don't introduce `customer` for the same thing.

**Split functions on abstraction level, not line count.**
- A function should sit at one altitude: orchestration, or domain logic, or I/O — not all
  three in one body.
- If you can't name it without "and", or the best name is handle/process/manage/do + noun,
  it is doing two things.
- A boolean parameter that switches behaviour is two functions.
- **And the opposite failure, which is just as bad:** do not extract a block that would need
  four or more parameters or return three or more values — that is a cut across the grain,
  not a seam. Do not create a helper called from exactly one place whose name describes its
  position in the caller (`step2`, `handleRest`, `_inner`, `processData2`). One sixty-line
  function at a single altitude beats six ten-line functions that only make sense read in
  call order.

**No speculative structure.** No interface or abstract base with one implementation, no
config option nothing sets, no extensibility hook for a second case that does not exist.
Equally: don't inline something used in three places to avoid "over-abstracting".

**Errors: catch only what you can act on.** No try/catch around code that cannot throw. No
broad catch that logs and continues with now-invalid state. Never swallow an exception to
make a test pass.

**Comments say why, not what.** Delete any comment that restates the line below it. Keep
comments that record a constraint, a rejected alternative, or a non-obvious reason.

**Tests must be able to fail.** For every test you write, you should be able to name the
specific bug it catches. Do not mock the unit under test. Do not assert only that something
is non-null, non-empty, or did not throw. Do not derive the expected value by running the
code and pasting the output.

**Dependencies.** Don't add one for something under about twenty lines of obvious code.
Don't reimplement what the standard library already does correctly.

**Say what you didn't do.** When you finish a unit of work, state plainly anything you
guessed at, stubbed, skipped, or could not verify. An unflagged guess is worse than an
admitted gap.

**Review cadence.** Run `/checkpoint` after each unit of work, and `/slop-check` before a PR
or at the end of a feature. Both are available from user scope — nothing to install here.
For copy-paste duplication specifically, `npx jscpd .` uses this repo's `.jscpd.json`.

## Notes
- Never modify, move, or delete the user's source image files — read-only access only.
- No telemetry, no network calls, no auto-updater. The app must work identically offline.
- JPEG passthrough is a hard requirement for PDF output quality/size — verify PDFsharp
  is not silently re-encoding (spec §4.3).
- Full acceptance criteria: `docs/pagemerge-spec.md` §7.
