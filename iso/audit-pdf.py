"""Check actual plotted text quads in the diagram area, after AutoCAD's font/layout engine."""
import json, sys, math
from pathlib import Path
import pymupdf

def overlaps(a,b):
    for poly in (a,b):
        for i,p in enumerate(poly):
            q=poly[(i+1)%len(poly)]; axis=(-(q[1]-p[1]),q[0]-p[0]); length=math.hypot(*axis)
            if length<1e-8: continue
            pa=[(x*axis[0]+y*axis[1])/length for x,y in a];pb=[(x*axis[0]+y*axis[1])/length for x,y in b]
            if min(max(pa),max(pb))-max(min(pa),min(pb))<.5:return False
    return True

def hit(a,b,poly):
    center=(sum(x for x,y in poly)/4,sum(y for x,y in poly)/4)
    p=[(center[0]+(x-center[0])*.75,center[1]+(y-center[1])*.75) for x,y in poly]
    area=sum(p[i][0]*p[(i+1)%4][1]-p[(i+1)%4][0]*p[i][1] for i in range(4));sign=1 if area>0 else -1
    lo,hi=0.,1.
    for i,u in enumerate(p):
        v=p[(i+1)%4];ex,ey=v[0]-u[0],v[1]-u[1]
        r=sign*(ex*(a[1]-u[1])-ey*(a[0]-u[0]));q=sign*(ex*(b[1]-a[1])-ey*(b[0]-a[0]))
        if abs(q)<1e-9:
            if r<0:return False
        elif q>0:lo=max(lo,-r/q)
        else:hi=min(hi,-r/q)
        if lo>hi:return False
    return (hi-lo)*math.dist(a,b)>1.2


pdf=Path(sys.argv[1]);document=pymupdf.open(pdf);items=[]
for block in document[0].get_text('dict')['blocks']:
    for line in block.get('lines',[]):
        for span in line['spans']:
            text=span['text'].strip()
            if not text or not all(c in '0123456789.,*' for c in text):continue
            quad=pymupdf.recover_quad(line['dir'],span)
            polygon=[tuple(p) for p in (quad.ul,quad.ur,quad.lr,quad.ll)]
            if min(p[0] for p in polygon)>1100 or min(p[1] for p in polygon)<140:continue
            items.append({'text':text,'quad':polygon})
collisions=[]
for i,a in enumerate(items):
    for b in items[i+1:]:
        if overlaps(a['quad'],b['quad']):collisions.append({'first':a,'second':b})
segments=[]
for path in document[0].get_drawings():
    if path['type'] not in ('s','fs'):continue
    for item in path['items']:
        if item[0]=='l':segments.append((tuple(item[1]),tuple(item[2])))
line_collisions=[{'text':item['text'],'quad':item['quad']} for item in items if any(hit(a,b,item['quad']) for a,b in segments)]
result={'pdf':str(pdf),'checkedTextSpans':len(items),'textCollisions':collisions,'textLineCollisions':line_collisions}
output=pdf.with_suffix('.text-audit.json');output.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'checked':len(items),'overlaps':len(collisions),'lineOverlaps':len(line_collisions),'pairs':[(c['first']['text'],c['second']['text']) for c in collisions]},ensure_ascii=False))
if collisions or line_collisions:sys.exit(2)

