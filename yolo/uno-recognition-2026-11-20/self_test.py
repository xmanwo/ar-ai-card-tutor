"""Local self-test: checks the service without a headset.

    .venv\\Scripts\\python.exe self_test.py

Exit code 0 means: /health answered and the sample image produced at least one card.
All output is ASCII on purpose, so it survives any Windows console code page.
"""

from __future__ import annotations

import sys
from pathlib import Path

import requests

BASE = "http://127.0.0.1:8000"
ROOT = Path(__file__).resolve().parent
SAMPLE = ROOT / "samples" / "test_uno.jpg"


def fail(message: str) -> int:
    print("[FAIL] " + message)
    return 1


def main() -> int:
    if not SAMPLE.is_file():
        return fail(f"sample image missing: {SAMPLE}")

    try:
        health = requests.get(BASE + "/health", timeout=10)
    except requests.RequestException as exc:
        return fail(
            f"cannot reach {BASE} ({exc.__class__.__name__}). "
            "Start run_server.bat first, and keep that window open."
        )

    if health.status_code != 200:
        return fail(f"/health returned HTTP {health.status_code}: {health.text[:200]}")

    info = health.json()
    print("[OK]   service is up")
    print(f"       status={info.get('status')} scales={info.get('scales')} "
          f"conf={info.get('conf')}")

    try:
        config = requests.get(BASE + "/config", timeout=5).json()
        print(f"       model={config.get('model')}")
    except requests.RequestException:
        pass

    with SAMPLE.open("rb") as handle:
        files = {"file": (SAMPLE.name, handle, "image/jpeg")}
        try:
            reply = requests.post(
                BASE + "/detect",
                files=files,
                headers={"X-Frame-Id": "1"},
                timeout=60,
            )
        except requests.RequestException as exc:
            return fail(f"/detect request failed: {exc}")

    if reply.status_code != 200:
        return fail(f"/detect returned HTTP {reply.status_code}: {reply.text[:300]}")

    body = reply.json()
    detections = body.get("detections") or []
    print(f"[OK]   /detect answered in {body.get('inference_ms')} ms, "
          f"{len(detections)} detection(s)")

    for item in detections[:8]:
        print(f"       {item.get('label'):<16} conf={item.get('confidence'):.2f} "
              f"box={item.get('bbox_xyxy')}")

    if not detections:
        return fail("the service works but found no card in the sample image. "
                    "Check that the model file exists and matches the one shipped in this folder.")

    print("\nAll good. The headset can use this service now.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
