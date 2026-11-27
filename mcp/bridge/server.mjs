// Minimal MCP stdio server (no dependencies) that forwards tool calls to the Plant 3D plugin.
import { DatabaseSync } from 'node:sqlite';
import { copyFileSync, readdirSync, readFileSync, mkdtempSync, realpathSync, rmSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, basename, extname, relative, isAbsolute, sep } from 'node:path';
import { createInterface } from 'node:readline';

const CATALOG_DIR = process.env.PLANT3D_CATALOG_DIR || 'Z:\\DIR_AUTOCAD_PLANT_3D\\Монтажный отдел\\CATALOGS PLANT 3D 2014\\AutoCAD Plant 3D 2023 Content';
const URL_BASE = process.env.PLANT3D_URL || 'http://127.0.0.1:18765/';
const MAX_CATALOG_BYTES = 512 * 1024 * 1024;

const TOOLS = [
  { name: 'ping', description: 'Check connection to Plant 3D; returns AutoCAD version', inputSchema: { type: 'object', properties: {} } },
  { name: 'document_info', description: 'Active drawing name and path', inputSchema: { type: 'object', properties: {} } },
  { name: 'list_entities', description: 'Count model-space entities by class and list first N (optionally filtered by class name)',
    inputSchema: { type: 'object', properties: { limit: { type: 'integer' }, class: { type: 'string' } } } },
  { name: 'plant_project_info', description: 'Current Plant 3D project: name, directory, units, spec names, drawing files', inputSchema: { type: 'object', properties: {} } },
  { name: 'list_plant_parts', description: 'Plant objects in the active drawing with Tag/Size/Spec/LineNumberTag; optional filter by line tag',
    inputSchema: { type: 'object', properties: { limit: { type: 'integer' }, line: { type: 'string' } } } },
  { name: 'capture_iso_snapshot', description: 'Read a complete isometric input snapshot of one active Plant 3D line, including native iso marker types, directions and connected context. Use the exact drawing path from document_info.',
    inputSchema: { type: 'object', properties: { drawing: { type: 'string', minLength: 1 }, line: { type: 'string', minLength: 1 } }, required: ['drawing', 'line'], additionalProperties: false } },
  { name: 'get_part_properties', description: 'All Plant data properties of an object by DWG handle (hex)',
    inputSchema: { type: 'object', properties: { handle: { type: 'string' } }, required: ['handle'] } },
  { name: 'list_specs', description: 'Names of piping specs in the current project', inputSchema: { type: 'object', properties: {} } },
  { name: 'list_spec_parts', description: 'List part classes in a project spec, or look up catalog records by class and DN. Optional case-insensitive search matches the size description.',
    inputSchema: { type: 'object', properties: { spec: { type: 'string' }, part_class: { type: 'string' }, dn: { type: 'number', exclusiveMinimum: 0 }, search: { type: 'string' }, offset: { type: 'integer', minimum: 0 } }, required: ['spec'], additionalProperties: false } },
  { name: 'insert_spec_part', description: 'Place one catalog part in the active Plant drawing at a model position. Use drawing = the name from document_info and list_spec_parts to select a unique record. Dry run by default. For a standalone part provide position (and length for a straight pipe). For a flanged valve insertion into a straight existing pipe provide pipe_handle and bolt_length, optionally fraction and gasket_type.',
    inputSchema: { type: 'object', properties: { drawing: { type: 'string' }, spec: { type: 'string' }, part_class: { type: 'string' }, dn: { type: 'number', exclusiveMinimum: 0 }, record_id: { type: 'integer' }, description: { type: 'string' },
      position: { type: 'array', items: { type: 'number' }, minItems: 3, maxItems: 3 }, axis: { type: 'array', items: { type: 'number' }, minItems: 3, maxItems: 3 }, up: { type: 'array', items: { type: 'number' }, minItems: 3, maxItems: 3 }, length: { type: 'number', exclusiveMinimum: 0 }, pipe_handle: { type: 'string' }, fraction: { type: 'number', exclusiveMinimum: 0, exclusiveMaximum: 1 }, bolt_length: { type: 'number', exclusiveMinimum: 0 }, gasket_type: { type: 'string' }, dry_run: { type: 'boolean' } },
      required: ['drawing', 'spec', 'part_class', 'dn'], allOf: [
        { anyOf: [{ required: ['record_id'] }, { required: ['description'] }] },
        { oneOf: [{ required: ['position'], not: { required: ['pipe_handle'] } }, { required: ['pipe_handle', 'bolt_length'] }] }
      ], additionalProperties: false } },
  { name: 'create_pipe', description: 'Creates a straight spec pipe in the active drawing. TWO-PHASE: by default (dry_run omitted/true) nothing is changed and the plan is returned; call again with dry_run=false to create. spec = project spec name (default !_Rus-PipeAndFittings_09Г2С_PN16), nd = nominal diameter in mm, length in mm, wall = wall thickness in mm (optional; if the spec has several pipes of that DN the default rule is used: s_min from table 5.6 by outer diameter + 2 mm, rounded up, e.g. 159 mm -> 5 mm), start [x,y,z] (default 0,0,0), direction [x,y,z] (default 1,0,0). Errors if the spec has no matching pipe or the match is ambiguous.',
    inputSchema: { type: 'object', properties: { spec: { type: 'string' }, nd: { type: 'number', exclusiveMinimum: 0 }, length: { type: 'number', exclusiveMinimum: 0 }, wall: { type: 'number' }, start: { type: 'array', items: { type: 'number' }, minItems: 3, maxItems: 3 }, direction: { type: 'array', items: { type: 'number' }, minItems: 3, maxItems: 3 }, relax_weld_spacing: { type: 'boolean' }, dry_run: { type: 'boolean' } }, required: ['nd', 'length'], additionalProperties: false } },
  { name: 'create_pipeline', description: 'Creates a continuous piping run along a polyline: straight pipes plus an elbow at every turn (angle taken from the geometry, elbow wall = pipe wall, elbow tangent length trimmed from both pipes). points = vertices [[x,y,z],...] in mm (2..50). TWO-PHASE: default is a dry run returning the plan; dry_run=false creates it. Pipes and elbows have the same wall and outer diameter; every joint is connected with the native connection manager. If anything fails, including an open joint, every object created by the call is erased. The result gives the connection count of each object (elbows and inner pipes 2, end pipes 1). spec default !_Rus-PipeAndFittings_09Г2С_PN16, wall default by the table-5.6 rule.',
    inputSchema: { type: 'object', properties: { points: { type: 'array', minItems: 2, maxItems: 50, items: { type: 'array', items: { type: 'number' }, minItems: 3, maxItems: 3 } }, nd: { type: 'number', exclusiveMinimum: 0 }, spec: { type: 'string' }, wall: { type: 'number' }, relax_weld_spacing: { type: 'boolean' }, dry_run: { type: 'boolean' } }, required: ['points', 'nd'], additionalProperties: false } },
  { name: 'create_branch', description: 'Creates a vent/drain on an existing run pipe, built like the user reference assembly: Tap joint on the run surface, DN pipe welded STRAIGHT into the run (no tee, GOST 32569 fig. 6.2a), Buttweld joint, weld-neck flange, "Flanged FL1-FL1 (RUS)" joint (gasket + bolt set), flanged gate (wedge) valve, a second flanged joint and a blind flange closing the open port. The branch axis is perpendicular to the run unless direction is given. TWO-PHASE: default is a dry run with the plan and design_notes; dry_run=false creates it, and everything is erased if any step fails. run_handle = the run pipe; distance_from_high_end (default 500 mm along the run from its higher end, at least the GOST 6.8.4 clearance from the ends); nd (default 25); stub_length (default 100 mm: the user rule for the pipe of vents and drains); variant of the valve and flange (default В/B); direction (default up, [0,0,1]).',
    inputSchema: { type: 'object', properties: { run_handle: { type: 'string' }, nd: { type: 'number', exclusiveMinimum: 0 }, distance_from_high_end: { type: 'number', exclusiveMinimum: 0 }, stub_length: { type: 'number', minimum: 50 }, relax_weld_spacing: { type: 'boolean' }, bolt_length: { type: 'number', exclusiveMinimum: 0 }, gasket_type: { type: 'string' }, variant: { type: 'string' }, wall: { type: 'number' }, spec: { type: 'string' }, direction: { type: 'array', items: { type: 'number' }, minItems: 3, maxItems: 3 }, dry_run: { type: 'boolean' } }, required: ['run_handle'], additionalProperties: false } },
  { name: 'connect_parts', description: 'Connects two Plant 3D parts at their coinciding ports (closest ports, must be within 1 mm) using the native connection manager. TWO-PHASE: default is a dry run reporting the ports and whether they can connect; dry_run=false connects.',
    inputSchema: { type: 'object', properties: { handleA: { type: 'string' }, handleB: { type: 'string' }, portA: { type: 'string' }, dry_run: { type: 'boolean' } }, required: ['handleA', 'handleB'], additionalProperties: false } },
  { name: 'create_bypass', description: 'Creates a NEW run (DN150 by default, 10 m, slope 2 mm/m) with two reducing tees and a DN50 bypass loop: reducer, pipe, 90-degree elbow, pipe, weld-neck flange, flanged gate valve, flange, pipe, elbow, pipe, reducer. Tee and reducer walls are never thinner than the connected pipes (user rule); every weld joint gets a Buttweld connector, both valve joints get "Flanged FL1-FL1 (RUS)" connectors with gasket and bolts. TWO-PHASE: default is a dry run with the plan and design_notes; dry_run=false creates it and erases everything if any step fails. tee1/tee2 = tee positions from the start (the high end) in mm (default 2000/8000), height = loop height above the run axis (default 600), start [x,y,z] (default [0,-11000,0]).',
    inputSchema: { type: 'object', properties: { nd: { type: 'number', exclusiveMinimum: 0 }, bypass_nd: { type: 'number', exclusiveMinimum: 0 }, length: { type: 'number', exclusiveMinimum: 0 }, slope: { type: 'number', minimum: 0 }, tee1: { type: 'number', exclusiveMinimum: 0 }, tee2: { type: 'number', exclusiveMinimum: 0 }, height: { type: 'number', exclusiveMinimum: 0 }, start: { type: 'array', items: { type: 'number' }, minItems: 3, maxItems: 3 }, variant: { type: 'string' }, gasket_type: { type: 'string' }, bolt_length: { type: 'number', exclusiveMinimum: 0 }, spec: { type: 'string' }, dry_run: { type: 'boolean' } }, additionalProperties: false } },
  { name: 'create_valve_bypass', description: 'Creates a DN50 bypass around an EXISTING valve of a run (flange - valve - flange with connectors, pipes on both sides): two reducing tees (wall >= pipe wall) are inserted into the run pipes beside the valve, with a loop of reducers, pipes, elbows, flanges and a flanged gate valve over it. MODIFIES the existing run: the two pipes next to the valve are shortened and their two weld connectors moved to new short pipes; save the drawing first. New objects are built and joined first, the existing run is changed last with a step-by-step undo on failure. TWO-PHASE: default is a dry run with the plan; dry_run=false does it. valve_handle = the existing valve; tee_clearance (default 400 mm from the existing flange weld to the tee port); height (default: clears the existing valve by 300 mm, at least 600).',
    inputSchema: { type: 'object', properties: { valve_handle: { type: 'string' }, bypass_nd: { type: 'number', exclusiveMinimum: 0 }, tee_clearance: { type: 'number', exclusiveMinimum: 0 }, height: { type: 'number', exclusiveMinimum: 0 }, variant: { type: 'string' }, gasket_type: { type: 'string' }, bolt_length: { type: 'number', exclusiveMinimum: 0 }, spec: { type: 'string' }, dry_run: { type: 'boolean' } }, required: ['valve_handle'], additionalProperties: false } },
  { name: 'add_supports', description: 'Places КП supports (OST 36-146-88) on pipes and builds the sub-structure under each one. Rules: supports next to every valve on both sides first, then at a fixed step (default 3000 mm); at least 100 mm from a weld axis to the edge of the support (GOST 32569 cl. 10.4.3, 11.2.6). The support spec follows the pipe material (09Г2С -> "!_Опоры ОСТ 36-146-88 09Г2С", steel 20 -> "...ВСт3пс"). Sub-structure per support (user sample): upper plate 250x250x5, 12П post down to the floor plate, floor plate; its height is computed from the floor level to the pipe axis. Plates and posts are created in the background through the Plant 3D commands PLANTSTEELPLATE and PLANTSTEELMEMBER (no managed API): the result carries a job id, call again with {"job": id} to see the progress; the profile and plate thickness come from the current structure settings of Plant 3D, check them in the result. TWO-PHASE: default is a dry run with the plan and warnings; dry_run=false places. pipes = handles of the pipes (max 40); the other side of a valve that is not in the list is reported as a warning.',
    inputSchema: { type: 'object', properties: { pipes: { type: 'array', items: { type: 'string' }, minItems: 1, maxItems: 40 }, step: { type: 'number', minimum: 500 }, weld_margin: { type: 'number', minimum: 100 }, support_half_width: { type: 'number', exclusiveMinimum: 0 }, support_height: { type: 'number', exclusiveMinimum: 0 }, floor_z: { type: 'number' }, structure: { type: 'boolean' }, connect: { type: 'boolean' }, support_family: { type: 'string' }, support_kind: { type: 'string' }, spec: { type: 'string' }, job: { type: 'string' }, dry_run: { type: 'boolean' } }, additionalProperties: false } },
  { name: 'create_tracing', description: 'Heat tracing of a STRAIGHT main line by the user\'s sample (view 3): two DN20 tracers (pipe 25x4, steel 20, spec "!_Rus-PipeAndFittings_ст20_PN16") on both sides, 45 degrees below the axis, 2 mm between the walls; around every flange and valve each tracer goes sideways (S-shape of two pipe bends, radius 90, angle chosen from the valve width); the ends are closed with 180 degree bends, so the tracing is one closed loop without inlet or outlet. Pipes, bends and a Buttweld connector at every joint; rolls back on any failure. The main line must be a straight run of pipes, flanges and valves (tees, reducers, elbows are not supported yet). TWO-PHASE: default is a dry run with the offsets, detours, part counts and warnings; dry_run=false creates. pipes = handles of the main pipes of the section.',
    inputSchema: { type: 'object', properties: { pipes: { type: 'array', items: { type: 'string' }, minItems: 1, maxItems: 40 }, spec: { type: 'string' }, nd: { type: 'number', exclusiveMinimum: 0 }, wall: { type: 'number', exclusiveMinimum: 0 }, gap: { type: 'number', minimum: 0 }, radius: { type: 'number', exclusiveMinimum: 0 }, pad: { type: 'number', minimum: 0 }, end_margin: { type: 'number', minimum: 0 }, tail_u: { type: 'number', minimum: 0 }, min_straight: { type: 'number', minimum: 0 }, handle: { type: 'number', minimum: 0 }, dip_radius: { type: 'number', exclusiveMinimum: 0 }, color: { type: 'integer', minimum: 0, maximum: 255 }, dip_tail: { type: 'number', minimum: 0 }, branch_overshoot: { type: 'number', minimum: 0 }, dip_side: { type: 'number' }, heat_branches: { type: 'boolean' }, stage: { type: 'integer', minimum: 1, maximum: 3 }, dry_run: { type: 'boolean' } }, required: ['pipes'], additionalProperties: false } },
  { name: 'create_tracing_route', description: 'Heat tracing of a whole pipe ROUTE (straight sections joined by 90 degree elbows, risers included; flanges, valves, spectacle blinds, tees whose branch is passed by). Two DN20 tracers keep their side of the pipe through the elbows (concentric arcs), step sideways round wide parts (their real boxes are used), and are closed with 180 degree bends beyond the two free ends. TWO-PHASE like create_tracing (dry run by default; stage 1/2 rehearsals). pipe = handle of any plain pipe of the route.',
    inputSchema: { type: 'object', properties: { pipe: { type: 'string' }, spec: { type: 'string' }, nd: { type: 'number', exclusiveMinimum: 0 }, wall: { type: 'number', exclusiveMinimum: 0 }, gap: { type: 'number', minimum: 0 }, radius: { type: 'number', exclusiveMinimum: 0 }, pad: { type: 'number', minimum: 0 }, end_margin: { type: 'number', minimum: 0 }, tail_u: { type: 'number', minimum: 0 }, min_straight: { type: 'number', minimum: 0 }, handle: { type: 'number', minimum: 0 }, clear_tolerance: { type: 'number', minimum: 0 }, min_gap: { type: 'number', minimum: 0 }, dip_tail: { type: 'number', minimum: 0 }, branch_overshoot: { type: 'number', minimum: 0 }, dip_radius: { type: 'number', exclusiveMinimum: 0 }, dip_side: { type: 'number' }, heat_branches: { type: 'boolean' }, twist: { type: 'boolean' }, stage: { type: 'integer', minimum: 1, maximum: 3 }, dry_run: { type: 'boolean' } }, required: ['pipe'], additionalProperties: false } },
  { name: 'probe_bend', description: 'Experiment for tracing: lists the parametric bend ("ИЗГИБ ТРУБЫ", PipeBend) parts of a spec with all their properties. With place=true it places ONE trial bend (radius, angle) far from the model, reads its ports and description back and erases it again.',
    inputSchema: { type: 'object', properties: { spec: { type: 'string' }, nd: { type: 'number', exclusiveMinimum: 0 }, radius: { type: 'number', exclusiveMinimum: 0 }, angle: { type: 'number', exclusiveMinimum: 0, maximum: 180 }, type: { type: 'string' }, l1: { type: 'number', minimum: 0 }, l2: { type: 'number', minimum: 0 }, wall: { type: 'number', exclusiveMinimum: 0 }, place: { type: 'boolean' }, keep: { type: 'boolean' }, line_group: { type: 'integer' } }, additionalProperties: false } },
  { name: 'create_joint', description: 'Creates the Buttweld connector of a weld joint between two parts whose ports coincide and face each other (for example a flange and a pipe whose connector was lost), and joins both parts to it. TWO-PHASE: default is a dry run; dry_run=false creates. Refuses if a connector is already at the joint.',
    inputSchema: { type: 'object', properties: { handleA: { type: 'string' }, handleB: { type: 'string' }, spec: { type: 'string' }, joint: { type: 'string', description: 'Plant joint type override for the weld connector' }, s2_first: { type: 'boolean', description: 'Connect S2 before S1' }, dry_run: { type: 'boolean' } }, required: ['handleA', 'handleB'], additionalProperties: false } },
  { name: 'repair_connections', description: 'Restores missing connector links: every connector port without a connection is joined to the part port that sits on it and faces it. TWO-PHASE: default is a dry run listing what would be joined; dry_run=false applies. handles = connectors to check (default: all connectors in the drawing).',
    inputSchema: { type: 'object', properties: { handles: { type: 'array', items: { type: 'string' }, minItems: 1, maxItems: 100 }, dry_run: { type: 'boolean' } }, additionalProperties: false } },
  { name: 'restore_branch_link', description: 'Restore the connection between a Tap connector and the run pipe by adding a dynamic port. Dry run by default; dry_run=false applies.',
    inputSchema: { type: 'object', properties: { run_handle: { type: 'string' }, connector_handle: { type: 'string' }, branch_handle: { type: 'string' }, dry_run: { type: 'boolean' } }, required: ['run_handle', 'connector_handle', 'branch_handle'], additionalProperties: false } },
  { name: 'set_layer', description: 'Move drawing objects to a layer, creating it with the specified color if needed. Dry run by default; dry_run=false applies. Object color follows the layer by default.',
    inputSchema: { type: 'object', properties: { handles: { type: 'array', items: { type: 'string' }, minItems: 1, maxItems: 200 }, layer: { type: 'string', minLength: 1 }, color: { type: 'integer', minimum: 1, maximum: 255 }, by_layer: { type: 'boolean' }, dry_run: { type: 'boolean' } }, required: ['handles', 'layer'], additionalProperties: false } },
  { name: 'port_conditions', description: 'Read the end conditions and matching properties of a Plant 3D part by DWG handle.',
    inputSchema: { type: 'object', properties: { handle: { type: 'string' } }, required: ['handle'], additionalProperties: false } },
  { name: 'inspect_connector', description: 'Read-only. Contents of a joint connector (AcPpDb3dConnector) by DWG handle: joint type, axes, ports and every sub-part (weld, gasket block, bolt set) with its property set.',
    inputSchema: { type: 'object', properties: { handle: { type: 'string' } }, required: ['handle'], additionalProperties: false } },
  { name: 'get_connections', description: 'Read-only. The connections of a part or connector by DWG handle: which object and port each connection joins.',
    inputSchema: { type: 'object', properties: { handle: { type: 'string' } }, required: ['handle'], additionalProperties: false } },
  { name: 'probe_part_type', description: 'Read-only experiment (nothing is added to the drawing): how the element type of a bare property set can be set. Used to build weld joint elements.',
    inputSchema: { type: 'object', properties: {}, additionalProperties: false } },
  { name: 'get_ports', description: 'Ports (name, position, direction, end type) of a Plant 3D part by DWG handle (hex)',
    inputSchema: { type: 'object', properties: { handle: { type: 'string' } }, required: ['handle'], additionalProperties: false } },
  { name: 'delete_objects', description: 'DELETES Plant 3D objects by DWG handle (max 50). TWO-PHASE: default is a dry run listing what would be deleted; dry_run=false erases. Refuses non-Plant objects and objects outside model space. The dry run returns the drawing name and the connection count of each object; pass "document" with that name in the real call so a change of the active drawing in between cannot redirect the delete.',
    inputSchema: { type: 'object', properties: { handles: { type: 'array', items: { type: 'string' }, minItems: 1, maxItems: 50 }, document: { type: 'string' }, dry_run: { type: 'boolean' } }, required: ['handles'], additionalProperties: false } },
  { name: 'send_command', description: 'Disabled unless PLANT3D_MCP_ALLOW_COMMANDS=1 was set before starting Plant 3D. Queues a regeneration or redraw command in the active drawing.',
    inputSchema: { type: 'object', properties: { command: { type: 'string', enum: ['REGEN', 'REGENALL', 'REDRAW', 'REDRAWALL'] } }, required: ['command'], additionalProperties: false } },
  { name: 'list_catalog_parts', description: 'Read parts from a Plant 3D catalog (.pcat, SQLite) without Plant 3D. Pick the catalog with path or catalog (part of the file name, searched in the project and in the shared content folder); if several match, their list is returned. Filters: search (text in descriptions), class, category, size (NominalDiameter), material. Without filters returns counts by class/category.',
    inputSchema: { type: 'object', properties: { path: { type: 'string' }, catalog: { type: 'string' }, search: { type: 'string' }, class: { type: 'string' }, category: { type: 'string' }, size: { type: 'number' }, material: { type: 'string' }, limit: { type: 'integer' } } } },
];

