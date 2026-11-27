using System;
using System.Collections.Generic;
using System.Linq;

namespace PipingSpecPlugin.Spec
{
    internal sealed record VtField(string Property, string Label, Func<VtRowData, string> Read,
        Action<VtRowData, string> Assign, string[] Headers, string Group = null,
        bool Numeric = false, Func<string, string> Parse = null)
    {
        internal string ValueFor(VtRowData row)
        {
            var value = Read(row);
            return Parse == null ? value?.Trim() : Parse(value);
        }
    }

    internal static class VtFieldMappings
    {
        internal const string GostHeader = "Классификация по ГОСТ 32569";
        const string WorkingConditions = "Рабочие условия трубопровода";
        const string DesignConditions = "Расчётные условия трубопровода";
        const string PressureHeader = "Давление, МПа(изб.)";
        const string TemperatureHeader = "Температура, °С";

        internal static readonly VtField[] DirectFields =
        {
            new("GostGroup", GostHeader, row => row.GostGroup, (row, value) => row.GostGroup = value,
                new[] { GostHeader }),
            new("TRTSGroup", "Классификация по ТР ТС 032-2013", row => row.TrTsGroup, (row, value) => row.TrTsGroup = value,
                new[] { "Классификация по ТР ТС 032-2013" }),
            new("Pwork", "Давление рабочее, МПа", row => row.WorkPressure, (row, value) => row.WorkPressure = value,
                new[] { PressureHeader }, Group: WorkingConditions, Numeric: true),
            new("Tworking", "Температура рабочая, С", row => row.WorkTemperature, (row, value) => row.WorkTemperature = value,
                new[] { TemperatureHeader }, Group: WorkingConditions, Numeric: true),
            new("PressureDesign", "Давление расчетное, МПа", row => row.DesignPressure, (row, value) => row.DesignPressure = value,
                new[] { PressureHeader }, Group: DesignConditions, Numeric: true),
            new("Tdesign", "Температура расчетная, С", row => row.DesignTemperature, (row, value) => row.DesignTemperature = value,
                new[] { TemperatureHeader }, Group: DesignConditions, Numeric: true),
            new("Thermo", "Термообработка", row => row.HeatTreatment, (row, value) => row.HeatTreatment = value,
                new[] { "Термообработка сварных соединений" }),
            new("Additional", "Доп. указания", row => row.Additional, (row, value) => row.Additional = value,
                new[] { "Дополнительные указания", "Дополнительныеуказания" }),
            new("InsulationThickness", "Толщина изоляции, мм", row => row.InsulationThickness, (row, value) => row.InsulationThickness = value,
                new[] { "Толщина изоляции, мм" }, Numeric: true),
        };

        // Keep the existing plan order: weld inspection is evaluated after the test pressures.
        internal static readonly VtField NdControl = new("NDControl", "Объем контроля швов, %", row => row.NdControl,
            (row, value) => row.NdControl = value, new[] { "Неразрушающий контроль сварных швов, %" },
            Numeric: true, Parse: VtValueParsing.ExtractNdPercent);

        internal static IEnumerable<VtField> ImportedFields => DirectFields.Append(NdControl);

        internal static readonly (string Label, Func<LineDesignationParts, string> Value, string Property)[] DesignationFields =
        {
            ("Размер (ГОСТ)",                     p => p.NominalSize,     "Size2"),
            ("Код среды",                         p => p.EnvironmentCode, "Код_трубопровода"),
            ("Материал трубопровода",             p => p.MaterialCode,    "Material_pipe"),
            ("Код условного давления",            p => p.PressureCode,    "Ysl_pressure"),
            ("Тип присоединения арматуры",        p => p.FlangeCode,      "flanges"),
            ("Тип изоляции",                      p => p.InsulationCode,  "InsulationType"),
            ("Тип обогрева (Спутник)",            p => p.SputnikCode,     "Heat_type"),
        };

        static readonly (string Property, string Label)[] OtherColumns =
        {
            ("Tag", "Идентификатор"),
            ("Номер", "Номер линии"),
            ("TestD", "Испытания на прочность, МПа"),
            ("TestW", "Испытания на плотность, МПа"),
            (NdControl.Property, NdControl.Label),
            ("CorrosionSpeed", "Скорость коррозии, мм/год"),
            ("srok", "Срок службы, лет"),
            ("Corrosion", "Прибавка на коррозию (расч.)"),
        };

        static readonly (string Property, string Label)[] Columns = DirectFields.Select(field => (field.Property, field.Label))
            .Concat(DesignationFields.Select(field => (field.Property, field.Label))).Concat(OtherColumns).ToArray();

        internal static IReadOnlyList<string> KnownLineGroupColumns { get; } = Columns.Select(column => column.Property).ToArray();
        internal static IReadOnlyDictionary<string, string> KnownLineGroupColumnLabels { get; } =
            Columns.ToDictionary(column => column.Property, column => column.Label, StringComparer.OrdinalIgnoreCase);
    }
}
