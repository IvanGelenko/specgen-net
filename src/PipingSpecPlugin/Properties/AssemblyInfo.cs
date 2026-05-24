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

[assembly: AssemblyVersion("0.7.0.0")]
[assembly: AssemblyFileVersion("0.7.0.0")]

// Tells AutoCAD which classes carry [CommandMethod] attributes. Each class
// hosting commands must be listed explicitly — without this, AutoCAD ignores
// the methods even though they compile fine.
[assembly: CommandClass(typeof(PipingSpecPlugin.EntryPoint))]
[assembly: CommandClass(typeof(PipingSpecPlugin.Commands.GenSpecCommand))]
[assembly: CommandClass(typeof(PipingSpecPlugin.Commands.ListPartsCommand))]
[assembly: CommandClass(typeof(PipingSpecPlugin.Commands.SpecUiCommand))]
[assembly: CommandClass(typeof(PipingSpecPlugin.Commands.WriteSpecCommand))]
[assembly: CommandClass(typeof(PipingSpecPlugin.Commands.ListProjectCommand))]
[assembly: ExtensionApplication(typeof(PipingSpecPlugin.PluginBootstrap))]
