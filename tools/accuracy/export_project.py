"""Read-only audit snapshot; never edits the Plant project or its database."""
import json
import sqlite3
import sys
import re
from decimal import Decimal, ROUND_CEILING, ROUND_HALF_EVEN
from pathlib import Path

root = Path(sys.argv[1]).resolve()
out = Path(sys.argv[2]).resolve()
out.mkdir(parents=True, exist_ok=True)
db = sqlite3.connect((root / 'Piping.dcf').as_uri() + '?mode=ro', uri=True)
db.row_factory = sqlite3.Row
db.execute('BEGIN')
tables = {r['TableName']: r['BaseTable'] for r in db.execute('select * from PnPTables')}
by_table = {}
for table in tables:
    try:
        by_table[table] = {r['PnPID']: dict(r) for r in db.execute('select * from "' + table.replace('"', '""') + '"')}
    except (sqlite3.Error, KeyError):
        pass
links = {}
for r in db.execute('select * from PnPDataLinks'):
    links.setdefault(r['RowId'], set()).add(r['DwgId'])
line_tags = {}
for r in db.execute('select p.Part,g.Tag from P3dLineGroupPartRelationship p join P3dLineGroup g on g.PnPID=p.LineGroup'):
    line_tags.setdefault(r['Part'], r['Tag'])
records, expected = [], []
piece_categories = {'BoltSet':'Fasteners', 'Gasket':'Fasteners', 'Valve':'Valves',
    'Flange':'Flanges', 'BlindFlange':'Flanges', 'Support':'Supports',
    'Instrument':'Instruments', 'Elbow':'Fittings', 'Reducer':'Fittings',
    'Tee':'Fittings', 'Nipple':'Fittings', 'Plug':'Fittings', 'Filter':'PipeSpecialties',
    'BlindDisk':'Miscellaneous', 'Cap':'Miscellaneous', 'Strainer':'Miscellaneous'}
expected_pieces = {}
raw_length, bolts, imperial = Decimal(0), Decimal(0), 0
for id, item in by_table['EngineeringItems'].items():
    props = dict(by_table['PnPBase'][id])
    table, visited = props['PnPClassName'], set()
    while table and table not in visited:
        visited.add(table)
        props.update(by_table.get(table, {}).get(id, {}))
        table = tables.get(table)
    record = {k: str(v) if v is not None else None for k, v in props.items()}
    record['PnPID'] = id
    record['SourcePnPID'] = id
    record['SourceDwgPath'] = 'drawing:' + ','.join(map(str, sorted(links.get(id, []))))
    record['LineNumberTag'] = line_tags.get(id, props.get('LineNumberTag'))
    record['EquipTag'] = props.get('Tag')
    record['Length'] = record.get('Length') or record.get('CutLength')
    record['Quantity'] = record.get('NumberInSet') or record.get('Quantity')
    for dst, src in [('PositionX','Position X'),('PositionY','Position Y'),('PositionZ','Position Z'),('CogX','COG X'),('CogY','COG Y'),('CogZ','COG Z')]:
        record[dst] = record.get(src)
    records.append(record)
    if props['PnPClassName'] == 'Pipe':
        length = Decimal(str(props['Length'])) / 1000
        raw_length += length
        weight = Decimal(str(props['LinearWeight'])) if props['LinearWeight'] is not None else Decimal(str(props.get('Weight') or 0)) / length
        if props['LinearWeightUnit'].upper() == 'LB/FT':
            weight *= Decimal('0.45359237') / Decimal('0.3048')
            imperial += 1
        expected.append({'Id': id, 'Qty': float(length.quantize(Decimal('.1'), rounding=ROUND_CEILING)),
                         'Weight': float(weight.quantize(Decimal('.001'), rounding=ROUND_HALF_EVEN))})
    if props['PnPClassName'] == 'BoltSet':
        bolts += Decimal(str(props['NumberInSet']))
    category = piece_categories.get(props['PnPClassName'])
    if category:
        expected_pieces[category] = expected_pieces.get(category, 0) + (int(Decimal(str(props['NumberInSet']))) if props['PnPClassName']=='BoltSet' else 1)