// MCP annotations let clients gate writes; everything not listed here is read-only.
const WRITE_ANNOTATIONS = {
  insert_spec_part: { readOnlyHint: false, destructiveHint: true, idempotentHint: false },
  create_pipe: { readOnlyHint: false, destructiveHint: false, idempotentHint: false },
  create_pipeline: { readOnlyHint: false, destructiveHint: false, idempotentHint: false },
  create_branch: { readOnlyHint: false, destructiveHint: false, idempotentHint: false },
  create_bypass: { readOnlyHint: false, destructiveHint: false, idempotentHint: false },
  create_valve_bypass: { readOnlyHint: false, destructiveHint: true, idempotentHint: false },
  create_joint: { readOnlyHint: false, destructiveHint: false, idempotentHint: false },
  repair_connections: { readOnlyHint: false, destructiveHint: false, idempotentHint: true },
  restore_branch_link: { readOnlyHint: false, destructiveHint: false, idempotentHint: false },
  set_layer: { readOnlyHint: false, destructiveHint: true, idempotentHint: true },
  add_supports: { readOnlyHint: false, destructiveHint: false, idempotentHint: false },
  probe_bend: { readOnlyHint: false, destructiveHint: false, idempotentHint: false },
  create_tracing: { readOnlyHint: false, destructiveHint: false, idempotentHint: false },
  create_tracing_route: { readOnlyHint: false, destructiveHint: false, idempotentHint: false },
  delete_objects: { readOnlyHint: false, destructiveHint: true, idempotentHint: true },
  connect_parts: { readOnlyHint: false, destructiveHint: false, idempotentHint: true },
  send_command: { readOnlyHint: false, destructiveHint: true, idempotentHint: false },
};
for (const t of TOOLS) t.annotations = WRITE_ANNOTATIONS[t.name] ?? { readOnlyHint: true };

