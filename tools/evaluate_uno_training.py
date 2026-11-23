"""Compare the frozen deployment baseline with a separately trained candidate."""

from collections import Counter
import gc
import json
from pathlib import Path
import sys
import time
import statistics

import train_uno_hand  # configure local caches before importing Ultralytics

ROOT=Path(__file__).resolve().parents[1]
PACKAGE=ROOT/"yolo/uno-recognition-2026-11-20"
sys.path.insert(0,str(PACKAGE))


def canonical(value):
    return value.casefold().replace("draw two","draw 2")


def main():
    import cv2
    import torch
    from ultralytics import YOLO, settings
    import detect_v2 as detector
    from hand_detector import detect_hand
    settings.update({key:False for key in ("sync","hub","wandb","mlflow","clearml","comet","neptune","tensorboard","raytune","dvc") if key in settings})
    output=ROOT/"output/trained_hand_model";output.mkdir(parents=True,exist_ok=True)
    phone_path=ROOT/".debug/recognition_eval/hand8_phone.jpg"
    phone=cv2.imread(str(phone_path))
    reference=json.loads((ROOT/"output/recognition_trial/hand8_results.json").read_text())
    expected=Counter(canonical(v) for v in reference["expected"])
    quest=[]
    for path in sorted((ROOT/".debug/recognition_frames").glob("*.json")):
        info=json.loads(path.read_text())
        if len(info.get("detections",[]))>=6:quest.append(path)
    quest=quest[::max(1,len(quest)//3)][:3]
    report={"phone_expected":list(reference["expected"]),"source_image_used_in_training":False,
            "public_test_warning":"Some public images may have been seen during the original baseline model's historical training. These metrics alone are not proof of new-domain generalization.",
            "phone_metric":"Multiset label-instance matches on one phone photo; not mAP and not proof of correct ownership/localization.",
            "quest_metric":"Qualitative only: no independently annotated frame-level hand ground truth.","models":{}}
    models={"baseline":PACKAGE/"models/best.pt","candidate":PACKAGE/"models/best_hand_candidate_20261121.pt"}
    for tag,path in models.items():
        model=YOLO(str(path));entry={"path":str(path),"phone":{},"quest":[]}
        for name,call in [
            ("single640",lambda:detector.detect(model,phone,640,.25,'0')),
            ("legacy_multiscale",lambda:detector.detect_multiscale(model,phone,device='0')),
            ("enhanced",lambda:detect_hand(model,phone,(256,416,512,640),.25,'0')[0]),
        ]:
            call()
            timings=[]
            for _ in range(5):
                torch.cuda.synchronize();start=time.perf_counter();detections=call();torch.cuda.synchronize()
                timings.append((time.perf_counter()-start)*1000)
            actual=Counter(canonical(item['pretty']) for item in detections)
            matched=sum((actual&expected).values())
            entry["phone"][name]={"count":len(detections),"matched_instances":matched,
                "missing":list((expected-actual).elements()),"extra":list((actual-expected).elements()),
                "all_labels_exact":actual==expected,"inference_ms":round(statistics.median(timings),2),
                "timing_policy":"Median of 5 warmed GPU calls; no transport/JPEG decode/Unity voting.","detections":detections}
            annotated=phone.copy()
            for item in detections:
                x1,y1,x2,y2=map(int,item['xyxy']);cv2.rectangle(annotated,(x1,y1),(x2,y2),(0,220,0),3)
                cv2.putText(annotated,f"{item['pretty']} {item['conf']:.2f}",(x1,max(20,y1-8)),cv2.FONT_HERSHEY_SIMPLEX,.8,(0,180,0),2)
            cv2.imwrite(str(output/f"phone_{tag}_{name}.jpg"),annotated)
            print(tag,name,'matched',matched,'of 8; extra',list((actual-expected).elements()),flush=True)
        for number,reference_path in enumerate(quest):
            image=cv2.imread(str(reference_path.with_suffix('.jpg')))
            for profile,call in [
                ('legacy_multiscale',lambda:detector.detect_multiscale(model,image,device='0')),
                ('enhanced',lambda:detect_hand(model,image,(256,416,512,640),.25,'0')[0]),
            ]:
                detections=call()
                annotated=image.copy()
                for item in detections:
                    x1,y1,x2,y2=map(int,item['xyxy']);cv2.rectangle(annotated,(x1,y1),(x2,y2),(0,220,0),2)
                    cv2.putText(annotated,item['pretty'],(x1,max(16,y1-5)),cv2.FONT_HERSHEY_SIMPLEX,.65,(0,180,0),2)
                destination=output/f"quest_{number}_{tag}_{profile}.jpg";cv2.imwrite(str(destination),annotated)
                entry['quest'].append({'source':str(reference_path.with_suffix('.jpg')),'profile':profile,
                                       'detections':detections,'preview':str(destination)})
        metrics=model.val(data=str(ROOT/'data/uno_hand_v1/data.yaml'),split='test',imgsz=640,batch=8,workers=0,
                          device=0,plots=False,save_json=False,verbose=False,project=str(output),name=tag+'_public_test',exist_ok=True)
        entry['public_test']={'precision':float(metrics.box.mp),'recall':float(metrics.box.mr),'map50':float(metrics.box.map50),
                              'map50_95':float(metrics.box.map),'classes_with_ground_truth':len(metrics.box.ap_class_index)}
        report['models'][tag]=entry
        (output/'comparison.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
        del model;gc.collect();torch.cuda.empty_cache()
    print('Comparison saved:',output/'comparison.json',flush=True)


if __name__ == '__main__':
    main()
