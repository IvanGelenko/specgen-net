namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Raw Plant 3D EngineeringItem row, read straight from
    /// DataLinksManager.GetAllProperties. Strings are kept exactly as
    /// stored — trimming and null-handling happen in SpecRowBuilder.
    /// </summary>
    public class PartRecord
    {
        public int PnPID;
        public int SourcePnPID; // original DLM row number before scan-wide remapping
        public string Status;
        public string PartCategory;
        public string PartFamilyLongDesc;
        public string PartSizeLongDesc;
        public string ShortDescription;
        public string NominalDiameter;
        public string NominalUnit;
        public string Material;
        public string MaterialCode;
        public string Spec;
        public string ItemCode;
        public string Manufacturer;
        public string DesignStd;
        public string CompatibleStandard;
        public string Weight;          // total mass (string form, parsed lazily)
        public string LengthUnit;
        public string WeightUnit;
        public string Length;          // pipe length (mm by default)
        public string LinearWeight;    // pipe kg/m
        public string LinearWeightUnit;
        public string WallThickness;
        public string Schedule;
        public string LineNumberTag;
        public string ContentIsoSymbolDefinition;  // used to detect supports
        public string EquipTag;        // for Equipment naming; rarely populated outside Equipment

        // Position / centre-of-gravity coordinates as raw strings from
        // DataLinks. Used by valve-companion detection for the 3D
        // proximity match — keep them strings, the discovery code parses
        // them on demand only when needed.
        public string PositionX;
        public string PositionY;
        public string PositionZ;
        public string CogX;
        public string CogY;
        public string CogZ;

        // Millimetres per drawing unit of the DWG the record was read from
        // (from Database.Insunits). Position/COG above are in DRAWING units,
        // not in the part's catalogue LengthUnit — the two are unrelated (a
        // metric catalogue can sit in an inch drawing). null = unknown /
        // unitless drawing; consumers then assume millimetres, the Plant 3D
        // metric default this plug-in targets. Set by PartReader.
        public double? DrawingMmPerUnit;

        // Filled by PnPClassName (e.g. "Pipe", "P3dConnector", "Equipment");
        // we need this in addition to PartCategory to recognise connectors
        // — their PartCategory is empty but the class is "P3dConnector".
        public string PnPClassName;

        // Optional pressure class / facing. The valve-companion fallback
        // match keys on Spec + NominalDiameter; these are kept around for
        // future refinements.
        public string PressureClass;
        public string Facing;

        // For multi-piece parts (bolt sets carry a "Quantity" of physical
        // bolts in the set — NumberInSet, see PartPropertyReader). Builder
        // multiplies the row count by this, for BoltSet / Fasteners only,
        // so a single BoltSet PnPID with Quantity=8 lands as qty=8 in the
        // spec. Other fasteners without it count as 1; a BoltSet without it
        // is NOT one bolt — it goes to the review section.
        public string Quantity;

        // Absolute path of the DWG this record was read from. PnPID alone
        // can't identify the source drawing — row-ids live in the
        // drawing-local namespace and the same int can name different
        // parts in different files. Scope=dwg filtering must compare by
        // this path, not by PnPID. Populated by DrawingPartIds.Remap
        // (project scan) or WriteSpecCommand.ScanSingleDrawing; null if
        // unknown.
        public string SourceDwgPath;

        // The part's OWN insulation thickness (PipeRunComponent, LengthUnit).
        // Plant 3D draws insulation on a part only when it is > 0; the line's
        // value is deliberately not consulted (an uninsulated metre of pipe
        // on an insulated line keeps 0 / blank here).
        public string InsulationThickness;

        // Doubts raised while READING this part (e.g. several line groups —
        // the first was taken). PartReader only knows the part, not the
        // user's filters, so it parks them here; SpecRowBuilder turns them
        // into ReviewItems only for parts that actually reach the spec.
        // null when there is nothing to report.
        public System.Collections.Generic.List<string> ReadWarnings;

        /// <summary>
        /// True when this record represents a Plant 3D PipeBend (a curved
        /// pipe segment with its own engineering item). Used by the
        /// "Учитывать изгибы как трубу" feature: when on, the bend's
        /// length folds into the neighbouring straight pipe instead of
        /// appearing on its own row.
        /// </summary>
        public bool IsPipeBend
            => !string.IsNullOrEmpty(PnPClassName)
               && PnPClassName.IndexOf("PipeBend",
                       System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Shallow copy (ReadWarnings list shared).</summary>
        public PartRecord Clone() => (PartRecord)MemberwiseClone();
    }
}
