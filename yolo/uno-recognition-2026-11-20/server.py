import logging
import json
import os
import sys
from contextlib import asynccontextmanager
from pathlib import Path
from threading import Lock
from time import perf_counter, time_ns
from typing import Annotated, Literal

import cv2
import numpy as np
import torch
from fastapi import FastAPI, File, Form, Header, HTTPException, UploadFile
from ultralytics import YOLO


# Packaged copy: the default root is this folder, so the unzip location decides it.
YOLO_ROOT = Path(
    os.environ.get("UNO_YOLO_DIR", str(Path(__file__).resolve().parent))
).resolve()
# Packaged copy: the model can be picked from the environment. The original handoff
# copy hard-coded models/best.pt and ignored UNO_MODEL.
MODEL_PATH = Path(
    os.environ.get("UNO_MODEL", str(YOLO_ROOT / "models" / "best.pt"))
).resolve()

if not (YOLO_ROOT / "detect_v2.py").is_file():
    raise RuntimeError("detect_v2.py was not found. Check UNO_YOLO_DIR.")

sys.path.insert(0, str(YOLO_ROOT))
import detect_v2 as detector
from hand_detector import detect_hand


logger = logging.getLogger("uvicorn.error")
inference_lock = Lock()


# ---------------------------------------------------------------- defaults
# The weight only fires when an object lands in a narrow absolute pixel window
# (roughly 40-85 px at any given imgsz). A single imgsz therefore trades the near
# range against the far one. Running a few scales and merging the boxes covers
# both ends; see detect_v2.detect_multiscale.
def _scales_from_env() -> tuple[int, ...]:
    raw = os.environ.get("UNO_SCALES", "256,416,512,640")
    try:
        parsed = tuple(int(part) for part in raw.replace(" ", "").split(",") if part)
    except ValueError:
        logger.warning("UNO_SCALES=%r is not a list of integers; falling back.", raw)
        return (640,)
    return parsed or (640,)


DEFAULT_SCALES = _scales_from_env()
DEFAULT_CONF = float(os.environ.get("UNO_CONF", "0.25"))
DEFAULT_DEDUPE = os.environ.get("UNO_DEDUPE", "1") != "0"
HAND_ENHANCE = os.environ.get("UNO_HAND_ENHANCE", "0") == "1"
CAPTURE_DIR = Path(os.environ["UNO_CAPTURE_DIR"]).resolve() if os.environ.get("UNO_CAPTURE_DIR") else None
CAPTURE_MAX = max(0, int(os.environ.get("UNO_CAPTURE_MAX", "150")))
capture_lock = Lock()
capture_seen = 0
capture_saved = 0


def capture_test_frame(data, payload):
    """Opt-in, bounded local captures. No keys, AI prompts or external uploads."""
    global capture_seen, capture_saved
    if CAPTURE_DIR is None or payload["scan_target"] != "hand":
        return
    with capture_lock:
        capture_seen += 1
        if capture_saved >= CAPTURE_MAX or (capture_seen-1) % 3:
            return
        capture_saved += 1
        stem = f"hand_{time_ns()}_frame{payload['frame_id']}"
    try:
        CAPTURE_DIR.mkdir(parents=True, exist_ok=True)
        (CAPTURE_DIR / f"{stem}.jpg").write_bytes(data)
        (CAPTURE_DIR / f"{stem}.json").write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    except OSError:
        logger.warning("Could not save a local recognition test frame.")

# 尺度是实测选的，不是凑的（2026-11-04，samples/ 下 6 张真实照片）：
#   256 补近处/大牌，512 是 blue 6 认成 0.89 的那一档，640 覆盖手臂距离。
#   416 单独用会把 blue 6 拉成 blue 3 0.57（低尺度分类头更弱），但放进并集里它只贡献
#   额外框、不抢高置信度的位置，所以留着 —— 实测多它一张、yellow 7 从 0.60 涨到 0.79。
#   四个是上限：再多，1280x960 一帧就要超过 100 ms，客户端 300 ms 的节奏开始吃力。
WARMUP_SIZE = (1280, 960)      # Quest passthrough 的实际出帧比例


def warm_up(model, device, scales=DEFAULT_SCALES):
    """把每个尺度的第一次推理提前跑掉。

    不预热的话，进程起来后第一个请求要 3 秒左右（cudnn 为新输入尺寸做 plan），
    正好把客户端一轮 8 秒的预算吃掉小半。
    """
    frame = np.full((WARMUP_SIZE[1], WARMUP_SIZE[0], 3), 127, dtype=np.uint8)
    started = perf_counter()
    for scale in scales:
        try:
            model.predict(frame, imgsz=scale, conf=DEFAULT_CONF, device=device, verbose=False)
        except Exception:
            logger.exception("Warm-up failed at imgsz=%s; continuing.", scale)
    logger.info("Warm-up %s at %sx%s: %.0f ms",
                list(scales), WARMUP_SIZE[0], WARMUP_SIZE[1], (perf_counter() - started) * 1000)


@asynccontextmanager
async def lifespan(app: FastAPI):
    if not MODEL_PATH.is_file():
        raise RuntimeError("The selected model file was not found. Copy the shared weights into models/ or set UNO_MODEL.")
    app.state.device = "0" if torch.cuda.is_available() else "cpu"
    app.state.model = YOLO(str(MODEL_PATH))
    logger.info("Model loaded once. Device: %s", app.state.device)
    logger.info("Default scales: %s, conf: %s, dedupe: %s",
                list(DEFAULT_SCALES), DEFAULT_CONF, DEFAULT_DEDUPE)
    warm_up(app.state.model, app.state.device)
    yield
    app.state.model = None


