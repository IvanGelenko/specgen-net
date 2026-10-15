using System;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using PipingSpecPlugin.Audit;

[assembly: CommandClass(typeof(AuditSmoke.Commands))]
namespace AuditSmoke
{
    public partial class Commands
    {
        [CommandMethod("AUDITCATALOG")]
        public void Catalog()
        {
            var destination = Environment.GetEnvironmentVariable("AUDIT_SMOKE_OUTPUT");
            var before = Convert.ToInt32(Application.GetSystemVariable("DBMOD"));
            try
            {
                var assembly = System.Reflection.Assembly.LoadFrom(Environment.GetEnvironmentVariable("AUDIT_PLUGIN_PATH"));
                var type = assembly.GetType("PipingSpecPlugin.Commands.AuditLineCatalog", true);
                var catalog = type.GetMethod("Read").Invoke(null, new object[] { Application.DocumentManager.MdiActiveDocument, (Action<string>)(s => { }) });
                File.WriteAllText(destination, System.Text.Json.JsonSerializer.Serialize(catalog, type));
                var optionType = assembly.GetType("PipingSpecPlugin.Audit.AuditOptions", true);
                var options = Activator.CreateInstance(optionType);
                var enabled = (System.Collections.Generic.HashSet<string>)optionType.GetProperty("Enabled").GetValue(options);
                enabled.UnionWith(new[] { "wall", "connections", "collisions" });
                // AUDIT_SMOKE_LINE: one line as PSAUDIT would select it; empty = all lines.
                var line = Environment.GetEnvironmentVariable("AUDIT_SMOKE_LINE");
                optionType.GetProperty("SelectedLines").SetValue(options, string.IsNullOrEmpty(line) ? null : new System.Collections.Generic.HashSet<string> { line });
                var reader = assembly.GetType("PipingSpecPlugin.Audit.DrawingAuditReader", true);
                var snapshot = reader.GetMethod("Read").Invoke(null, new object[] { Application.DocumentManager.MdiActiveDocument, options, null, type.GetProperty("Parts").GetValue(catalog) });
                var issues = assembly.GetType("PipingSpecPlugin.Audit.AuditRules", true).GetMethod("Run").Invoke(null, new[] { snapshot, options });
                var json = (string)assembly.GetType("PipingSpecPlugin.Audit.AuditReport", true).GetMethod("Serialize").Invoke(null, new[] { snapshot, options, issues });
                File.WriteAllText(destination + ".audit.json", json);
                File.WriteAllText(destination + ".state.json", System.Text.Json.JsonSerializer.Serialize(new { Before = before, After = Convert.ToInt32(Application.GetSystemVariable("DBMOD")) }));
            }
            catch (System.Exception ex) { File.WriteAllText(destination + ".error.txt", ex.ToString()); }
        }

        [CommandMethod("AUDITGEOMETRY")]
        public void Geometry()
        {
            var rows = new System.Collections.Generic.List<string>();
            var seen = new System.Collections.Generic.HashSet<string>();
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var space = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            void Probe(Entity entity, string prefix, int depth)
            {
                rows.Add(prefix + entity.GetType().Name);
                if (depth > 3 || entity is Solid3d || entity is Curve) return;
                using var objects = new DBObjectCollection();
                try { entity.Explode(objects); foreach (DBObject obj in objects) if (obj is Entity child) Probe(child, prefix + "  ", depth + 1); }
                catch (System.Exception ex) { rows.Add(prefix + "ERROR " + ex.Message); }
                finally { foreach (DBObject obj in objects) obj.Dispose(); }
            }
            foreach (ObjectId id in space)
            {
                var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (entity != null && seen.Add(entity.GetType().Name)) Probe(entity, "", 0);
            }
            File.WriteAllLines(Environment.GetEnvironmentVariable("AUDIT_SMOKE_OUTPUT"), rows);
        }

        [CommandMethod("AUDITSMOKE")]
        public void Run()
        {
            var destination = Environment.GetEnvironmentVariable("AUDIT_SMOKE_OUTPUT");
            try
            {
                var options = new AuditOptions();
                foreach (var rule in AuditRules.All) options.Enabled.Add(rule.Id);
                var line = Environment.GetEnvironmentVariable("AUDIT_SMOKE_LINE");
                if (!string.IsNullOrEmpty(line)) options.SelectedLines = new System.Collections.Generic.HashSet<string> { line };
                var snapshot = DrawingAuditReader.Read(Application.DocumentManager.MdiActiveDocument, options);
                File.WriteAllText(destination, AuditReport.Serialize(snapshot, options, AuditRules.Run(snapshot, options)));
                File.WriteAllText(destination + ".parts.json", System.Text.Json.JsonSerializer.Serialize(snapshot.Parts));
            }
            catch (System.Exception ex) { File.WriteAllText(destination + ".error.txt", ex.ToString()); }
        }

        [CommandMethod("AUDITBOXES")]
        public void Boxes()
        {
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var space = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var keys = new System.Collections.Generic.List<string>();
            foreach (var x in new[] { 0d, 5d, 15d, 50d })
            {
                var solid = new Solid3d(); solid.CreateBox(10, 10, 10);
                solid.TransformBy(Matrix3d.Displacement(new Vector3d(x, 0, 0)));
                space.AppendEntity(solid); tr.AddNewlyCreatedDBObject(solid, true);
                keys.Add(solid.Handle.ToString());
            }
            tr.Commit();
            Run();
            File.WriteAllText(Environment.GetEnvironmentVariable("AUDIT_SMOKE_OUTPUT") + ".boxes.json", System.Text.Json.JsonSerializer.Serialize(keys));
        }
    }
}
