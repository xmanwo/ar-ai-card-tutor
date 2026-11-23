"""Local GPU smoke check of the candidate through the existing HTTP contract."""

import json
import os
from pathlib import Path
import sys

import train_uno_hand  # local caches and telemetry settings

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / "yolo/uno-recognition-2026-11-20"
os.environ["UNO_MODEL"] = str(PACKAGE / "models/best_hand_candidate_20261121.pt")
os.environ["UNO_SCALES"] = "256,416,512,640"
os.environ["UNO_HAND_ENHANCE"] = "0"
os.environ.pop("UNO_CAPTURE_DIR", None)
sys.path.insert(0, str(PACKAGE))


def main():
    from fastapi.testclient import TestClient
    import server

    jpeg = (ROOT / ".debug/recognition_eval/hand8_phone.jpg").read_bytes()
    report = {"model": str(server.MODEL_PATH), "requests": []}
    with TestClient(server.app) as client:
        response = client.get("/health")
        response.raise_for_status()
        report["health"] = response.json()
        assert len(server.app.state.model.names) == 54
        for number, target in enumerate((None, "hand", "discard")):
            response = client.post(
                "/detect", data={} if target is None else {"scan_target": target},
                files={"file": ("frame.jpg", jpeg, "image/jpeg")},
                headers={"X-Frame-Id": str(number)},
            )
            response.raise_for_status()
            result = response.json()
            assert result["frame_id"] == number
            assert result["scan_target"] == target
            assert result["scales"] == [256, 416, 512, 640]
            assert result["profile"] == "legacy"
            assert result["image_width"] == 1536 and result["image_height"] == 2048
            assert result["detections"]
            report["requests"].append({"target": target, "count": len(result["detections"]),
                                       "inference_ms": result["inference_ms"], "status_code": response.status_code})
    output = ROOT / "output/trained_hand_model/api_smoke.json"
    output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
