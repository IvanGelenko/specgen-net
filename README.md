# PipingSpecPlugin (.NET)

Native AutoCAD Plant 3D plug-in (C# / .NET 8) that generates a piping
specification (`.docx`) from the active project.

The plug-in talks to the live Plant 3D in-memory model through the
`PnP*Mgd` APIs, so it sees unsaved changes and registers its ribbon button
natively — no `.cuix` plumbing required.

## Commands

| Command | What it does |
|---|---|
| `WRITESPEC` | Generate a spec; scope (whole project / active drawing) comes from the saved preference and the picker dialog. |
| `WRITESPECPROJ` | Generate a spec for the **whole project**, ignoring the saved scope. Bound to the ribbon's **Весь проект** button. |
| `WRITESPECDWG` | Generate a spec for the **active drawing** only. Bound to the ribbon's **Текущий чертёж** button. |

After `NETLOAD`, AutoCAD's ribbon shows a **Спецификация** tab with
**Весь проект** and **Текущий чертёж** buttons. The output template is
picked from the dialog the commands open.

## Targets

- AutoCAD Plant 3D **2026** (R25.0) and newer
- Windows 10/11 x64

## Build

Requires Visual Studio 2022 (or Build Tools for VS 2022) and an installed
copy of AutoCAD Plant 3D 2026 (the project references `AcMgd.dll`,
`AcDbMgd.dll`, `AcCoreMgd.dll` and `PnP*.dll` from
`C:\Program Files\Autodesk\AutoCAD 2026`).

```
msbuild PipingSpecPlugin.sln /p:Configuration=Release
```

If AutoCAD is installed in a non-standard location, pass the install root:

```
msbuild PipingSpecPlugin.sln /p:Configuration=Release ^
        /p:AcadInstallDir="D:\Autodesk\AutoCAD 2026"
```

## Smoke test

1. Build the solution.
2. Open Plant 3D 2026 and a project drawing.
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
runner** (labels `self-hosted, windows, autocad`) that has AutoCAD Plant 3D
2026 and Inno Setup 6 — a GitHub-hosted runner can't build the plug-in
because it references AutoCAD/Plant 3D DLLs from the local install. The
workflow stamps the version from the tag, runs the tests, builds
`PipingSpecPlugin-Setup.exe` via `build.cmd`, and publishes a GitHub Release
with that asset. The tag `vX.Y.Z` is the single source of truth for the
version; the asset name stays exactly `PipingSpecPlugin-Setup.exe` because
the in-plugin updater matches it by name.

**Manual fallback** (runner unavailable): bump `VERSION`, run `build.cmd`,
then create the release and attach `dist_installer\PipingSpecPlugin-Setup.exe`
by hand under the same `vX.Y.Z` tag.
