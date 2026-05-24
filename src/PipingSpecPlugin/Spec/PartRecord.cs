namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Raw Plant 3D EngineeringItem row, read straight from
    /// DataLinksManager.GetAllProperties. Mirrors the SELECT in the Python
    /// edition's fetch_rows() one-to-one so the downstream bucket logic
    /// can stay structurally identical. Strings are kept exactly as
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

        // Optional pressure class / facing (used by valve-companion fallback
        // match — Python's "fallback_key" relies on Spec + NominalDiameter,
        // but we keep these around for future refinements).
        public string PressureClass;
        public string Facing;
    }
}
