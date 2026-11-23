"""Build a grouped 54-class corner dataset plus occlusion-aware synthetic hands.

User photos and Quest captures are deliberately never read by this training builder.
"""

import argparse
import hashlib
import json
import math
import random
import shutil
from collections import Counter, defaultdict
from pathlib import Path

import cv2
import numpy as np
import yaml
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
UPSTREAM = ROOT / "data" / "upstream_uno"
ART = ROOT / "unity/AICardTutor/Assets/_Project/Resources/UnoCards"


def read_labels(path):
    rows = []
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        values = line.split()
        if len(values) != 5:
            raise ValueError("Malformed label")
        cls, x, y, w, h = map(float, values)
        if cls != int(cls) or not all(math.isfinite(v) for v in (x,y,w,h)):
            raise ValueError("Invalid label")
        if not (0 <= x <= 1 and 0 <= y <= 1 and 0 < w <= 1 and 0 < h <= 1):
            raise ValueError("Out-of-bounds label")
        rows.append((int(cls), x, y, w, h))
    return rows


def write_sample(folder, split, name, image, labels):
    if isinstance(image, Path):
        shutil.copy2(image, folder/split/"images"/(name+image.suffix.lower()))
    else:
        cv2.imwrite(str(folder/split/"images"/(name+".jpg")), image, [cv2.IMWRITE_JPEG_QUALITY, 92])
    (folder/split/"labels"/(name+".txt")).write_text(
        "\n".join(f"{c} {x:.7f} {y:.7f} {w:.7f} {h:.7f}" for c,x,y,w,h in labels)+"\n", encoding="utf-8")


def texture_key(name):
    if name == "lcolor-40": return "WC"
    if name == "lcolor-400": return "W4"
    color = next(color for color in ("blue","green","red","yellow") if name.startswith("l"+color))
    suffix = name[1+len(color):]
    value = "R" if "revers" in suffix else "S" if "skip" in suffix else "A2" if suffix == "-20" else suffix.lstrip("-")
    return color[0].upper()+value


def glyph_corners(texture, name):
    masks=[]
    for lower in (False,True):
        canonical=cv2.rotate(texture,cv2.ROTATE_180) if lower else texture
        region=canonical[24:101,24:115]
        foreground=np.max(region[:,:,:3],axis=2)>100 if name.startswith("lcolor") else np.min(region[:,:,:3],axis=2)>215
        count,components,stats,_=cv2.connectedComponentsWithStats(np.uint8(foreground),8)
        selected=np.zeros(foreground.shape,np.uint8)
        for label in range(1,count):
            x,y,w,h,area=stats[label]
            if area>=5 and x>0 and y>0 and x+w<foreground.shape[1] and y+h<foreground.shape[0]:
                selected[components==label]=255
        if not np.any(selected):raise ValueError("Could not locate synthetic corner glyph: "+name)
        mask=np.zeros((360,240),np.uint8);mask[24:101,24:115]=selected
        if lower:mask=cv2.rotate(mask,cv2.ROTATE_180)
        ys,xs=np.where(mask>0)
        bbox=(max(0,int(xs.min())-3),max(0,int(ys.min())-3),min(240,int(xs.max())+4),min(360,int(ys.max())+4))
        masks.append((bbox,mask))
    return masks


