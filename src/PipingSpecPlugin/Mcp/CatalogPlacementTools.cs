using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.ProcessPower.PartsRepository;
using Autodesk.ProcessPower.PnP3dObjects;

namespace Plant3dMcp
{
    static partial class PlantTools
    {
        static string CatalogString(JsonElement args, string name) =>
            args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;

        static string RequiredCatalogString(JsonElement args, string name) =>
            CatalogString(args, name) is { Length: > 0 } value ? value :
                throw new ArgumentException(name + " is required");

        static double CatalogPositiveNumber(JsonElement args, string name)
        {
            double number = Tools.Required(args, name, JsonValueKind.Number).GetDouble();
            if (!double.IsFinite(number) || number <= 0)
                throw new ArgumentException(name + " must be finite and positive");
            return number;
        }

        static double[] CatalogCoordinates(JsonElement args, string name)
        {
            var coordinates = Tools.Required(args, name, JsonValueKind.Array).EnumerateArray().Select(value => value.GetDouble()).ToArray();
            if (coordinates.Length != 3 || coordinates.Any(value => !double.IsFinite(value)))
                throw new ArgumentException(name + " must contain three finite coordinates");
            return coordinates;
        }

        public static object ListSpecParts(JsonElement args)
        {
            var spec = RequiredCatalogString(args, "spec");
            var manager = SpecManager.GetSpecManager();
            string key = LoadSpecKey(manager, Piping(), spec);
            string kind = CatalogString(args, "part_class");
            if (string.IsNullOrEmpty(kind))
            {
                var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string lookup in new[] { spec, key }.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    try { types.UnionWith(manager.GetAvailablePartTypes(lookup) ?? Array.Empty<string>()); }
                    catch (Autodesk.AutoCAD.Runtime.Exception) when (lookup != key) { }
                }
                return new { spec, classes = types.OrderBy(type => type).ToArray() };
            }

            double dn = CatalogPositiveNumber(args, "dn");
            string contains = CatalogString(args, "search");
            var parts = CollectParts(manager, spec, key, kind, dn)
                .Where(candidate => string.IsNullOrEmpty(contains) ||
                    (candidate.desc ?? "").Contains(contains, StringComparison.OrdinalIgnoreCase))
                .Select(candidate => new
                {
                    record_id = SpecRecordOf(candidate.part),
                    description = candidate.desc,
                    wall = candidate.wall,
                    part_class = candidate.part.Type
                }).Distinct().ToArray();
            int offset = Tools.OptionalInt(args, "offset") ?? 0;
            if (offset < 0) throw new ArgumentOutOfRangeException("offset");
            return new { spec, part_class = kind, dn, total = parts.Length, offset, items = parts.Skip(offset).Take(100).ToArray() };
        }