app = FastAPI(title="UNO Recognition Service", lifespan=lifespan)


def require_model():
    model = getattr(app.state, "model", None)
    if model is None:
        raise HTTPException(status_code=503, detail="Model is not ready.")
    return model


def resolve_scales(imgsz: int | None, scales: str | None) -> tuple[int, ...]:
    """Per-request override, then the env default.

    imgsz=640 keeps the old single-scale behaviour and is the cheapest request;
    scales="256,416,640" asks for the union explicitly.
    """
    if imgsz:
        if imgsz < 64 or imgsz > 4096:
            raise HTTPException(status_code=422, detail="imgsz out of range.")
        return (int(imgsz),)

    if scales:
        try:
            parsed = tuple(int(part) for part in scales.replace(" ", "").split(",") if part)
        except ValueError:
            raise HTTPException(status_code=422, detail="scales must be a list of integers.")
        if not parsed:
            raise HTTPException(status_code=422, detail="scales is empty.")
        if any(v < 64 or v > 4096 for v in parsed):
            raise HTTPException(status_code=422, detail="scales out of range.")
        if len(parsed) > 4:
            raise HTTPException(status_code=422, detail="At most four scales per request.")
        return parsed

    return DEFAULT_SCALES


@app.get("/health")
def health():
    require_model()
    return {
        "status": "ready",
        "scales": list(DEFAULT_SCALES),
        "conf": DEFAULT_CONF,
        "dedupe": DEFAULT_DEDUPE,
        "loose_pairing": detector.LOOSE_PAIRING,
        "hand_enhance": HAND_ENHANCE,
    }


@app.get("/config")
def config():
    """What this process will use when a request does not say otherwise."""
    return {
        "default_scales": list(DEFAULT_SCALES),
        "default_conf": DEFAULT_CONF,
        "default_dedupe": DEFAULT_DEDUPE,
        "loose_pairing": detector.LOOSE_PAIRING,
        "model": str(MODEL_PATH),
        "hand_enhance": HAND_ENHANCE,
        "capture_dir": str(CAPTURE_DIR) if CAPTURE_DIR else None,
    }


@app.post("/detect")
def detect_image(
    file: Annotated[UploadFile, File()],
    x_frame_id: Annotated[int, Header(alias="X-Frame-Id", ge=0)],
    imgsz: Annotated[int | None, Form()] = None,
    scales: Annotated[str | None, Form()] = None,
    conf: Annotated[float | None, Form()] = None,
    dedupe: Annotated[bool | None, Form()] = None,
    scan_target: Annotated[Literal["hand", "discard"] | None, Form()] = None,
):
    model = require_model()
    try:
        data = file.file.read()
    finally:
        file.file.close()

    if not data:
        raise HTTPException(status_code=400, detail="The uploaded file is empty.")

    decode_started = perf_counter()
    try:
        frame = cv2.imdecode(np.frombuffer(data, dtype=np.uint8), cv2.IMREAD_COLOR)
    except cv2.error:
        frame = None
    if frame is None:
        raise HTTPException(status_code=400, detail="Cannot decode the image.")
    decode_ms = (perf_counter()-decode_started)*1000

    resolved = resolve_scales(imgsz, scales)
    use_conf = DEFAULT_CONF if conf is None else float(conf)
    use_dedupe = DEFAULT_DEDUPE if dedupe is None else bool(dedupe)

    if not 0.0 < use_conf < 1.0:
        raise HTTPException(status_code=422, detail="conf must be between 0 and 1.")

    # Serialize access to the shared model; callers should keep one request in flight.
    with inference_lock:
        started = perf_counter()
        metadata = {"profile": "legacy", "views": 1}
        try:
            if HAND_ENHANCE and scan_target == "hand" and use_dedupe:
                results, metadata = detect_hand(model, frame, resolved, use_conf, app.state.device)
            elif len(resolved) == 1:
                results = detector.detect(
                    model,
                    frame,
                    imgsz=resolved[0],
                    conf=use_conf,
                    device=app.state.device,
                    dedupe=use_dedupe,
                )
            else:
                results = detector.detect_multiscale(
                    model,
                    frame,
                    scales=resolved,
                    conf=use_conf,
                    device=app.state.device,
                    dedupe=use_dedupe,
                )
        except Exception as exc:
            logger.exception("Inference failed for frame %s.", x_frame_id)
            raise HTTPException(
                status_code=500, detail="Inference failed. Check the server log."
            ) from exc
        elapsed_ms = (perf_counter() - started) * 1000

    payload = {
        "frame_id": x_frame_id,
        "image_width": int(frame.shape[1]),
        "image_height": int(frame.shape[0]),
        "inference_ms": round(elapsed_ms, 2),
        "decode_ms": round(decode_ms, 2),
        "scan_target": scan_target,
        **metadata,
        "scales": list(resolved),
        "loose_pairing": detector.LOOSE_PAIRING,
        "detections": [
            {
                "id": index,
                "raw_label": item["raw"],
                "label": item["pretty"].title(),
                "confidence": float(item["conf"]),
                "bbox_xyxy": list(item["xyxy"]),
            }
            for index, item in enumerate(results)
        ],
    }
    capture_test_frame(data, payload)
    return payload
