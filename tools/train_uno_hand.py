"""Local-only fine tuning. Writes a candidate without replacing the deployed model."""

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / "yolo/uno-recognition-2026-11-20"
CONFIG = ROOT / ".debug/training/config"
CONFIG.mkdir(parents=True, exist_ok=True)
os.environ["YOLO_CONFIG_DIR"] = str(CONFIG)
os.environ["MPLCONFIGDIR"] = str(CONFIG/"matplotlib")
os.environ["WANDB_MODE"] = "disabled"
os.environ["OMP_NUM_THREADS"] = "2"
os.environ["MKL_NUM_THREADS"] = "2"
font = Path("C:/Windows/Fonts/arial.ttf")
if font.exists() and not (CONFIG/"Arial.ttf").exists(): shutil.copy2(font, CONFIG/"Arial.ttf")


def main():
    import torch
    import yaml
    from ultralytics import YOLO, settings
    settings.update({key:False for key in ("sync","hub","wandb","mlflow","clearml","comet","neptune","tensorboard","raytune","dvc") if key in settings})
    parser=argparse.ArgumentParser()
    parser.add_argument("--epochs",type=int,default=18)
    parser.add_argument("--batch",type=int,default=12)
    parser.add_argument("--resume")
    args=parser.parse_args()
    status_file=ROOT/".debug/training/status.json"
    def write_status(**value):
        value["updated_utc"]=datetime.now(timezone.utc).isoformat()
        temp=status_file.with_suffix(".tmp")
        temp.write_text(json.dumps(value,indent=2),encoding="utf-8")
        temp.replace(status_file)
    if not torch.cuda.is_available():raise RuntimeError("CUDA is required for this training run.")
    torch.set_num_threads(2)
    model=YOLO(args.resume or str(PACKAGE/"models/best.pt"))
    config=ROOT/"data/uno_hand_v1/data.yaml"
    expected=yaml.safe_load(config.read_text(encoding="utf-8"))["names"]
    if [model.names[i] for i in range(len(model.names))] != expected:
        raise RuntimeError("Training class order does not match the deployed model.")
    def progress(trainer):
        metrics={str(k):float(v) for k,v in trainer.metrics.items()}
        write_status(state="training",epoch=trainer.epoch+1,epochs=trainer.epochs,
                     metrics=metrics,run_dir=str(trainer.save_dir))
    model.add_callback("on_fit_epoch_end",progress)
    write_status(state="starting",device=torch.cuda.get_device_name(0),epochs=args.epochs)
    try:
        if args.resume:
            model.train(resume=True)
        else:
            model.train(
                data=str(config),epochs=args.epochs,imgsz=640,batch=args.batch,
                device=0,workers=0,cache=False,amp=True,optimizer="AdamW",
                lr0=0.0002,lrf=0.1,weight_decay=0.0005,warmup_epochs=1,warmup_bias_lr=0.0,
                cos_lr=True,patience=6,seed=20261121,deterministic=True,
                hsv_h=0.0,hsv_s=0.25,hsv_v=0.30,degrees=75,translate=0.10,
                scale=0.5,shear=2.0,perspective=0.0001,fliplr=0.0,flipud=0.0,
                mosaic=0.5,mixup=0.0,close_mosaic=4,
                plots=False,save=True,save_period=3,
                project=str(ROOT/"training_runs"),name="uno_hand_corner_v1",
                exist_ok=False,verbose=False,
            )
        best=model.trainer.save_dir/"weights/best.pt"
        candidate=PACKAGE/"models/best_hand_candidate_20261121.pt"
        shutil.copy2(best,candidate)
        write_status(state="complete",run_dir=str(model.trainer.save_dir),candidate=str(candidate),
                     metrics={str(k):float(v) for k,v in model.trainer.metrics.items()})
        print("Candidate saved:",candidate,flush=True)
    except BaseException as error:
        write_status(state="failed",error_type=type(error).__name__,message=str(error)[:1600])
        raise


if __name__ == "__main__":
    main()
