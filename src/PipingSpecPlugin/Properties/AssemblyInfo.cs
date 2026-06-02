using System.Reflection;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.Runtime;

[assembly: AssemblyTitle("PipingSpecPlugin")]
[assembly: AssemblyDescription("Plant 3D piping specification generator")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("In-house")]
[assembly: AssemblyProduct("PipingSpecPlugin")]
[assembly: AssemblyCopyright("")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]
[assembly: Guid("B5E20C42-5C4D-4F1B-9B7C-1D5E2B0A8C02")]

// AssemblyVersion / AssemblyFileVersion are NOT declared here. With
// GenerateAssemblyInfo=false we'd normally have to hard-code them, but a
// hard-coded version overrides the -p:Version passed by build.cmd / CI,
// which silently shipped the wrong version (and broke the in-plugin update
// check that reads AssemblyVersion). The csproj generates these two
// attributes from $(Version) instead — see the GenerateVersionInfo target.

// Tells AutoCAD which classes carry [CommandMethod] attributes. The
// ribbon's "Сгенерировать" button invokes WRITESPEC; everything else
// (early diagnostic commands like PSPING / LISTPARTS / GENSPEC) was
// removed for the production cut.
[assembly: CommandClass(typeof(PipingSpecPlugin.Commands.WriteSpecCommand))]
[assembly: CommandClass(typeof(PipingSpecPlugin.Commands.MoveToLayerCommand))]
[assembly: CommandClass(typeof(PipingSpecPlugin.Commands.ConnectAdjacentCommand))]
[assembly: CommandClass(typeof(PipingSpecPlugin.Commands.AboutCommand))]
[assembly: ExtensionApplication(typeof(PipingSpecPlugin.PluginBootstrap))]
