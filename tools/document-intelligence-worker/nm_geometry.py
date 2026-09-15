"""Local-only, geometry-first helper for the NM pilot.  It never reads OCR text."""
from dataclasses import dataclass
import cv2
import numpy as np

@dataclass(frozen=True)
class Region:
    x: int; y: int; width: int; height: int

def rotate_clockwise(image):
    return cv2.rotate(image, cv2.ROTATE_90_CLOCKWISE)

def original_to_normalized(x, y, original_width, original_height):
    return original_height - y, x

def normalized_to_original(x, y, original_width, original_height):
    return y, original_height - x

def _runs(values, gap=10):
    result=[]
    for value in values:
        if not result or value-result[-1][-1]>gap: result.append([value])
        else: result[-1].append(value)
    return [round(sum(run)/len(run)) for run in result]

def _logical(fragments, axis_tolerance, minimum_span):
    groups=[]
    for position,start,end in sorted(fragments):
        target=next((g for g in groups if abs(g[0]-position)<=axis_tolerance),None)
        if target is None: groups.append([position,start,end,1])
        else: target[0]=(target[0]*target[3]+position)/(target[3]+1);target[1]=min(target[1],start);target[2]=max(target[2],end);target[3]+=1
    return [round(g[0]) for g in groups if g[2]-g[1]>=minimum_span]

def detect(image):
    h,w=image.shape[:2]; gray=cv2.cvtColor(image,cv2.COLOR_BGR2GRAY)
    binary=cv2.threshold(gray,180,255,cv2.THRESH_BINARY_INV)[1]
    raw=cv2.HoughLinesP(binary,1,np.pi/180,70,minLineLength=max(80,w//12),maxLineGap=max(8,w//100))
    if raw is None:return None
    vertical=[];horizontal=[]
    for x1,y1,x2,y2 in raw[:,0]:
        if abs(x2-x1)<=max(5,w*.012): vertical.append(((x1+x2)/2,min(y1,y2),max(y1,y2)))
        elif abs(y2-y1)<=max(5,h*.012): horizontal.append(((y1+y2)/2,min(x1,x2),max(x1,x2)))
    xs=_logical(vertical,max(8,w*.018),h*.20);ys=_logical(horizontal,max(8,h*.018),w*.20)
    if len(xs)<3 or len(ys)<2:return None
    return sorted(xs),sorted(ys)

def regions(image):
    found=detect(image)
    if not found:return [],[],None
    xs,ys=found; rows=[Region(xs[0],ys[i],xs[-1]-xs[0],ys[i+1]-ys[i]) for i in range(len(ys)-1) if ys[i+1]-ys[i]>18]
    # Fixed printed column roles, derived from detected separators; absent narrow columns stay unused.
    roles=["serial","recordedPerson","khasra","areaShare","landCompensation","structureCompensation","entitlement"]
    fields=[]
    for row in rows:
        for i in range(min(len(roles),len(xs)-1)):
            fields.append((roles[i],Region(xs[i],row.y,xs[i+1]-xs[i],row.height)))
    return rows,fields,(xs,ys)

def overlay(image):
    rows,fields,lines=regions(image); out=image.copy()
    if lines:
        xs,ys=lines
        for x in xs:cv2.line(out,(x,ys[0]),(x,ys[-1]),(0,255,0),3)
        for y in ys:cv2.line(out,(xs[0],y),(xs[-1],y),(255,0,0),3)
    return out,rows,fields

# Template coordinates are ratios of the rotated page.  They describe the printed
# government form, never a document-specific handwritten value.
NM_TEMPLATE_SLOTS=((.06,.14,.88,.25),(.06,.39,.88,.25),(.06,.64,.88,.25))
NM_FIELDS=(("serial",.00,.00,.05,1),("recordedPerson",.05,.00,.35,1),("khasra",.40,.00,.12,1),("areaShare",.52,.00,.10,1),("landCompensation",.62,.00,.12,1),("structureCompensation",.74,.00,.10,1),("entitlement",.84,.00,.16,1))
def template_regions(shape):
    h,w=shape[:2]; slots=[Region(round(x*w),round(y*h),round(a*w),round(b*h)) for x,y,a,b in NM_TEMPLATE_SLOTS]
    fields=[(role,Region(s.x+round(x*s.width),s.y+round(y*s.height),round(a*s.width),round(b*s.height))) for s in slots for role,x,y,a,b in NM_FIELDS]
    return slots,fields

def register(reference, page):
    ref=cv2.cvtColor(reference,cv2.COLOR_BGR2GRAY); img=cv2.cvtColor(page,cv2.COLOR_BGR2GRAY)
    try:
        warp=np.eye(2,3,dtype=np.float32)
        score,warp=cv2.findTransformECC(ref,img,warp,cv2.MOTION_AFFINE,(cv2.TERM_CRITERIA_EPS|cv2.TERM_CRITERIA_COUNT,100,.0001))
        if score<.35 or abs(warp[0,0]-1)>.25 or abs(warp[1,1]-1)>.25:return None,score
        return warp,float(score)
    except cv2.error:return None,0.0