function findCatalogs(dir, depth = 4, out = []) {
  if (depth < 0) return out;
  let entries;
  try { entries = readdirSync(dir, { withFileTypes: true }); } catch { return out; }
  for (const e of entries) {
    const p = join(dir, e.name);
    if (e.isDirectory()) findCatalogs(p, depth - 1, out);
    else if (e.isFile() && /\.(pcat|pspc)$/i.test(e.name)) out.push(p);
  }
  return out;
}

async function listCatalogParts(args) {
  let path = args.path;
  if (path !== undefined && (typeof path !== 'string' || !isAbsolute(path) || /^[\\/]{2}/.test(path) || !/\.(pcat|pspc)$/i.test(path)))
    throw new Error('path must be an absolute local path to a .pcat or .pspc file');
  const inside = (root, file) => {
    const rel = relative(root, file);
    return rel && rel !== '..' && !rel.startsWith('..' + sep) && !isAbsolute(rel);
  };
  const roots = [];
  function addRoot(dir) {
    if (!dir || !isAbsolute(dir) || /^[\\/]{2}/.test(dir)) return;
    try { roots.push({ path: dir, real: realpathSync(dir) }); } catch { /* root unavailable */ }
  }
  addRoot(CATALOG_DIR);
  const contains = (root, file) => inside(root.path, file) || inside(root.real, file);
  if (!path || !roots.some(root => contains(root, path))) {
    try {
      const info = await call('plant_project_info', {});
      if (info.ok) addRoot(info.result.directory);
    } catch { /* Plant 3D not running: catalog folder only */ }
  }
  if (!path) {
    let found = [...new Set(roots.flatMap(root => findCatalogs(root.path)))];
    if (args.catalog) {
      const q = args.catalog.toLowerCase();
      found = found.filter(f => basename(f).toLowerCase().includes(q));
    }
    if (found.length !== 1) return { note: 'Specify "catalog" (part of file name) or "path"; matching .pcat/.pspc files:', files: found };
    path = found[0];
  }
  if (!roots.some(root => contains(root, path)))
    throw new Error('catalog must be inside PLANT3D_CATALOG_DIR or the active Plant 3D project');
  path = realpathSync(path);
  if (!roots.some(root => inside(root.real, path)) || !['.pcat', '.pspc'].includes(extname(path).toLowerCase()))
    throw new Error('catalog must resolve to a .pcat or .pspc file inside an allowed directory');
  const file = statSync(path);
  if (!file.isFile() || file.size > MAX_CATALOG_BYTES)
    throw new Error('catalog must be a regular file of at most 512 MiB');
  // Copy first: the live catalog may be locked or being edited by Plant 3D.
  let tmpDir, db;
  try {
    tmpDir = mkdtempSync(join(tmpdir(), 'plant3d-mcp-'));
    const tmp = join(tmpDir, 'catalog.db');
    copyFileSync(path, tmp);
    db = new DatabaseSync(tmp, { readOnly: true });
    const limit = Math.min(Math.max(Number(args.limit ?? 50) || 0, 0), 500);
    const where = [], params = [];
    if (args.search) { where.push('(e.PartFamilyLongDesc like ? or e.PartSizeLongDesc like ? or e.ShortDescription like ?)'); const s = `%${args.search}%`; params.push(s, s, s); }
    if (args.class) { where.push('b.PnPClassName = ?'); params.push(args.class); }
    if (args.category) { where.push('e.PartCategory = ?'); params.push(args.category); }
    if (args.size !== undefined) { where.push('e.NominalDiameter = ?'); params.push(args.size); }
    if (args.material) { where.push('e.Material like ?'); params.push(`%${args.material}%`); }
    const from = 'from EngineeringItems e join PnPBase b on b.PnPID = e.PnPID' + (where.length ? ' where ' + where.join(' and ') : '');
    const total = db.prepare(`select count(*) c ${from}`).get(...params).c;
    const byClass = db.prepare(`select b.PnPClassName class, e.PartCategory category, count(*) n ${from} group by 1, 2 order by n desc`).all(...params).map(r => ({ ...r }));
    const items = where.length || args.limit
      ? db.prepare(`select e.PnPID id, b.PnPClassName class, e.PartCategory category, e.PartFamilyLongDesc family, e.PartSizeLongDesc description,
          e.NominalDiameter size, e.MatchingPipeOd od, e.Material material, e.CompatibleStandard standard, e.Weight weight, e.EndType endType,
          e.PressureClass pressureClass, e.ItemCode itemCode ${from} limit ?`).all(...params, limit).map(r => ({ ...r }))
      : [];
    return { file: path, total, byClass, items };
  } finally {
    try { db?.close(); }
    finally { if (tmpDir) rmSync(tmpDir, { recursive: true, force: true }); }
  }
}

