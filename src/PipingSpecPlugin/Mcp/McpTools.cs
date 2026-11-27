using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;

namespace Plant3dMcp
{
    // The MCP tools (Claude <-> Plant 3D), embedded in the plug-in. The server that carries the calls is PipingSpecPlugin.Mcp.McpServer.
    static class Tools
    {
        static Document ActiveDoc()
        {
            return AcApp.DocumentManager.MdiActiveDocument ?? throw new InvalidOperationException("No active document");
        }

        public static int Limit(JsonElement args, int def, int max = 1000)
        {
            return Math.Clamp(OptionalInt(args, "limit") ?? def, 0, max);
        }

        internal static JsonElement Required(JsonElement args, string name, JsonValueKind kind)
        {
            if (!args.TryGetProperty(name, out var value))
                throw new ArgumentException("Missing required argument '" + name + "'.");
            if (value.ValueKind != kind)
                throw new ArgumentException("Argument '" + name + "' must be of type " + kind + ".");
            return value;
        }

        internal static int? OptionalInt(JsonElement args, string name)
        {
            if (!args.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)) return number;
            throw new ArgumentException("Argument '" + name + "' must be an integer.");
        }

        internal static double? OptionalNumber(JsonElement args, string name)
        {
            if (!args.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && double.IsFinite(number)) return number;
            throw new ArgumentException("Argument '" + name + "' must be a finite number.");
        }

        public static object Run(string tool, JsonElement args)
        {
            switch (tool)
            {
                case "ping":
                    return new { acad = AcApp.Version.ToString(), docs = AcApp.DocumentManager.Count };
                case "document_info":
                {
                    var d = ActiveDoc();
                    return new { name = d.Name, path = d.Database.Filename };
                }
                case "list_entities":
                    return ListEntities(args);
                case "plant_project_info":
                    return PlantTools.ProjectInfo();
                case "list_plant_parts":
                    return PlantTools.ListParts(args);
                case "capture_iso_snapshot":
                    return CaptureIsoSnapshot(args);
                case "get_part_properties":
                    return PlantTools.PartProperties(args);
                case "create_pipe":
                    return PlantTools.CreatePipe(args);
                case "create_pipeline":
                    return PlantTools.CreatePipeline(args);
                case "create_joint":
                    return PlantTools.CreateJoint(args);
                case "repair_connections":
                    return PlantTools.RepairConnections(args);
                case "create_tracing_route":
                    return PipingSpecPlugin.Tracing.TracingEngine.CreateTracingRoute(args);
                case "create_tracing":
                    return PipingSpecPlugin.Tracing.TracingEngine.CreateTracing(args);
                case "probe_connector_wrapper":
                    if (Environment.GetEnvironmentVariable("PLANT3D_MCP_ENABLE_DEV_TOOLS") != "1")
                        throw new InvalidOperationException("probe_connector_wrapper is a disabled development tool.");
                    return PlantTools.ProbeConnectorWrapper(args);
                case "restore_branch_link":
                    return PlantTools.RestoreBranchLink(args);
                case "set_layer":
                    bool dry = !args.TryGetProperty("dry_run", out var dryRun) || dryRun.GetBoolean();
                    return PipingSpecPlugin.Tracing.TracingEngine.SetLayer(args, dry);
                case "port_conditions":
                    return PlantTools.PortConditions(args);
                case "probe_bend":
                    return PlantTools.ProbeBend(args);
                case "add_supports":
                    return PlantTools.AddSupports(args);
                case "create_valve_bypass":
                    return PlantTools.CreateValveBypass(args);
                case "create_bypass":
                    return PlantTools.CreateBypass(args);
                case "get_connections":
                    return PlantTools.GetConnections(args);
                case "probe_part_type":
                    return PlantTools.ProbePartType(args);
                case "inspect_connector":
                    return PlantTools.InspectConnector(args);
                case "create_branch":
                    return PlantTools.CreateBranch(args);
                case "connect_parts":
                    return PlantTools.ConnectParts(args);
                case "get_ports":
                    return PlantTools.GetPorts(args);
                case "delete_objects":
                    return PlantTools.DeleteObjects(args);
                case "list_spec_parts":
                    return PlantTools.ListSpecParts(args);
                case "insert_spec_part":
                    return PlantTools.InsertSpecPart(args);
                case "list_specs":
                    return PlantTools.ListSpecs();
                case "send_command":
                    return SendCommand(args);
                default:
                    throw new ArgumentException("Unknown tool: " + tool);
            }
        }

        static object CaptureIsoSnapshot(JsonElement args)
        {
            string line = Tools.Required(args, "line", JsonValueKind.String).GetString()?.Trim();
            string drawing = Tools.Required(args, "drawing", JsonValueKind.String).GetString();
            if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(drawing))
                throw new ArgumentException("Specify the line tag and the drawing path returned by document_info.");

            var document = ActiveDoc();
            if (!string.Equals(document.Database.Filename, drawing, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The active drawing changed. Check document_info before capturing.");
            using var documentLock = document.LockDocument();
            var project = Autodesk.ProcessPower.PlantInstance.PlantApplication.CurrentProject;
            var piping = project?.ProjectParts["Piping"] as Autodesk.ProcessPower.P3dProjectParts.PipingProject;
            var drawingLines = PipingSpecPlugin.Commands.AuditLineCatalog.ReadLiveIsometric(document.Database, piping?.DataLinksManager).DrawingLines();
            var handles = drawingLines.Where(p => string.Equals(p.Value, line, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToArray();
            var snapshot = IsoGenerator.AutoCAD.PlantSnapshotReader.Capture(
                document.Database, document.Database.Filename, ObjectId.Null, line, handles, drawingLines: drawingLines);
            if (snapshot.Parts.Count == 0)
                throw new InvalidOperationException("The requested line is not present in the active drawing.");
            return snapshot;
        }

        // Permit only the non-interactive commands listed here, without arguments.
        static object SendCommand(JsonElement args)
        {
            if (Environment.GetEnvironmentVariable("PLANT3D_MCP_ALLOW_COMMANDS") != "1")
                throw new InvalidOperationException("send_command is disabled. Set PLANT3D_MCP_ALLOW_COMMANDS=1 before starting Plant 3D to enable it.");
            var d = ActiveDoc();
            string cmd = Tools.Required(args, "command", JsonValueKind.String).GetString().ToUpperInvariant();
            if (cmd is not ("REGEN" or "REGENALL" or "REDRAW" or "REDRAWALL"))
                throw new ArgumentException("command must be REGEN, REGENALL, REDRAW or REDRAWALL, without arguments.");
            d.Editor.WriteMessage("\nPlant3dMcp: command " + cmd + "\n");
            d.SendStringToExecute("_." + cmd + "\n", true, false, false);
            return new { queued = cmd };
        }

        static object ListEntities(JsonElement args)
        {
            var d = ActiveDoc();
            bool obj = args.ValueKind == JsonValueKind.Object;
            int limit = Limit(args, 100);
            string filter = obj && args.TryGetProperty("class", out var c) ? c.GetString() : null;
            var counts = new Dictionary<string, int>();
            var items = new List<object>();
            using (var tr = d.Database.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(d.Database), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    string cls = id.ObjectClass.Name;
                    counts[cls] = counts.TryGetValue(cls, out var n) ? n + 1 : 1;
                    if (items.Count < limit && (filter == null || cls == filter))
                    {
                        var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                        items.Add(new { handle = id.Handle.ToString(), cls, layer = e.Layer });
                    }
                }
                tr.Commit();
            }
            return new { counts, items };
        }
    }
}
