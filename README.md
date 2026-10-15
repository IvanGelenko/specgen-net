# PipingSpecPlugin (.NET)

Version 1.3.5: see [the accuracy verification report](ACCURACY-REPORT.md) for
the fixes, checks against project 0873-000-ТХМ, and remaining data limitations.

Native AutoCAD Plant 3D plug-in (C# / .NET 8) that generates a piping
specification (`.docx`) from the active project.

The plug-in reads saved project drawings through Plant 3D APIs. Before a scan,
it checks relevant open drawings and asks the user to save pending edits.
Generation never automatically saves, closes, or reopens drawings.

VT synchronization matches workbook `n/n` to the project's `ID`. The full
designation goes to `Tag`; `Номер` receives exactly three digits after the
medium code: `150-KSN011-LS25CH-NH` → `011`. Invalid numbers leave the existing
value unchanged and appear in the review warnings.

**Refresh ledger** merges rows with the same trimmed ID and line tag in the
review table only. Conflicting cell values are retained as `⚠ value | value`;
an empty variant is shown as `(пусто)`. Cell text wraps and is available in a
tooltip. Underlying Plant 3D records and synchronization targets are preserved.

Synchronization and orphan deletion require a transaction. Write or persistence
errors stop the operation, trigger a rollback attempt and are displayed to the
user. Word output replaces an existing file only after generation succeeds.

## Commands

`SELECTBYLAYER` (**Layers → Select by layer**) lists populated layers with
object counts and a name search. It selects all top-level entities on the chosen
layer in the current model/paper space, including unsaved edits. Block and xref
insertions are selected as whole objects by their insertion layer. The command
does not save the drawing, change layer visibility or modify geometry.

`PSAUDIT` opens the model audit for selected lines in the active drawing.
The checklist uses the same saved-DWG `PartReader`, `LineNumberTag` and entity
handle resolution as **Select by line**, including **Without line number**.
If the drawing has unsaved edits, the audit offers to save it first (in its
original DWG version), like **Select by line**. Parts whose data cannot be read
do not stop the audit; they are listed as not checked. Choose any combination of:

- Wall thickness: pipes, elbows and pipe bends of one line with the same
  nominal diameter must have equal walls (0.01 mm tolerance; different
  diameters are checked separately; unknown thickness or nominal diameter is
  reported as not checked). A tee or reducer end must not be thinner than the
  pipe welded to it — the end with the pipe's nominal diameter (a reducer
  159x8-89x6 has 8 mm on one end and 6 mm on the other; Plant 3D keeps the
  other ends as ports). The wall is read in the part's catalogue length unit,
  not in drawing units (INSUNITS).
- Free physical ports and disconnected portions of a line. Open ends are
  review findings, since some are intentional. Cross-DWG connectivity is excluded.
  A gap that an element with unreadable connections or line number might close
  is reported as not checked.
- Solid-volume intersections between selected lines and all other model-space
  elements, including loaded xrefs and nested attachments. Direct overlays are
  included; nested overlays that do not propagate into the host are excluded.
  Each insertion retains its transform, source path and identity; selecting an
  external element selects its top-level xref insertion. The full geometry is
  checked independently of XCLIP. Missing/unloaded xrefs, unavailable geometry
  and unsupported nonuniform solid scaling are explicitly reported as incomplete.
  Obstacles are any 3D objects: 3D solids (also ones modelled with history),
  Plant 3D equipment, Plant 3D structure members (beams, columns — AutoCAD
  exposes them as curves, so they used to be skipped), closed meshes, blocks.
  Symbolic welds/port markers and 2D objects (lines, text, dimensions, hatches,
  leaders, images, underlays) have no volume to test. For a MINSERT array only
  the first copy is checked, and this is reported as incomplete.

Insulation-only overlaps are ignored when both elements have the same nonempty
line number. Body intersections remain reportable. Pipes and inline assets are
exploded twice using transient clones to separate their insulation envelopes.
Unknown or different line numbers do not suppress insulation overlaps.

Supports are excluded only against their directly attached piping components,
using Plant 3D connection data even when the connectivity audit is disabled.
An unattached support or its contact with another pipe remains a collision.
Xref attachment keys include the insertion path to keep instances separate.

Parts joined through a Plant 3D connection object (weld, flanged joint, stub-in)
are not reported as collisions, however their bodies overlap (a branch in the
header wall, an olet base, a pipe end inside a tee); the same holds for the
connection object against the parts it joins. The summary and the report show
how many such overlaps were left out. Parts that overlap without a connection
object — a tee laid over a pipe, a branch pushed into a header without a
stub-in — are still reported.