def synthetic_hand(rng, textures, glyphs):
    width, height = 768, 576
    base = np.asarray(rng.integers(55,150,3), np.float32)
    noise = rng.normal(0, rng.uniform(4,15), (height,width,1))
    canvas = np.clip(base+noise,0,255).astype(np.uint8)
    # A few blurred desktop-like regions, with no unlabelled card glyphs in the background.
    for _ in range(5):
        a=(int(rng.integers(width)),int(rng.integers(height)))
        b=(int(rng.integers(width)),int(rng.integers(height)))
        cv2.rectangle(canvas,a,b,tuple(int(v) for v in rng.integers(20,200,3)),-1)
    canvas=cv2.GaussianBlur(canvas,(21,21),0)
    count=int(rng.integers(4,13))
    card_h=int(rng.integers(185,355)); card_w=int(card_h*2/3)
    spread=float(rng.uniform(45,105)); center_x=width/2+float(rng.uniform(-35,35))
    pivot=np.array([center_x,float(rng.uniform(370,500))])
    owner=np.zeros((height,width),np.int16)
    candidates=[]
    for i in range(count):
        cls=int(rng.integers(len(textures)))
        rgba=cv2.resize(textures[cls],(card_w,card_h),interpolation=cv2.INTER_AREA)
        fraction=i/(count-1)-.5
        angle=-fraction*spread+float(rng.uniform(-4,4))
        matrix=cv2.getRotationMatrix2D((card_w/2,card_h*.88),angle,1)
        matrix[0,2]+=pivot[0]-card_w/2+fraction*float(rng.uniform(45,100))
        matrix[1,2]+=pivot[1]-card_h*.88
        warped=cv2.warpAffine(rgba,matrix,(width,height),flags=cv2.INTER_LINEAR)
        alpha=warped[:,:,3:4]/255.0
        canvas=np.asarray(canvas*(1-alpha)+warped[:,:,:3]*alpha,np.uint8)
        owner[warped[:,:,3]>220]=i+1
        # Both visible pip corners are annotated, rather than labelling a hidden full card.
        for box,glyph in glyphs[cls]:
            x1,y1,x2,y2=box
            source=np.array([[x1/240*card_w,y1/360*card_h,1],
                             [x2/240*card_w,y1/360*card_h,1],
                             [x2/240*card_w,y2/360*card_h,1],
                             [x1/240*card_w,y2/360*card_h,1]])
            polygon=source@matrix.T
            local_mask=cv2.resize(glyph,(card_w,card_h),interpolation=cv2.INTER_NEAREST)
            ink=cv2.warpAffine(local_mask,matrix,(width,height),flags=cv2.INTER_NEAREST)
            candidates.append((cls,i+1,polygon,ink))
    # A hand-like occluder covers the lower part but never grants labels to hidden symbols.
    if rng.random()<.8:
        skin=tuple(int(v) for v in rng.integers([80,110,150],[130,180,235]))
        center=(int(pivot[0]+rng.uniform(-30,30)),int(pivot[1]+25))
        axes=(int(rng.integers(55,100)),110)
        cv2.ellipse(canvas,center,axes,0,0,360,skin,-1)
        cv2.ellipse(owner,center,axes,0,0,360,0,-1)
    labels=[]
    for cls,identity,polygon,ink in candidates:
        theoretical=np.count_nonzero(ink)
        visible=np.count_nonzero((ink>0)&(owner==identity))
        if theoretical<5 or visible/theoretical<.95: continue
        minimum=np.maximum(0,polygon.min(axis=0)); maximum=np.minimum([width,height],polygon.max(axis=0))
        if min(maximum-minimum)<8: continue
        x,y=(minimum+maximum)/2; w,h=maximum-minimum
        labels.append((cls,x/width,y/height,w/width,h/height))
    if rng.random()<.4:canvas=cv2.GaussianBlur(canvas,(3,3),float(rng.uniform(.3,1)))
    return canvas,labels


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--output",default="data/uno_hand_v1")
    parser.add_argument("--synthetic",type=int,default=800)
    parser.add_argument("--corner-crops",type=int,default=400)
    parser.add_argument("--refresh-synthetic",action="store_true")
    args=parser.parse_args()
    folder=(ROOT/args.output).resolve()
    if not folder.is_relative_to((ROOT/"data").resolve()):raise ValueError("Dataset must stay in data/")
    folder.mkdir(parents=True,exist_ok=True)
    if (folder/"manifest.json").exists() and not args.refresh_synthetic:
        print("Dataset already prepared:",folder);return
    if args.refresh_synthetic:
        config=yaml.safe_load((folder/"data.yaml").read_text())
        classes=config["names"]
        textures=[cv2.cvtColor(cv2.resize(np.array(Image.open(ART/(texture_key(name)+".png")).convert("RGBA")),(240,360)),cv2.COLOR_RGBA2BGRA) for name in classes]
        glyphs=[glyph_corners(texture,name) for texture,name in zip(textures,classes)]
        rng=np.random.default_rng(20261121)
        for index in range(args.synthetic):
            image,rows=synthetic_hand(rng,textures,glyphs)
            if rows:write_sample(folder,"train",f"fan_{index:05d}",image,rows)
        manifest=json.loads((folder/"manifest.json").read_text())
        manifest["synthetic_annotation"]="Glyph-tight boxes; at least 95% visible ink after all occlusions."
        (folder/"manifest.json").write_text(json.dumps(manifest,indent=2),encoding="utf-8")
        print("Refreshed synthetic hands with glyph visibility masks.");return
    for split in ("train","val","test"):
        for kind in ("images","labels"):(folder/split/kind).mkdir(parents=True,exist_ok=True)
    classes=yaml.safe_load((UPSTREAM/"UNO-DATASET--3/data.yaml").read_text())["names"]
    lookup={name:i for i,name in enumerate(classes)}
    samples=[]; seen=set(); skipped=Counter(); source_counts=Counter()
    for source_name in ("UNO-DATASET--3","New Dataset"):
        source=UPSTREAM/source_name
        source_names=yaml.safe_load((source/"data.yaml").read_text())["names"]
        remap={i:lookup[name] for i,name in enumerate(source_names) if name in lookup}
        for image in sorted(source.glob("*/images/*")):
            if image.suffix.lower() not in (".jpg",".jpeg",".png"):continue
            label=image.parent.parent/"labels"/(image.stem+".txt")
            if not label.exists():skipped["missing_label"]+=1;continue
            try:rows=read_labels(label)
            except (ValueError,OSError):skipped["invalid_label"]+=1;continue
            if not rows or any(row[0] not in remap for row in rows):
                skipped["unsupported_or_empty"]+=1;continue
            digest=hashlib.sha256(image.read_bytes()).hexdigest()
            if digest in seen:skipped["duplicate_bytes"]+=1;continue
            seen.add(digest)
            group=source_name+"/"+image.stem.split(".rf.")[0].lower()
            bucket=int(hashlib.sha256(group.encode()).hexdigest()[:8],16)%100
            split="train" if bucket<80 else "val" if bucket<90 else "test"
            mapped=[(remap[c],x,y,w,h) for c,x,y,w,h in rows]
            samples.append((image,mapped,group,split,digest))
            source_counts[source_name]+=1
    split_counts=Counter();coverage=defaultdict(Counter);train_samples=[];groups=defaultdict(set)
    for index,(image,rows,group,split,digest) in enumerate(samples):
        write_sample(folder,split,f"real_{index:05d}",image,rows)
        split_counts[split]+=1;coverage[split].update(row[0] for row in rows);groups[split].add(group)
        if split=="train":train_samples.append((image,rows))
    assert not (groups["train"]&groups["val"] or groups["train"]&groups["test"] or groups["val"]&groups["test"])

    rng=np.random.default_rng(20261121)
    textures=[]
    for name in classes:
        rgba=cv2.resize(np.array(Image.open(ART/(texture_key(name)+".png")).convert("RGBA")),(240,360))
        textures.append(cv2.cvtColor(rgba,cv2.COLOR_RGBA2BGRA))
    glyphs=[glyph_corners(texture,name) for texture,name in zip(textures,classes)]
    for index in range(args.synthetic):
        image,rows=synthetic_hand(rng,textures,glyphs)
        if rows:write_sample(folder,"train",f"fan_{index:05d}",image,rows)
    for index in range(args.corner_crops):
        source,rows=train_samples[int(rng.integers(len(train_samples)))]
        image=cv2.imread(str(source));height,width=image.shape[:2]
        cls,x,y,w,h=rows[int(rng.integers(len(rows)))]
        padding=float(rng.uniform(.7,2.0))
        x1=max(0,int((x-w*(.5+padding))*width));x2=min(width,int((x+w*(.5+padding))*width))
        y1=max(0,int((y-h*(.5+padding))*height));y2=min(height,int((y+h*(.5+padding))*height))
        if x2-x1<8 or y2-y1<8:continue
        labels=[]
        for c,cx,cy,bw,bh in rows:
            left=(cx-bw/2)*width;right=(cx+bw/2)*width;top=(cy-bh/2)*height;bottom=(cy+bh/2)*height
            if left>=x1 and right<=x2 and top>=y1 and bottom<=y2:
                labels.append((c,((left+right)/2-x1)/(x2-x1),((top+bottom)/2-y1)/(y2-y1),(right-left)/(x2-x1),(bottom-top)/(y2-y1)))
        if labels:write_sample(folder,"train",f"corner_{index:05d}",image[y1:y2,x1:x2],labels)
    config={"path":str(folder),"train":"train/images","val":"val/images","test":"test/images","nc":54,"names":classes}
    (folder/"data.yaml").write_text(yaml.safe_dump(config,sort_keys=False),encoding="utf-8")
    counts={split:len(list((folder/split/"images").iterdir())) for split in ("train","val","test")}
    manifest={"sources":dict(source_counts),"upstream":"https://github.com/Stormynova/Uno_Card_detection","upstream_commit":"538bc836fbd56693f3d73e2a7f17c9a85df07734",
        "licenses":{"UNO-DATASET--3":"CC BY 4.0","New Dataset":"Public Domain","synthetic_faces":"CC0-1.0; see Unity UnoCards/Source.json"},
        "excluded_uno_flip_classes":[name for name in source_names if name not in lookup],
        "split_counts":counts,"real_split_counts":dict(split_counts),"class_coverage":{s:len(c) for s,c in coverage.items()},"skipped":dict(skipped),
        "held_out_user_images":True,"split_policy":"Grouped by original filename before Roboflow augmentation; exact byte duplicates removed.",
        "samples":[{"source":str(p.relative_to(UPSTREAM)),"split":s,"group":g,"sha256":d} for p,_,g,s,d in samples]}
    (folder/"manifest.json").write_text(json.dumps(manifest,indent=2),encoding="utf-8")
    credits=folder/"source_credits";credits.mkdir(exist_ok=True)
    for source_name in ("UNO-DATASET--3","New Dataset"):
        for name in ("README.dataset.txt","README.roboflow.txt"):
            shutil.copy2(UPSTREAM/source_name/name,credits/(source_name.replace(" ","_")+"_"+name))
    shutil.copy2(ART/"LICENSE.txt",credits/"synthetic_card_art_CC0.txt")
    shutil.copy2(ART/"Source.json",credits/"synthetic_card_art_source.json")
    print(json.dumps({key:manifest[key] for key in ("sources","split_counts","real_split_counts","class_coverage","skipped","excluded_uno_flip_classes")},indent=2))


if __name__ == "__main__":
    cv2.setNumThreads(1)
    main()
