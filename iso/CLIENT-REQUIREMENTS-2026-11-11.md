# SpecGen isometric requirements, 11 November 2026

These requirements supersede conflicting earlier layout settings.

1. Installer validates supported Plant 3D 2025/2026 and required runtime/payload dependencies. No SDK, Python, MCP, source tree, or AI is required on client machines.
2. User selects a DWG template; persist the last valid selection per user. Validate the template before rendering.
3. User supplies one finite positive symbol scale, shared by all component categories and diameters.
4. Remove the project-style selector and runtime IsoConfig dependency. Project symbol DWG and SKEY mapping remain the symbol source.
5. Leader strokes may cross any elements. Keep leader text readable and allow up to 30 mm reach.
6. Excluding flanges/gaskets/fasteners changes BOM visibility only. Always preserve real flange lengths in engineering dimensions.
7. Vertical dimensions start at the adjoining pipe axis, including hidden branch offsets where applicable.
8. Pipes appear first in the BOM, sorted by ascending numeric nominal diameter.
9. Within every BOM category sort by ascending numeric nominal diameter, with deterministic tie-breaks.
10. Round displayed meter quantities upward to one decimal place after aggregating quantities. Preserve unrounded engineering data.
11. Remove fixed pipe-length floors; reserve only the paper space needed for dimension text/arrows. Do not resize native component geometry.
12. Instruments include sensors, pressure gauges, and thermometers. Valves and flowmeters belong to pipeline valves/equipment.
13. Use native aligned dimensions (Russian RZMPARAL / AlignedDimension), with text centered above the dimension baseline.

Additional acceptance requirements: each drawing sheet contains one connected pipeline fragment; model breakpoints honored unless explicitly ignored; separate clean BOM continuation pages; one combined PDF; atomic publishing; visible cancellable progress. Do not claim the KSN001 three-sheet goal or native 2025 runtime compatibility until actually verified.

## Implementation / validation (1.3.35)

All thirteen items are implemented. Native validation in 2025 and 2026 confirmed 68 aligned dimensions, 59 vertical-Z extension directions on nonvertical runs, centered text and unchanged engineering values. All five actual BOM tables report 3 mm text height. Core regression checks cover hardware exclusion versus dimensions, pipe-axis branch dimensions, numeric diameter sorting, meter display rounding without changing raw lengths, equipment categorization and crossing leaders within 30 mm. Installer prerequisite and payload checks were executed without installing over the working plugin; missing/corrupt payloads were rejected.

Four marked sections are preserved at manual symbol scale 20; additional clean BOM continuation pages are permitted. Installer checks and native rendering were tested on this workstation; a separate clean-client installation has not been performed.
