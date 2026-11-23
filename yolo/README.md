# PC recognition and AI service / 电脑端识别与 AI 服务

The source service lives in `uno-recognition-2026-11-20/`. Run the BAT launchers
from the repository root. Keep only one service process on port 8000.

源码位于 `uno-recognition-2026-11-20/`。使用仓库根目录的 BAT 启动，8000 端口只保留一个服务。

## Files not included in Git / 不随 Git 上传的文件

Obtain these files from the team's shared handoff and copy them into
`uno-recognition-2026-11-20/models/`:

| File | Use |
| --- | --- |
| `best.pt` | Original baseline; selected by `run_ai_server.bat` |
| `best_hand_candidate_20261121.pt` | Trained candidate; selected by `run_trained_hand_test.bat` |
| `best_ft_20261104.pt` | Historical optional model; not selected by either root launcher |

模型权重、原始 ZIP、APK、训练集和用户照片均留在共享包或本地，不进入 Git。
The candidate JSON model card is committed next to the weights' expected location;
it includes SHA-256, dataset credits, split counts, evaluation limitations and sources.

## Setup and run / 安装与运行

1. Install Python 3.11–3.13 (locally tested: 3.12.14) and run
   `uno-recognition-2026-11-20/setup_windows.bat`.
2. Copy the shared weights into `models/` as listed above.
3. Start `run_trained_hand_test.bat` at the repository root. Enter your own DeepSeek
   API key when prompted; it is held in process memory. Keep the service window open.
4. Connect Quest by USB and allow USB debugging. Run `connect_quest_usb.bat`.
   The helper finds ADB through `ADB_EXE`, Android SDK environment variables, PATH,
   or the default Unity 6000.3.25f1 SDK paths. For a custom installation, set `ADB_EXE`.
5. Build and run Unity's `Assets/Scenes/SampleScene.unity` on Quest. Scan and Confirm
   discard and hand separately; choose a colour for a Wild discard. Use hand input
   for hand-guided focus. AI buttons call DeepSeek only after the required state is saved.

日常运行需要两个 BAT：一个识别/AI 服务窗口，以及一次 USB 转发。USB 重连后重新转发。
修改 Unity 代码需要重新 Build And Run；仅更换电脑端模型不需要重打 APK。

The tested NVIDIA environment is PyTorch 2.8.0 + CUDA 12.8, torchvision 0.23.0 and
Ultralytics 8.4.177. To reproduce that GPU installation, run inside the package:

```bat
.venv\Scripts\python.exe -m pip install torch==2.8.0 torchvision==0.23.0 --index-url https://download.pytorch.org/whl/cu128
```

`/health` reports readiness; `/config` shows the selected model and scales. Root
`run_ai_server.bat` restores the baseline. `run_hand_test.bat` is the older, slower
rotation-enhancement trial and is optional. The standalone `run_server.bat` starts
recognition only; it does not add the DeepSeek router.

## Offline checks / 离线检查

From the repository root, using the package's virtual environment:

```bat
yolo\uno-recognition-2026-11-20\.venv\Scripts\python.exe -m pip install -r yolo\uno-recognition-2026-11-20\requirements-dev.txt
yolo\uno-recognition-2026-11-20\.venv\Scripts\python.exe -m unittest discover -s tools -p "test_*.py"
```

These API/geometry tests use mocked inference/cloud calls and do not require a real
API key or model weights. Real GPU smoke/evaluation scripts in `tools/` additionally
need the weights and the local evaluation captures. `self_test.py` also requires the
shared sample at `samples/test_uno.jpg`, which is intentionally not committed.

## Current scope and sources / 当前范围与来源

The Unity preview retains the original photo and the actual uploaded crop. The
adaptive crop follows tracked palms and fits nearby card-corner clusters; a large
fixed physical fan box is no longer used. Missing tracking and empty/jumping crops
recover through full-frame recognition. Latest adaptive focus still needs updated-APK
headset verification; Wild misses and opposite-corner duplicate counts remain known.

This is a manual scan/review/Confirm workflow. Continuous play tracking, automatic
played-card/drawn-card events and a full UNO turn state machine are not implemented.

The detector identifies corner symbols, with code derived from
[Stormynova/Uno_Card_detection](https://github.com/Stormynova/Uno_Card_detection).
Its MIT notice is preserved in `uno-recognition-2026-11-20/UPSTREAM_LICENSE.txt`.
Training data sources/licences are recorded in the candidate JSON model card;
synthetic card faces use the Unity `Resources/UnoCards` CC0 assets and source manifest.
