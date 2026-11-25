using System.Globalization;

namespace IsoGenerator.Core;

internal static class BomQuantities
{
    internal static bool Includes(Part part,IReadOnlySet<string>? excluded,bool instrumentsOnly)=>
        !part.IsConnector&&excluded?.Contains(part.Handle)!=true&&
        (!instrumentsOnly||BomCategories.Classify(part)!=BomCategory.Instruments);

    internal static bool IsBoltSet(Part part)=>part.CatalogClass.Equals("BoltSet",StringComparison.OrdinalIgnoreCase)||
        part.CatalogClass.Equals("BoltSetSubPart",StringComparison.OrdinalIgnoreCase);

    internal static string Unit(Part part,bool excludeHardware)
    {
        if(part.Component==ComponentKind.Pipe)return "м";
        if(excludeHardware&&BomCategories.Classify(part)==BomCategory.Valves)return "компл.";
        var unit=part.Properties.GetValueOrDefault("IsoQuantityUnit","").Trim();
        return unit.Length>0?unit:IsBoltSet(part)?"компл.":"шт.";
    }

    internal static double Amount(Part part)
    {
        if(part.Component==ComponentKind.Pipe)
        {
            var ends=part.Ports.Where(port=>port.Name.Equals("S1",StringComparison.OrdinalIgnoreCase)||
                port.Name.Equals("S2",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(ends.Length!=2||ends[0].Name.Equals(ends[1].Name,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{part.Handle}: для определения длины трубы нужны порты S1 и S2.");
            double length=ends[0].Position.DistanceTo(ends[1].Position);
            if(!(length>0)||!double.IsFinite(length))
                throw new InvalidDataException($"{part.Handle}: некорректная длина трубы.");
            return length/1000;
        }
        var quantity=part.Properties.GetValueOrDefault("IsoQuantity","");
        bool bolts=IsBoltSet(part);
        if(bolts)
            quantity=part.Properties.GetValueOrDefault("NumberInSet",quantity);
        else if(quantity.Length==0)return 1;
        if(!double.TryParse(quantity,NumberStyles.Float,CultureInfo.InvariantCulture,out double amount)||
           !double.IsFinite(amount)||amount<=0||(bolts&&amount!=Math.Truncate(amount)))
            throw new InvalidDataException(bolts
                ?$"{part.Handle}: не задано корректное целое число крепёжных деталей NumberInSet."
                :$"{part.Handle}: неверное количество в спецификации.");
        return amount;
    }
}
