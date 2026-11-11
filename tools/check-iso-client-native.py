import json,math,sys,pathlib,glob
root=pathlib.Path(sys.argv[1]);scenes=sorted(glob.glob(sys.argv[2]));count=0;tables=0;z_count=0
for index,scene_file in enumerate(scenes,1):
 scene=json.load(open(scene_file,encoding='utf-8-sig'))
 report=json.load(open(root/f'sheet-{index:02d}/report.json',encoding='utf-8-sig'))
 model=next(b for b in report['blocks'] if b['Name']=='*Model_Space')
 dims=[e for e in model['entities'] if e['type']=='AlignedDimension' and e.get('sourceLink')]
 assert len(dims)==len(scene['Dimensions']),(index,'dimension count')
 for entity,planned in zip(dims,scene['Dimensions']):
  d=entity['details'];a=d['anchors']['start'];b=d['anchors']['end'];line=d['anchors']['dimensionLine'];text=d['textPosition']
  dx,dy=b[0]-a[0],b[1]-a[1];length=math.hypot(dx,dy);u=(dx/length,dy/length);n=(-u[1],u[0])
  angle=math.atan2(dy,dx)+d['anchors']['Oblique'];ex=(math.cos(angle),math.sin(angle));expected=planned['ExtensionDirection'];expected=(expected['X'],expected['Y'])
  assert abs(abs(ex[0]*expected[0]+ex[1]*expected[1])-1)<1e-7,(index,'plane direction')
  if abs(expected[0])<1e-7:
   assert abs(ex[0])<1e-7,(index,'Z extensions not vertical');z_count+=1
  mid=((a[0]+b[0])/2,(a[1]+b[1])/2);t=((line[0]-mid[0])*n[0]+(line[1]-mid[1])*n[1])/(ex[0]*n[0]+ex[1]*n[1]);base=(mid[0]+ex[0]*t,mid[1]+ex[1]*t)
  if dx<-.0001 or abs(dx)<.0001 and dy<0:n=(-n[0],-n[1])
  assert abs(text[0]-base[0]-n[0]*2.8)<1e-5 and abs(text[1]-base[1]-n[1]*2.8)<1e-5,(index,'dimension text not centered above baseline')
  assert d['DimensionText']==str(math.ceil(planned['Millimeters']-1e-8)),(index,'engineering value changed')
  count+=1
 table=next(e for e in model['entities'] if e['type']=='Table')['details']
 assert all((not text and h==-1) or abs(h-3)<1e-8 for texts,heights in zip(table['rows'],table['textHeights']) for text,h in zip(texts,heights)),(index,'BOM text height is not 3 mm')
 native_rows=[r for r in table['rows'][2:] if r[0].strip()]
 assert len(native_rows)==len(scene['BillOfMaterials']),(index,'BOM rows lost')
 for native,planned in zip(native_rows,scene['BillOfMaterials']):
  assert native[0]==str(planned['Position']) and native[2]==planned['Description'],(index,'BOM order mismatch')
  if planned['Unit']=='м':
   expected_qty=f"{max(.1,math.ceil(planned['Quantity']*10-1e-9)/10):.1f} м" if planned['Quantity']>0 else '0.0 м'
   assert native[3]==expected_qty,(index,'BOM meter formatting',native[3],expected_qty)
 tables+=1
print(json.dumps({'sheets':len(scenes),'native_aligned_dimensions':count,'vertical_Z_extensions':z_count,'validated_BOM_tables':tables,'BOM_text_height_mm':3,'dimension_text_centering':'passed','engineering_dimensions':'unchanged'},indent=2))
