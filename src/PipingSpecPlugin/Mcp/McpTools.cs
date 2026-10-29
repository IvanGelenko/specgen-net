using System;
using System.Collections.Generic;
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
            int v = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("limit", out var l) ? l.GetInt32() : def;
            return Math.Clamp(v, 0, max);
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
                    return PlantTools.ProbeConnectorWrapper(args);
                case "restore_branch_link":
                    return PlantTools.RestoreBranchLink(args);
                case "set_layer":
                    return PipingSpecPlugin.Tracing.TracingEngine.SetLayer(args);
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
                case "list_specs":
                    return PlantTools.ListSpecs();
                case "send_command":
                    return SendCommand(args);
                default:
                    throw new ArgumentException("Unknown tool: " + tool);
            }
        }

        // Arbitrary command/LISP execution is off unless the user opts in via environment variable;
        // even then LISP expressions, comments and multi-line input are refused.
        static object SendCommand(JsonElement args)
        {
            if (Environment.GetEnvironmentVariable("PLANT3D_MCP_ALLOW_COMMANDS") != "1")
                throw new InvalidOperationException("send_command is disabled. Set PLANT3D_MCP_ALLOW_COMMANDS=1 before starting Plant 3D to enable it.");
            var d = ActiveDoc();
            string cmd = args.GetProperty("command").GetString()?.Trim() ?? "";
            if (cmd.Length == 0 || cmd.Length > 200 || cmd.IndexOfAny(new[] { '(', ';', '\n', '\r' }) >= 0)
                throw new ArgumentException("command must be a single plain command (no LISP, no newlines)");
            d.Editor.WriteMessage("\nPlant3dMcp: command " + cmd + "\n");
            d.SendStringToExecute(cmd + "\n", true, false, false);
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