def text(value):
    return (value or '').strip()

def decimal_value(value):
    return Decimal(str(value).replace(',', '.')) if value not in (None, '') else Decimal(0)

def pipe_groups(parts, by_line):
    # Separate decimal reference calculation: sum source millimetres for
    # identical procurement items, then round each position once, upward.
    groups = {}
    for p in parts:
        name = next((p.get(k) for k in ('PartSizeLongDesc','PartFamilyLongDesc','ShortDescription') if text(p.get(k))), '')
        name = re.sub(r'\s+', ' ', name).strip()
        standard = text(p.get('DesignStd')) or text(p.get('CompatibleStandard'))
        line = (p.get('LineNumberTag') or '__NO_LINE__') if by_line else ''
        key = (name, decimal_value(p.get('NominalDiameter')).quantize(Decimal('.001')),
               text(p.get('NominalUnit')).lower(), text(p.get('Material')), standard,
               text(p.get('ItemCode')), text(p.get('Manufacturer')), line,
               text(p.get('PressureClass')), text(p.get('Facing')),
               standard,
               decimal_value(p.get('WallThickness')) if p.get('WallThickness') not in (None,'') else None,
               text(p.get('Schedule')))
        groups[key] = groups.get(key, Decimal(0)) + decimal_value(p['Length']) / Decimal(1000)
    return [{'Name': key[0], 'LineTag': key[7], 'Qty': float(length.quantize(Decimal('.1'),rounding=ROUND_CEILING))}
            for key, length in groups.items()]

pipes = [p for p in records if p['PnPClassName']=='Pipe']
length_cases = []
def add_case(label, selected, by_line=False, line=None, status=None, drawing=None):
    length_cases.append({'Label':label,'GroupByLine':by_line,'Line':line,'Status':status,'Drawing':drawing,
                         'SourceMetres':str(sum((decimal_value(p['Length'])/1000 for p in selected),Decimal(0))),
                         'Rows':pipe_groups(selected,by_line)})

add_case('All pipes',pipes)
add_case('All pipes grouped by line',pipes,True)
for line in sorted(set(p.get('LineNumberTag') or '__NO_LINE__' for p in pipes)):
    selected=[p for p in pipes if (p.get('LineNumberTag') or '__NO_LINE__')==line]
    add_case('Line '+line,selected,True,line=line)
    for status in sorted(set(p.get('Status') or '' for p in selected)):
        add_case('Line '+line+' / status '+status,[p for p in selected if (p.get('Status') or '')==status],
                 True,line=line,status=status)
for drawing in sorted(set(p['SourceDwgPath'] for p in pipes)):
    add_case(drawing,[p for p in pipes if p['SourceDwgPath']==drawing],drawing=drawing)

snapshot = {'Records': records, 'ExpectedPipes': expected, 'ExpectedBoltPieces': float(bolts),
            'RawPipeMetres': str(raw_length), 'ImperialLinearWeightPipes': imperial,
            'ExpectedPieceCounts': expected_pieces, 'LengthCases':length_cases}
(out / 'snapshot.json').write_text(json.dumps(snapshot, ensure_ascii=False), encoding='utf-8')
summary = {k: v for k, v in snapshot.items() if k not in ('Records','ExpectedPipes','LengthCases')}
summary['LengthCases'] = len(length_cases)
summary['EngineeringItems'] = len(records)
summary['Pipes'] = len(expected)
summary['BoltSets'] = sum(r['PnPClassName']=='BoltSet' for r in records)
summary['MultiDrawingRowIds'] = sum(len(v)>1 for v in links.values())
(out / 'source-summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(summary, ensure_ascii=True))
