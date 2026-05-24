# PipingSpecPlugin (.NET)

Native AutoCAD Plant 3D plug-in (C# / .NET 8) that generates a piping
specification from the active project.

This is a full rewrite of the Python-based
[specgen](https://github.com/IvanGelenko/specgen) tool. The Python edition
talks to `Piping.dcf` directly via SQLite; this edition talks to the live
Plant 3D in-memory model through the `PnP*Mgd` APIs, so it sees unsaved
changes and can register a ribbon button natively without `.cuix` plumbing.

Status: **early scaffold**. Available commands: `PSPING`, `GENSPEC`,
`LISTPARTS`, `SPECUI`, `WRITESPEC`.

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
4. Type `PSPING` → a message box should pop confirming the plug-in is alive.
5. Type `GENSPEC` → the editor should print the active Plant 3D project's
   name, folder, `.xml` file, and the current drawing path. If no Plant 3D
   project is open it prints a `No Plant 3D project is currently open`
   notice — that's expected on a plain AutoCAD drawing.
6. Type `LISTPARTS` → the editor should print the total row count for the
   active drawing followed by a breakdown by category (top-level class)
   and the top-15 full class names. Empty/non-Plant 3D drawings get an
   explanatory notice instead.
7. Type `SPECUI` → a modal WPF window opens with the same data as
   `LISTPARTS` shown in two trees. Close it with the **Close** button or
   <kbd>Esc</kbd>. This is a smoke test of WPF in-process under AutoCAD,
   not the final UI.
8. Type `WRITESPEC` → a save-as dialog opens; pick a `.docx` path. The
   resulting file has a heading and a (category, count) table. Settings
   live at `%APPDATA%\PipingSpecPlugin\settings.json` — the same path
   used by the Python edition, so picking output dirs / templates
   carries over between the two plug-ins.
9. After loading the DLL, AutoCAD's ribbon should show a new
   **Spec Generator** tab with **Generate / Preview / Project info**
   buttons that invoke `WRITESPEC` / `SPECUI` / `GENSPEC` respectively.
   Registered programmatically — no `.cuix` plumbing required.

> **Rebuild while AutoCAD is open?** AutoCAD locks `PipingSpecPlugin.dll`
> for the rest of the session after `NETLOAD`, so a second `dotnet build`
> into `bin\Release\` will fail with MSB3026. Either close AutoCAD before
> rebuilding, or pass `-p:OutDir=bin/Release-vNEXT/` and `NETLOAD` the
> versioned copy. AutoCAD doesn't notice you replaced the file — you
> *must* restart it to reload an updated DLL.

## Roadmap

See [IMPROVEMENTS](https://github.com/IvanGelenko/specgen/blob/main/IMPROVEMENTS.md)
in the Python repo for the feature backlog being ported here.
