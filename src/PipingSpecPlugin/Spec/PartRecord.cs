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
    }
}
