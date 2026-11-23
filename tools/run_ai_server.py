"""Launch the original recognition app with the additional tutoring router."""

import getpass
import os
from pathlib import Path
import socket
import sys


def main():
    root = Path(__file__).resolve().parents[1]
    package = root / "yolo" / "uno-recognition-2026-11-20"
    # Catch the usual duplicate-server mistake before asking for credentials or loading YOLO.
    with socket.socket() as probe:
        try:
            probe.bind(("127.0.0.1", 8000))
        except OSError:
            print("端口 8000 已占用。请先关闭原 run_server.bat 服务窗口，再启动本脚本。")
            return 1
    key = os.environ.get("DEEPSEEK_API_KEY", "").strip()
    if not key:
        key = getpass.getpass("粘贴完整 DeepSeek API Key（不显示、不保存），然后按 Enter：").strip()
    if not key:
        print("没有输入 Key，已取消。")
        return 1
    os.environ["DEEPSEEK_API_KEY"] = key
    os.environ.setdefault("UNO_YOLO_DIR", str(package))
    os.environ.setdefault("UNO_MODEL", str(package / "models" / "best.pt"))
    config_dir = package / ".ultralytics"
    (config_dir / "Ultralytics").mkdir(parents=True, exist_ok=True)
    os.environ.setdefault("YOLO_CONFIG_DIR", str(config_dir))
    os.environ.setdefault("MPLCONFIGDIR", str(package / ".venv" / "cache" / "matplotlib"))
    sys.path.insert(0, str(package))

    import uvicorn
    from server import app
    from tutor_api import router

    app.include_router(router)
    print("识别 + AI 服务即将启动。USB 地址：http://127.0.0.1:8000")
    print(f"当前识别模型：{Path(os.environ['UNO_MODEL']).name}。只有点击 AI 按钮才会调用 DeepSeek。")
    print("请保留本窗口。")
    if os.environ.get("UNO_HAND_ENHANCE", "0") == "1":
        print("手牌增强测试已开启。原始扫描测试帧仅保存在电脑本地。")
    uvicorn.run(app, host="127.0.0.1", port=8000, log_level="info")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (KeyboardInterrupt, EOFError):
        print("\n服务已停止。")
