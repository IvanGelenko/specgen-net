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

[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]

// Tells AutoCAD which assembly to scan for [CommandMethod] attributes.
[assembly: CommandClass(typeof(PipingSpecPlugin.EntryPoint))]