        public static object InsertSpecPart(JsonElement args)
        {
            var doc = ActiveDocument();
            string drawing = RequiredCatalogString(args, "drawing");
            if (!string.Equals(drawing, doc.Name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The active drawing changed; read document_info again.");
            if (doc.Database.Insunits != UnitsValue.Millimeters)
                throw new InvalidOperationException("Only millimetre drawings are supported.");

            string spec = RequiredCatalogString(args, "spec");
            string kind = RequiredCatalogString(args, "part_class");
            double dn = CatalogPositiveNumber(args, "dn");
            int? record = Tools.OptionalInt(args, "record_id");
            string description = CatalogString(args, "description");
            if (record is null && string.IsNullOrEmpty(description))
                throw new ArgumentException("Specify a record_id or an exact description from list_spec_parts.");
            if (record is <= 0 or int.MaxValue)
                throw new ArgumentException("record_id is unavailable; select by exact description instead.");

            var piping = Piping();
            var manager = SpecManager.GetSpecManager();
            string key = LoadSpecKey(manager, piping, spec);
            var matches = CollectParts(manager, spec, key, kind, dn)
                .Where(candidate => (!record.HasValue || SpecRecordOf(candidate.part) == record.Value) &&
                    (string.IsNullOrEmpty(description) || string.Equals(candidate.desc, description, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"Catalog selection matched {matches.Length} parts. Use list_spec_parts and select exactly one.");

            var selected = matches[0];
            string partType = selected.part.Type ?? kind;
            bool Is(string type) => partType.Equals(type, StringComparison.OrdinalIgnoreCase) || kind.Equals(type, StringComparison.OrdinalIgnoreCase);
            if (Is("Gasket") || Is("BoltSet") || Is("Weld"))
                throw new ArgumentException("This catalog item belongs to a connector. Use create_joint for joint components.");

            if (CatalogString(args, "pipe_handle") is { Length: > 0 } pipeHandle)
            {
                if (!Is("Valve")) throw new ArgumentException("Inline insertion currently supports flanged valves.");
                return InsertFlangedValveOnPipe(args, pipeHandle, doc, piping, manager, spec, key, dn, selected.part, selected.desc);
            }

            var xyz = CatalogCoordinates(args, "position");
            var position = new Point3d(xyz[0], xyz[1], xyz[2]);
            Vector3d ReadAxis(string name, Vector3d fallback)
            {
                if (!args.TryGetProperty(name, out _)) return fallback;
                var values = CatalogCoordinates(args, name);
                var result = new Vector3d(values[0], values[1], values[2]);
                if (result.Length < 1e-8) throw new ArgumentException(name + " must be nonzero");
                return result.GetNormal();
            }
            var direction = ReadAxis("axis", Vector3d.XAxis);
            var up = ReadAxis("up", Math.Abs(direction.Z) < .9 ? Vector3d.ZAxis : Vector3d.YAxis);
            up -= direction * up.DotProduct(direction);
            if (up.Length < 1e-8) throw new ArgumentException("axis and up must not be parallel");
            up = up.GetNormal();
            bool pipe = Is("Pipe");
            bool support = Is("Support") || Is("PipeSupport");
            double length = pipe ? CatalogPositiveNumber(args, "length") : 0;
            double od = pipe ? OuterDiameter(selected.part) : 0;
            if (pipe && (!double.IsFinite(od) || od <= 0))
                throw new InvalidOperationException("The catalog pipe has no usable outside diameter.");

            var plan = new { drawing, spec, part_class = partType, dn, description = selected.desc,
                record_id = SpecRecordOf(selected.part), position = Arr(position), axis = Arr(direction), up = Arr(up), length };
            if (!args.TryGetProperty("dry_run", out var dryRun) || dryRun.GetBoolean())
                return new { dry_run = true, plan };

            var dlm = piping.DataLinksManager;
            var before = ModelRows(doc, dlm);
            var added = new HashSet<int>();
            try
            {
                using (doc.LockDocument())
                using (var adder = new PipingObjectAdder(piping.DataLinksManager3d, doc.Database))
                {
                    int row;
                    if (pipe)
                        row = AddPipe(adder, selected.part, position, position + direction * length, od);
                    else if (support)
                    {
                        var asset = new Support();
                        asset.SymbolId = ContentManager.GetContentManager().GetSymbol(selected.part, doc.Database);
                        asset.Position = position;
                        row = adder.Add(selected.part, asset);
                    }
                    else
                    {
                        var asset = new PipeInlineAsset();
                        asset.SymbolId = ContentManager.GetContentManager().GetSymbol(selected.part, doc.Database);
                        asset.Position = position;
                        if (!asset.SetOrientation(direction, up)) throw new InvalidOperationException("Plant rejected part orientation.");
                        row = adder.Add(selected.part, asset);
                    }
                    if (row <= 0) throw new InvalidOperationException("Plant did not return a row for the inserted part.");
                    added.Add(row);
                    var id = FindByRow(doc, dlm, row);
                    if (support && (direction != Vector3d.XAxis || up != Vector3d.ZAxis))
                    {
                        using var transaction = doc.Database.TransactionManager.StartTransaction();
                        var placedSupport = (Support)transaction.GetObject(id, OpenMode.ForWrite);
                        var across = up.CrossProduct(direction).GetNormal();
                        placedSupport.TransformBy(Matrix3d.AlignCoordinateSystem(position, Vector3d.XAxis, Vector3d.YAxis,
                            Vector3d.ZAxis, position, direction, across, up));
                        transaction.Commit();
                    }
                    return new { dry_run = false, handle = id.Handle.ToString(), row_id = row, plan,
                        ports = PortsOf(doc, id).Select(port => new { port.name, position = Arr(port.pos), direction = Arr(port.dir) }).ToArray() };
                }
            }
            catch (System.Exception failure)
            {
                string cleanup = RollbackSince(doc, dlm, added, before);
                throw new InvalidOperationException("Catalog insertion failed. " + failure.Message + " Rollback: " + cleanup, failure);
            }
        }
    }
}
