# PipingSpecPlugin (.NET)

Native AutoCAD Plant 3D plug-in (C# / .NET 8) that generates a piping
specification from the active project.

This is a full rewrite of the Python-based
[specgen](https://github.com/IvanGelenko/specgen) tool. The Python edition
talks to `Piping.dcf` directly via SQLite; this edition talks to the live
Plant 3D in-memory model through the `PnP*Mgd` APIs, so it sees unsaved
changes and can register a ribbon button natively without `.cuix` plumbing.

Status: **early scaffold**. Available commands: `PSPING`, `GENSPEC`.

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

## Roadmap

See [IMPROVEMENTS](https://github.com/IvanGelenko/specgen/blob/main/IMPROVEMENTS.md)
in the Python repo for the feature backlog being ported here.
