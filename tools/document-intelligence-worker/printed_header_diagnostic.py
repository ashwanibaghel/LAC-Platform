import cv2, glob, json, os

def normalized(path):
    return cv2.rotate(cv2.imread(path), cv2.ROTATE_90_CLOCKWISE)

def header(image):
    h,w=image.shape[:2]
    return image[0:int(h*.30), 0:w]

ref_path='tmp/nm-pilot/page-10.png'
reference=header(normalized(ref_path))
orb=cv2.ORB_create(3000)
rk,rd=orb.detectAndCompute(cv2.cvtColor(reference,cv2.COLOR_BGR2GRAY),None)
matcher=cv2.BFMatcher(cv2.NORM_HAMMING)
paths=['tmp/nm-pilot/page-01.png','tmp/nm-pilot/page-10.png','tmp/nm-pilot/page-20.png']+sorted(glob.glob('tmp/nm-pilot/spread-*.png'))
results=[]
for path in paths:
    target=header(normalized(path)); tk,td=orb.detectAndCompute(cv2.cvtColor(target,cv2.COLOR_BGR2GRAY),None)
    raw=[] if td is None else matcher.knnMatch(rd,td,k=2)
    good=[a for a,b in raw if a.distance < .72*b.distance]
    inliers=0;error=None;transform=None
    if len(good)>=5:
        src=cv2.KeyPoint_convert(rk)[[m.queryIdx for m in good]]; dst=cv2.KeyPoint_convert(tk)[[m.trainIdx for m in good]]
        transform,mask=cv2.estimateAffinePartial2D(src,dst,method=cv2.RANSAC,ransacReprojThreshold=4)
        if mask is not None:
            inliers=int(mask.sum()); pred=cv2.transform(src.reshape(1,-1,2),transform).reshape(-1,2); error=float(((pred[mask.ravel()>0]-dst[mask.ravel()>0])**2).sum(axis=1).mean()**.5)
    ratio=inliers/len(good) if good else 0
    state='Registered' if inliers>=8 and ratio>=.45 and error is not None and error<=4 else 'NeedsManualAlignment'
    results.append(dict(page=os.path.basename(path),rawMatches=len(raw),accepted=len(good),inliers=inliers,inlierRatio=round(ratio,3),reprojectionError=None if error is None else round(error,2),state=state))
print(json.dumps(results,indent=2))
