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
        public string Status;
        public string PartCategory;
        public string PartFamilyLongDesc;
        public string PartSizeLongDesc;
        public string ShortDescription;
        public string NominalDiameter;
        public string NominalUnit;
        public string Material;
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
        // bolts in the set). Builder multiplies the row count by this so
        // a single BoltSet PnPID with Quantity=8 lands as qty=8 in the
        // spec. Falls back to 1 when the property is missing.
        public string Quantity;

        // Absolute path of the DWG this record was read from. PnPID alone
        // can't identify the source drawing — row-ids live in the
        // drawing-local namespace and the same int can name different
        // parts in different files. Scope=dwg filtering must compare by
        // this path, not by PnPID. Populated by WriteSpecCommand after
        // each ReadOneDrawing; null if unknown.
        public string SourceDwgPath;

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
    }
}
