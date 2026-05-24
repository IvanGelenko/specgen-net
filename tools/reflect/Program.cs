using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

// One-off introspection tool: dump the public surface of a managed DLL using
// MetadataLoadContext so the assembly is read for metadata only (no execution,
// no dependency resolution). Required for the Plant 3D managed wrappers,
// which transitively reference unmanaged ARX bits that won't bind outside
// the AutoCAD process.
class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: reflect <dll path> [name filter]");
            return 1;
        }
        var path = args[0];
        var filter = args.Length > 1 ? args[1].ToLowerInvariant() : null;

        var probeDirs = new[]
        {
            Path.GetDirectoryName(path),
            @"C:\Program Files\Autodesk\AutoCAD 2026",
            @"C:\Program Files\Autodesk\AutoCAD 2026\PLNT3D",
            RuntimeEnvironment(),
        };
        var paths = new List<string>();
        foreach (var d in probeDirs)
            if (!string.IsNullOrEmpty(d) && Directory.Exists(d))
                paths.AddRange(Directory.GetFiles(d, "*.dll"));

        var resolver = new PathAssemblyResolver(paths.Distinct());
        using var mlc = new MetadataLoadContext(resolver);
        var asm = mlc.LoadFromAssemblyPath(path);

        Console.WriteLine($"# {Path.GetFileName(path)}  ({asm.FullName})");

        if (filter == "refs")
        {
            foreach (var r in asm.GetReferencedAssemblies().OrderBy(r => r.Name))
                Console.WriteLine($"   REF {r.FullName}");
            return 0;
        }

        foreach (var t in asm.GetTypes()
                              .Where(t => t.FullName != null
                                          && (filter == null
                                              || t.FullName.ToLowerInvariant().Contains(filter)))
                              .OrderBy(t => t.FullName))
        {
            Console.WriteLine();
            Console.WriteLine($"## {t.FullName}");
            foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                .OrderBy(m => m.Name))
            {
                string mods = "";
                if (m is MethodBase mb) mods = mb.IsStatic ? " STATIC" : "";
                else if (m is PropertyInfo pi) mods = (pi.GetMethod ?? pi.SetMethod)?.IsStatic == true ? " STATIC" : "";
                else if (m is FieldInfo fi) mods = fi.IsStatic ? " STATIC" : "";
                Console.WriteLine($"   - [{m.MemberType}]{mods} {m}");
            }
        }
        return 0;
    }

    static string RuntimeEnvironment()
        => Path.GetDirectoryName(typeof(object).Assembly.Location);
}