The audit does not change the drawing. **Show in model** (or a double-click on
a row) hides the results window, zooms to the issue — a collision to where the
two parts overlap — and highlights its elements (an element nested in an xref or
block itself, not the whole insertion); Enter brings the same results back.
**Select and close** selects the elements of the chosen rows (for an xref
element, its insertion) and closes the window. A Word DOCX report can be saved
with the built-in System Design-based
template (`Resources/AuditReportTemplate.docx`). The report includes scope,
selected lines, check totals and every finding with source and element handles.
`PSAUDITJSON` remains available as a scripted diagnostic report
for all lines, prompting for a new output filename (a bare name is placed next
to the drawing). It reads the same saved-DWG line numbers as `PSAUDIT` and stops
if the drawing has unsaved edits. New checks implement
`IAuditRule` and are registered in `AuditRules.All`.

`PSCOLORBYLAYER` (**Layers → Color by layer**) sets local model objects,
ordinary block contents and attributes to ByLayer. A valve on a separate
layer keeps that layer and receives the layer color of pipes with the same
`LineNumberTag` (ACI and TrueColor supported). If pipe layers have conflicting
colors or the line cannot be resolved, the valve stays ByLayer with a warning.
Valve overrides are refreshed when the command runs again. Xref contents are
read-only. Changes run in one transaction without saving the drawing; layer
membership and lock states are preserved.

| Command | What it does |
|---|---|
| `WRITESPEC` | Generate a spec; scope (whole project / active drawing) comes from the saved preference and the picker dialog. |
| `WRITESPECPROJ` | Generate a spec for the **whole project**, ignoring the saved scope. Bound to the ribbon's **Весь проект** button. |
| `WRITESPECDWG` | Generate a spec for the **active drawing** only. Bound to the ribbon's **Текущий чертёж** button. |
| `SYNCVT` | Sync pipe-line parameters from a Ведомость трубопроводов (ВТ) Excel workbook into P3dLineGroup rows, matching the ВТ column «n/n» to the line's custom `ID` property. Empty ВТ cells never clear project values. Bound to the ribbon's **Синхронизация ВТ** button. |

After `NETLOAD`, AutoCAD's ribbon shows a **Спецификация** tab with
**Весь проект** and **Текущий чертёж** buttons, an **Изоляция** panel
(insulation take-off per line: **Весь проект** / **Текущий чертёж** /
**Методика** — `INSULSPECPROJ` / `INSULSPECDWG` / `INSULHELP`), plus a **Ведомость
трубопроводов** panel with **Синхронизация ВТ**. The output template is
picked from the dialog the commands open.

## Targets

- AutoCAD Plant 3D **2025** (R25.0) and **2026** (R25.1) — one DLL for both.
  It is built against the 2025 managed wrappers (25.0.0.0, PnP 16.0); 2026
  binds those references to its newer assemblies. A DLL built against 2026
  would not load in 2025.
- Windows 10/11 x64

## Build

Requires Visual Studio 2022 (or Build Tools for VS 2022) and an installed
copy of AutoCAD Plant 3D **2025** (the project references `AcMgd.dll`,
`AcDbMgd.dll`, `AcCoreMgd.dll` and `PnP*.dll` from
`C:\Program Files\Autodesk\AutoCAD 2025`). Do not point `AcadInstallDir` at
2026 for a release build — the bundle declares `SeriesMin="R25.0"`.

```
msbuild PipingSpecPlugin.sln /p:Configuration=Release
```

If AutoCAD is installed in a non-standard location, pass the install root:

```
msbuild PipingSpecPlugin.sln /p:Configuration=Release ^
        /p:AcadInstallDir="D:\Autodesk\AutoCAD 2025"
```

## Smoke test

1. Build the solution.
2. Open Plant 3D 2025 or 2026 and a project drawing.
3. `NETLOAD` → pick `src\PipingSpecPlugin\bin\Release\PipingSpecPlugin.dll`.
4. The ribbon should show a **Спецификация** tab with **Весь проект** and
   **Текущий чертёж** buttons (registered programmatically — no `.cuix`).
5. Click **Весь проект** (or type `WRITESPEC`) → the picker dialog opens
   with statuses / categories / lines from the project. Confirm and pick a
   `.docx` output path.
6. The resulting file has the spec table filled per line and category. If
   no Plant 3D project is open, a notice explains that a project must be
   opened first.
