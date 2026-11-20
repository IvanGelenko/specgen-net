---
name: plant3d-mcp
description: Work with AutoCAD Plant 3D 2026 through the `plant3d` MCP server (project info, Plant objects in the active drawing, specs, .pcat/.pspc catalogs, creating pipes and pipelines with elbows, connecting and deleting parts). Use for any request about the open Plant 3D drawing/project, piping parts, specs or catalogs, and when extending the MCP tools, the heat tracing generator or the bridge in C:\specgen.
---

# Plant 3D via MCP

Tools are `mcp__plant3d__*`. Load their schemas with ToolSearch (`select:mcp__plant3d__ping,...`) before the first call.

## Architecture
- The MCP server is part of the plug-in PipingSpecPlugin (C:\specgen\src\PipingSpecPlugin): `Mcp\McpServer.cs` (HTTP on `127.0.0.1:18765`, per-session token, calls run on the AutoCAD main thread; OFF by default: ribbon button «Codex → Сервер MCP», command `PSMCP`, autostart file `%LOCALAPPDATA%\Plant3dMcp\autostart`), `Mcp\McpTools.cs` (dispatch `Tools.Run`), `Mcp\PlantTools.cs`, `PipelineTools.cs`, `BranchTools.cs`, `BypassTools.cs`, `ValveBypassTools.cs`, `SupportTools.cs`, `BendTools.cs`, `ConnectorTools.cs`. The heat tracing generator is `Tracing\` (`TracingRoute.cs` planner, `TracingCommit.cs` creation, `TracingSupport.cs`, command `PSTRACING`); `create_tracing_route`, `create_tracing` and `set_layer` are routed there.
- `mcp\bridge\server.mjs`: dependency-free MCP stdio server, registered in `C:\specgen\.mcp.json`. Forwards to the plug-in; `list_catalog_parts` is handled in the bridge itself (node:sqlite), so it works without Plant 3D. `mcp\tools\call.ps1 -Tool <name> -ArgsFile <json>` calls the endpoint directly with the session token.
- Norms and the user's rules/samples: `C:\specgen\docs\tracing\` (RULES-GOST-32569.md, gost32569_table_E1.json, reference\*.json|txt, USER-RULES-TRACING.md), the norm PDF in `docs\norms\`.

## Tools
| Tool | Kind | Purpose |
|---|---|---|
| `ping`, `document_info` | read | connection check, active drawing |
| `plant_project_info`, `list_specs` | read | project name/dir/units/specs/drawings |
| `list_plant_parts` | read | Plant objects of the active drawing; filters `line`, `limit` |
| `get_part_properties` | read | all Plant properties by DWG handle (hex) |
| `get_ports` | read | ports (name, position, direction) of a part |
| `list_entities` | read | raw model-space entity counts by class |
| `list_catalog_parts` | read | read a .pcat/.pspc (SQLite); `path` or `catalog`; filters `search`, `class`, `category`, `size`, `material`, `limit` |
| `create_pipe` | write, two-phase | one straight spec pipe (`nd`, `length`, `start`, `direction`, `wall`, `spec`) |
| `create_pipeline` | write, two-phase | pipes + elbows along a polyline, all joints connected; rolls back on any failure |
| `create_branch` | write, two-phase | DN pipe welded straight into a run pipe (no tee), weld-neck flange and gate valve on it; written but NOT yet tested live |
| `create_bypass` | write, two-phase | NEW DN150 run with two reducing tees and a DN50 bypass loop with a flanged gate valve; connectors at every joint; written, not yet tested live |
| `create_valve_bypass` | write, two-phase | DN50 bypass around an EXISTING valve; modifies the run (save first); tested live on valve 515C |
| `create_joint`, `repair_connections` | write, two-phase | Buttweld connector between two coincident ports; re-link orphan connector ports (v28) |
| `add_supports` | write, two-phase | КП supports (OST 36-146-88) on pipes: next to valves on both sides, then step 3 m, edge >=100 mm from a weld axis; spec by pipe material (09Г2С / ст20->ВСт3пс); under each support plate+12П post+floor plate via Plant 3D commands PLANTSTEELPLATE/PLANTSTEELMEMBER in a background job (`job` to poll); sample in `reference/support-reference.json`; v29, written, NOT yet tested live |
| `get_connections`, `inspect_connector` | read | connections of a part / contents of a joint connector |
| `connect_parts` | write, two-phase | connect two parts at coinciding ports |
| `delete_objects` | write, two-phase | erase Plant objects by handle (max 50) |
| `send_command` | write, disabled | needs env `PLANT3D_MCP_ALLOW_COMMANDS=1` |

Write tools: the default call is a dry run (plan only, nothing changed); the user sees the plan, then call again with `dry_run=false`. A single explicit user request to create something with all parameters given counts as approval.

## Security model
- The plugin writes a random per-session token to `%LOCALAPPDATA%\Plant3dMcp\token` (only after the listener started, deleted only by the instance that wrote it); the bridge sends it as `Authorization: Bearer`. The plugin also rejects any `Origin` header, wrong `Host`, non-JSON content type, bodies > 1 MB (enforced while reading, also for chunked requests). A 403 means a stale token: restart the Codex session or `MCPSTOP`/`MCPSTART`.
- `send_command` is disabled unless Plant 3D was started with env `PLANT3D_MCP_ALLOW_COMMANDS=1` (single plain command, no LISP). Do not enable it to work around a missing tool: add a proper tool instead.
- `list_catalog_parts` accepts only local `.pcat`/`.pspc` files (no UNC, no other extensions).
- `delete_objects` refuses non-Plant objects, objects outside model space and duplicate handles; the dry run returns the drawing name, pass it back as `document` in the real call.
- Requests that time out waiting for AutoCAD are cancelled, never executed late.

## Design rules (ГОСТ 32569-2013) — apply them whenever pipes are created
The rules compiled from the standard (`C:\specgen\docs\tracing\RULES-GOST-32569.md`; source PDF `C:\specgen\docs\norms\4293771088.pdf`; spacing table `C:\specgen\docs\tracing\gost32569_table_E1.json`) are binding for every pipe, pipeline or branch you create. **Read RULES-GOST-32569.md before the first creation in a session.** Short form:
- Slope (cl. 10.1.4): >= 0.002 (2 mm/m) liquids and gas along the flow, 0.003 gas against the flow, 0.005 acids/alkalis. Ask for the medium if it matters; warn when the slope is lower.
- Vents at upper points, drains at lower points on every line (cl. 10.2.1); vent/drain DN not less than table 10.3 (DN25-80: 15; 100-150: 20; 175-300: 25; 350-450: 32; 500-700: 40; 800-1200: 50). Pockets from DN100 (table 10.1).
- Weld spacing (cl. 6.8.1): a straight pipe between butt welds is at least max(3*wall, 100 mm for OD<=219 / 250 for <=550 / 400 above). Enforced by `create_pipe`/`create_pipeline`; only `relax_weld_spacing=true` (justified, user-requested) lowers it.
- Branch welded into a pipe (cl. 6.3, 6.8.4): clearance from the branch surface to a weld/bend >= max(OD,50) for OD<100, >=100 for OD>=100. Enforced by `create_branch`.
- Elbows (cl. 6.4): steep elbows R<=1.5*DN by default; flanges type 11 (weld neck) unless PN<=25 and t<=300 °C (then type 01 is allowed); supports >=100 mm from welds; valve hand drive <=1.6 m above the floor; handwheel opens counter-clockwise.
- Spacing between adjacent pipes: table E.1 (B = b_i + b_j).
- User rule: the pipe of every vent and drain is 100 mm long. A vent/drain assembly is: Tap joint on the run surface, DN25 pipe 100 mm, Buttweld joint, weld-neck flange (weld end and hub toward the pipe), "Flanged FL1-FL1 (RUS)" connector (4.5 mm gasket + bolts), gate valve, second flanged connector, blind flange on the open port (no port may stay open). Joints are `AcPpDb3dConnector` objects (JointType Tap / Buttweld / Flanged FL1-FL1 (RUS)), not separate gasket parts. The reference assembly was built by the user in temp.dwg (handles 4EEF..4F14) and can be read with `inspect_connector`.
Every create tool returns `design_notes` (and the dry run shows them): quote them to the user, name the clause for each deviation, and never silently relax a rule. Deviations only on the user's explicit instruction.

## Rules
- Start with `ping`. If it fails: Plant 3D not running, the plug-in PipingSpecPlugin not loaded, or the server not switched on (ribbon button «Codex → Сервер MCP» / command `PSMCP`).
- Read tools are safe. Anything that changes the drawing (creating/editing/deleting) only on an explicit user request; say what changed and the handles of created objects. Nothing is saved implicitly: tell the user that unsaved work is lost when Plant 3D closes.
- Prefer small `limit` values first (a drawing can have 1000+ objects).
- Catalog text is in the catalog's language: DIN/ASME catalogs are English ("Gate valve"), the Russian ones (ОСТ, Rus-PipeAndFittings) are Russian. No match on the first term is not an error; call `list_catalog_parts` without `search`/`limit` for class/category counts.
- Shared content folder: `Z:\DIR_AUTOCAD_PLANT_3D\Монтажный отдел\CATALOGS PLANT 3D 2014\AutoCAD Plant 3D 2023 Content` (69 .pcat, override with env `PLANT3D_CATALOG_DIR`). Project specs: `<project>\Spec Sheets\*.pspx`; the spec `!_Rus-PipeAndFittings_09Г2С_PN16` lives in the shared content folder (`.pspx`; parts in the neighbouring `.pspc`).
- Default spec for new pipes: `!_Rus-PipeAndFittings_09Г2С_PN16` (project group "Мини-каталоги труб"), set by the user.
- Default pipe wall (user rule: table 5.6 s_min + 2 mm, rounded up) by outer diameter Da: <=25 -> 3, <=57 -> 4 (1.5+2=3.5), <=114 -> 4, <=219 -> 5 (2.5+2=4.5), <=325 -> 5, <=377 -> 6 (3.5+2=5.5), >426 -> 6. Table has no row for 377<Da<=426 (the >426 value is used). Applies only when the spec has several pipes of that DN and no explicit `wall`. Comparison is exact per the table (+0.05 mm float tolerance): 114.3 mm counts as >114.
- Wall rules (user): pipe and elbows of one run have the SAME wall and outer diameter; tees and reducers must have wall >= the wall of the pipe they connect (smallest such wall in the spec; for a part joining two pipes, >= the thicker one). The tee/reducer rule is documented but NOT yet implemented in code (no tee/reducer tools yet).
- Runs must be continuous: never leave bare pipes meeting at a corner. Use `create_pipeline`. Elbow tangent length = R*tan(angle/2) of the elbow's own angle (90 DN150 159x5: R=225), trimmed from both pipes; the geometric turn must match an elbow angle in the spec to 0.05 deg; vertex spacing must leave every pipe at least the project's minimum pipe length.
- Defaults assumed when the user gives none: start (0,0,0), direction +X; say so in the reply.
- Plant data units: project is metric; sizes are DN in mm; coordinates in mm.

## How creation works (learned the hard way)
- `PipingObjectAdder.Add` creates the object only; it does NOT connect parts. Connections are made with the native `PnP3dObjects.ConnectionManager.Connect(Pair, Pair)` (stored as `P3dPartConnection` relations with Port1/Port2).
- Inline assets (elbows) need `ContentManager.GetSymbol(part, db)` assigned to `SymbolId` before `Add`, otherwise the part is an empty record with no geometry or ports.
- Elbow placement is measured: place a trial elbow, read its port positions, compute the pose, re-place, then verify both port positions (0.5 mm) and directions (2 deg; port directions point away from the part toward the neighbour pipe). Rollback erases every data-link row added by the call.
- Joint connectors are created with `PipingObjectAdder.Add(jointName, PartSizePropertiesCollection subParts, Connector)`: the connector gets `Position`, `SetOrientation(x, z)` (local X runs from port S1 to S2; S1 faces the joint's first part), `SlopeTolerance` (0.1 deg = 0.001745 rad for Tap and Flanged, 3 deg for Buttweld), and sub-parts added with `AddSubPart` in the same order as the property sets. Flanged FL1-FL1 (RUS) = `BlockSubPart` (symbol from `ContentManager.GetSymbol(gasket)`) + `BoltSetSubPart` (Length 115, Offset 0) with the spec gasket (type Д, record 16616) and bolt set (13031); the two flanges are 4.5 mm apart (gasket thickness). Parts are joined THROUGH the connector (`ConnectCore(connector, part, portName)`); the Tap needs a dynamic port with end type "TAP" on the run, and the branch starts on the run SURFACE at sqrt(R^2 - r^2) from the axis.
- Pipe bends ("ИЗГИБ ТРУБЫ", PipeBend) are made with `PnP3dPipeRouting.BentPipe` (assembly PnP3dPipeUI): PartSizeProperties = the PIPE spec part (type "Pipe"), SymbolId, OuterDiameter, Radius, Angle (radians), L1, L2, Position = corner (axes intersection), SetOrientation, `Append(adder.LineGroupId)` (-1). Never pass 0 as the line group: Plant shows `PnPCompletionStatus.RowNotFound`. `probe_bend` (place, keep, radius, angle, l1, l2, line_group) verifies it.
- Supports: `PipingObjectAdder.Add(part, Support)`; connect to a pipe through a dynamic port Dn on the pipe at the axis point (`AddDynamicPort` + `ConnectCore`). Structure plates/members have no managed API (commands PLANTSTEELPLATE / PLANTSTEELMEMBER).
- Weld connectors: make them with `PnP3dPipeRouting.ConnectorWrapper` (`Spec`, `Connect(pairA, pairB, "Buttweld (RUS)")`, `Append(pairA, pairB, lineGroupId)`, parts opened ForWrite in one transaction): the connector gets the typed Buttweld element with a data row and PL on both ports. A hand-made `AddConnector` weld has an empty end condition on one port and makes Plant 3D show "no connection defined for types '' and PL" and erase connectors. `create_tracing` uses the wrapper; `create_joint` still uses AddConnector with a template copy (S1 port stays empty): prefer the wrapper. Never pump messages (DoEvents) inside a tool.
- Way of working: edit the sources in C:\specgen\src\PipingSpecPlugin, build with `dotnet build src\PipingSpecPlugin\PipingSpecPlugin.csproj -c Release`, run `dotnet test test\PipingSpecPlugin.Tests` (646 tests must pass), install the plug-in (build.cmd / installer; the DLL is locked while Plant 3D runs), restart Plant 3D, switch the MCP server on. There is no hot reload any more: every change of the code needs a restart of Plant 3D.
- Tracing colour: layer "Спутник" (red, ACI 1), objects "by layer" (ColorIndex 256). Move the parts with `set_layer` in batches of ~12-15 right after `create_tracing` (not inside it). The Conceptual visual style may still paint Plant objects by material; check in wireframe or in the object's properties ("ПоСлою").
- (superseded by the later rules in docs/tracing/USER-RULES-TRACING.md: no twists on risers, the riser is a straight vertical line with two 90-degree bends, the gap may be 1-3 mm but never an interference, unavoidable collisions are created and painted violet) HARD RULES (user, 2026-10-29): (1) on every horizontal run the tracers sit exactly as in view 3 (two tracers at +-45 deg below the axis, both sides); never leave them stacked/one above the other because the frame was carried through risers (create_tracing_route currently carries the frame and gets this wrong on routes with a riser between horizontals of different directions: needs a twist around the riser); (2) never forget the loops round vents and drains; (3) trace the WHOLE line (main route plus all branches, vents, drains, bypasses), not just the straight main run.
- PROTOCOL: never delete a whole tracing and recreate it in the same Plant 3D session (three crashes in a row, all in native code, right after `delete_objects` of the old tracing). Ask the user to restart Plant 3D WITHOUT saving, then run one `create_tracing`, then `set_layer`; the user checks and saves.
- Heavy creations (`create_tracing` with a bypass: 60+ parts) have crashed Plant 3D several times in long sessions (native heap errors in AcPp3dJoint.connect / PortsOf / AddPipe, see %LOCALAPPDATA%\\Autodesk\\CER\\...\\rawdata-t2.pb). Run them right after a restart, save the drawing first, and keep a copy.
- Tracing layout: supply tracer = all dips (vent/drain) and the bypass loop, the other tracer (return) runs straight and only goes round the flanges/valve of the main line. Tracer route round a bypass: rises beside the main pipe, moves to "under the bypass pipe at 45 deg", runs under the top run with detours, comes down, returns.
- `create_tracing` (v45+): straight main line, two DN20 tracers (view 3), detours around flanges/valves (angles 15/30/45/60), closing 180 bends beyond the ends, vents/drains heated by a dip (dip_radius 40), collision check, stage 1/2 dry rehearsals (`stage`, `keep`).
- KNOWN GAP (closed for tracing, open for older tools): the weld sub-parts (TapWeld, Buttweld) are created but have no data row (partId -1, no properties) because the Type of a bare `SpecPart` cannot be set (probe_part_type: no property name does it); the reference has rows with Type TapWeld/Buttweld, SpecRecordId -1. Likely needs a typed property set from the project parts repository (`DataLinksManager3d.ProjectPartsRepository`, `PartsRepository.SelectParts("Buttweld", ...)`), conversion to `PartSizeProperties` not yet found.
- Rollback must not trust its own list of added rows: Plant 3D may re-create a part (seen with a flange) under a NEW row number, leaving an orphan. `RollbackSince` erases the added rows and then every Plant row that did not exist before the call, and reports any LEFTOVER. After any failed write call, compare `list_plant_parts` totals with the count before it.
- Spec access: `SpecManager.LoadSpec` accepts the full path of the `.pspx` (from `PipingProject.GetSpecFileName`), not the bare name; `SelectParts` needs a type key and the right name form, so `CollectParts` tries several forms and unions all matching type keys.
- Spec pipe/elbow properties may be missing on the part: outer diameter and wall fall back to parsing `159x5` from the description (sanity-checked so steel grades like `08х18н10т` are not mistaken for sizes).
- `DataLinksManager.FindAcPpRowId` throws `ObjectDoesNotHaveLink` for objects without Plant data; only that exception is treated as "no data" (see `RowId`).
- The project `.dcf` files update only when the drawing is saved; never use them to verify unsaved work.
- A DLL loaded by Plant 3D is locked: close Plant 3D before installing a new build. The bridge (`server.mjs`) is reloaded by restarting the Codex session; its tool list is cached until then.
- Plant assemblies: `C:\Program Files\Autodesk\AutoCAD 2026\PLNT3D` and the AutoCAD root (mixed-mode; read signatures with a console project using `System.Reflection.MetadataLoadContext`, not `Assembly.LoadFrom`).
- Project access: `PlantApplication.CurrentProject.ProjectParts["Piping"]` -> `PipingProject`; `.DataLinksManager` for properties, `.DataLinksManager3d` for the adder.

## Extending
1. Add the case in `Tools.Run` (src\PipingSpecPlugin\Mcp\McpTools.cs) and the implementation (Mcp\PlantTools.cs / PipelineTools.cs, tracing in Tracing\); add the tool and its annotations to `mcp\bridge\server.mjs` (write tools: `readOnlyHint:false`, destructive ones `destructiveHint:true`; every new argument needs its place in the input schema: `additionalProperties:false`).
2. Build, test, install, restart Plant 3D, switch the server on, restart the Codex session.
3. Test each new tool on a scratch drawing first and report failures with the error text. Every write tool must be two-phase and must leave no objects behind on failure.