// The plugin writes a per-session token here; without it the endpoint answers 403.
function readToken() {
  const local = process.env.LOCALAPPDATA;
  if (!local || !isAbsolute(local)) throw new Error('LOCALAPPDATA must be set to an absolute path');
  const p = join(local, 'Plant3dMcp', 'token');
  try { return readFileSync(p, 'utf8').trim(); } catch { return null; }
}

async function call(tool, args) {
  if (tool === 'list_catalog_parts') return { ok: true, result: await listCatalogParts(args) };
  const token = readToken();
  if (!token) throw new Error('token file not found: start Plant 3D, load the plugin and start the server with PSMCP');
  let r;
  try {
    r = await fetch(URL_BASE, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
      body: JSON.stringify({ tool, args }),
      signal: AbortSignal.timeout(90_000),
    });
  } catch (e) {
    if (e.name === 'TimeoutError') throw new Error('timed out; the operation may still have completed - check the drawing before retrying');
    throw e;
  }
  if (r.status === 403) throw new Error('plugin rejected the request (403): check or restart the MCP server with PSMCP');
  return r.json();
}

async function handle(msg) {
  const { id, method, params } = msg;
  switch (method) {
    case 'initialize':
      return { protocolVersion: params?.protocolVersion || '2025-06-18', capabilities: { tools: {} }, serverInfo: { name: 'plant3d', version: '0.1.0' } };
    case 'tools/list':
      return { tools: TOOLS };
    case 'tools/call': {
      try {
        if (!TOOLS.some(tool => tool.name === params?.name)) throw new Error('Unknown tool: ' + params?.name);
        const res = await call(params.name, params.arguments || {});
        return { isError: !res.ok, content: [{ type: 'text', text: JSON.stringify(res.ok ? res.result : res.error, null, 2) }] };
      } catch (e) {
        return { isError: true, content: [{ type: 'text', text: e.name === 'TypeError' ? `Plant 3D plugin unreachable (is the server enabled with PSMCP?): ${e.message}` : e.message }] };
      }
    }
    case 'ping': return {};
    default:
      if (id === undefined) return undefined; // notification
      throw Object.assign(new Error('Method not found'), { code: -32601 });
  }
}

const lines = createInterface({ input: process.stdin, crlfDelay: Infinity });
for await (const raw of lines) {
    const line = raw.trim();
    if (!line) continue;
    let msg;
    try { msg = JSON.parse(line); }
    catch { process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: null, error: { code: -32700, message: 'Parse error' } }) + '\n'); continue; }
    if (!msg || typeof msg !== 'object' || Array.isArray(msg) || typeof msg.method !== 'string') {
      process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: null, error: { code: -32600, message: 'Invalid Request' } }) + '\n');
      continue;
    }
    try {
      const result = await handle(msg);
      if (msg.id !== undefined && result !== undefined) process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: msg.id, result }) + '\n');
    } catch (e) {
      if (msg.id !== undefined) process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: msg.id, error: { code: e.code || -32603, message: e.message } }) + '\n');
    }
}