7. A run log is written alongside each generation — use it to diagnose a
   part that didn't make it into the spec.

> **Rebuild while AutoCAD is open?** AutoCAD locks `PipingSpecPlugin.dll`
> for the rest of the session after `NETLOAD`, so a second `dotnet build`
> into `bin\Release\` will fail with MSB3026. Either close AutoCAD before
> rebuilding, or pass `-p:OutDir=bin/Release-vNEXT/` and `NETLOAD` the
> versioned copy. AutoCAD doesn't notice you replaced the file — you
> *must* restart it to reload an updated DLL.

## Settings

User settings live at `%APPDATA%\PipingSpecPlugin\settings.json` (output
directory, template path, picker preferences). See `ARCHITECTURE.md` for
the schema and migration model.

## Updates

On load the plug-in checks GitHub Releases for a newer build (in the
background — load is never blocked) and, if one is found, shows a
notification with an **Обновить** button. It downloads
`PipingSpecPlugin-Setup.exe` and launches it; the install completes after
AutoCAD is closed (the loaded DLL is locked for the session). The check
can be turned off via the **Проверять обновления** checkbox in the picker.

**Publishing a release (automated):** push a version tag and CI does the
rest —

```
git tag v1.2.0
git push origin v1.2.0
```

The `.github/workflows/release.yml` workflow runs on a **self-hosted Windows
runner** (labels `self-hosted, windows` — the defaults a Windows runner gets)
that has AutoCAD Plant 3D 2025 and Inno Setup (6 or 7) — a GitHub-hosted runner
can't build the plug-in because it references AutoCAD/Plant 3D DLLs from the
local install. The
workflow stamps the version from the tag, runs the tests, builds
`PipingSpecPlugin-Setup.exe` via `build.cmd`, and publishes a GitHub Release
with that asset. The tag `vX.Y.Z` is the single source of truth for the
version; the asset name stays exactly `PipingSpecPlugin-Setup.exe` because
the in-plugin updater matches it by name.

**Manual fallback** (runner unavailable): bump `VERSION`, run `build.cmd`,
then create the release and attach `dist_installer\PipingSpecPlugin-Setup.exe`
by hand under the same `vX.Y.Z` tag.

## Navisworks audit (NavisAudit)

`src/NavisAudit` is a separate plug-in for **Navisworks Manage 2024, 2025 and
2026** (.NET Framework 4.8; one DLL per release, because the Navisworks API is
versioned per release: 21.0 / 22.0 / 23.0). **Надстройки → Аудит трубопроводов**
opens the PSAUDIT window for the open model: the same lines, checks, table and
Word report. Clicking a row selects its elements; a double click zooms to them.

The rules are the Plant 3D plug-in's own (`Audit/*.cs`, linked by source).
NavisAudit only reads the model into the same `AuditSnapshot`:

- Plant 3D objects: the **AutoCAD** property tab (Plant 3D Object Enabler):
  class, line, size, wall, material, ports. NWCs written without the enabler
  hold `ACAD_PROXY_ENTITY` proxies with no data; they are reported once.
- Connections: geometry snap points are the ports. Ports within 10 mm of each
  other are joined (the gasket gap). A branch end on a pipe is a stub-in,
  checked against the TapWeld connector. Connector COG and gasket/bolt boxes
  locate the connection objects.
- Insulation is a second object on the `…_Insulation` layer. It is attached to
  its part, and `InsulationJointPolicy` applies.
- Copies (a file or xref loaded twice in one place) are reported once per
  repeated xref and left out of the audit.
- Clashes: a temporary Clash Detective test (Hard) of the selected lines
  against the whole model, removed afterwards. The Plant 3D policies apply:
  supports, joints, tolerance, structure nodes.

Not offered: bolt length. The model has no flange faces or port names per joint.

```
build-navis.cmd        → dist_navis\NavisAudit-Setup.exe (per-user bundle)
tools\NavisAuditSmoke  → runs the JSON plug-in in a hidden Navisworks:
  NavisAuditSmoke <model.nwd|nwc|dwg> <NavisAudit.dll | -> <out.json> [lines=…] [tolerance=2] [structures=1] [parts=1] [docx=…] [show=1]
tools\NavisAuditPreview → renders the window from such a JSON report
```

With the bundle installed, Navisworks loads the bundle's DLL (same plug-in id)
even when the smoke tool is given another one. Copy a fresh build into
`%APPDATA%\Autodesk\ApplicationPlugins\NavisAudit.bundle\Contents\<year>\`.
