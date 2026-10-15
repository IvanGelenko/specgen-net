namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// One data row read from the Ведомость трубопроводов (ВТ) workbook,
    /// keyed by the "n/n" column. Values are the raw cell text; no unit
    /// conversion or picklist mapping happens here — that's
    /// LineSyncService's job.
    /// </summary>
    public class VtRowData
    {
        public int RowNumber;      // 1-based Excel row, for diagnostics.
        public string NN;          // "n/n" — sync key (e.g. "1", "2", "3.1").
        public string Id;          // "ИД трубопровода" — kept for reference, no longer the sync key.
        public string Designation; // "Обозначение" — the P3dLineGroup.Tag-style line tag(s), \n-separated when multiple.
        public string ProjectCode; // "Шифр проекта"
        public string PipeSize;    // "Трубопровод (наружный диаметр)"
        public string Material;    // "Материал трубопровода"
        public string Product;     // "Наименование транспортируемого продукта"
        public string GostGroup;   // "Классификация по ГОСТ 32569"
        public string TrTsGroup;   // "Классификация по ТР ТС 032-2013"
        public string WorkPressure;      // Рабочие условия / Давление
        public string WorkTemperature;   // Рабочие условия / Температура
        public string DesignPressure;    // Расчётные условия / Давление
        public string DesignTemperature; // Расчётные условия / Температура
        public string TestStrength;      // Условия испытаний / На прочность/плотность
        public string TestTightness;     // Условия испытаний / На герметичность
        public string Density;           // Плотность, кг/м3
        public string DensityTemperature; // t°С (при плотности)
        public string CorrosionRate;     // Скорость коррозии, мм/год
        public string ServiceLife;       // Срок службы / отбраковочная толщина
        public string NdControl;         // Неразрушающий контроль сварных швов, %
        public string HeatTreatment;     // Термообработка сварных соединений
        public string Additional;        // Дополнительные указания
        public string InsulationThickness; // Толщина изоляции, мм
    }
}
