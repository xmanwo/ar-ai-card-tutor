# UNO Recognition Service — Portable Package
# UNO 识别服务 — 便携包

> Source checkout / 源码仓库：`models/best.pt` 与 `models/best_hand_candidate_20261121.pt`
> 已随 Git 提供；历史可选权重、样图和原始 ZIP 不在 Git 中。当前 Unity + DeepSeek + USB
> 流程以仓库根目录 README 和 `yolo/README.md` 为准；下面保留独立识别服务的使用说明。

**EN** — This folder is the PC half of the AR AI Card Tutor demo. The headset sends
camera frames here; this service runs a YOLO model on them and returns the detected
cards. Nothing is installed system-wide: everything lives inside this folder.

**中文** — 这个文件夹是 AR AI Card Tutor 演示的电脑端部分。头显把相机画面发到这里，
这个服务用 YOLO 模型跑推理，把识别到的牌返回去。不需要往系统里装任何东西，
所有文件都在这个文件夹里。

---

## 1. What is in here / 包里有什么

| File / 文件 | Purpose / 用途 |
| --- | --- |
| `server.py` | HTTP service (`/health`, `/detect`) / HTTP 服务 |
| `detect_v2.py` | YOLO inference + corner-figure de-duplication / 推理与角标去重 |
| `models/best.pt` | Default weights, the ones the demo has been tested with / 默认权重，演示验证过的那份 |
| `models/best_ft_20261104.pt` | Newer fine-tuned weights, optional / 11-04 微调的新权重，可选 |
| `setup_windows.bat` | One-time installer / 一次性安装 |
| `run_server.bat` | Starts the service / 启动服务 |
| `self_test.py` | Checks the service without a headset / 不戴头显也能自检 |
| `samples/test_uno.jpg` | Sample frame used by the self-test / 自检用的样图 |

---

## 2. One-time setup / 一次性安装

**EN** — Install **Python 3.11–3.13** from python.org and tick *Add python.exe to PATH*.
Unzip this folder to a short path with **no spaces**, for example `D:\uno-recognition`.
Do not put it on the Desktop or in OneDrive — spaces and sync break the virtualenv.
Then double-click `setup_windows.bat` and wait. The first run downloads about 2 GB.

**中文** — 先装 **Python 3.11–3.13**（python.org，安装时勾选 *Add python.exe to PATH*）。
把这个文件夹解压到一个**没有空格**的短路径，比如 `D:\uno-recognition`。
不要放桌面或 OneDrive——路径里的空格和同步会搞坏虚拟环境。
然后双击 `setup_windows.bat`，等它跑完。第一次会下载大约 2 GB 的 PyTorch。

**EN** — If the PC has an NVIDIA GPU, replace the CPU build of PyTorch once:
`".venv\Scripts\python.exe" -m pip install torch torchvision --index-url https://download.pytorch.org/whl/cu124`

**中文** — 如果这台电脑有 NVIDIA 显卡，跑一次这条命令把 CPU 版 PyTorch 换成 GPU 版：
`".venv\Scripts\python.exe" -m pip install torch torchvision --index-url https://download.pytorch.org/whl/cu124`

---

## 3. Every time you run the demo / 每次演示

**EN** — Double-click `run_server.bat`. Wait for `Uvicorn running on http://0.0.0.0:8000`
and `Warm-up ... ms`. Keep the window open; closing it stops recognition.
Then run the self-test in a second window:
`".venv\Scripts\python.exe" self_test.py` — it should print `All good`.

**中文** — 双击 `run_server.bat`，等到出现 `Uvicorn running on http://0.0.0.0:8000`
和 `Warm-up ... ms`。窗口要一直开着，关掉识别就停了。
再开一个窗口跑自检：`".venv\Scripts\python.exe" self_test.py`，看到 `All good` 就成了。

**EN** — Windows Firewall will ask once. Allow **private networks**. Without it the
headset cannot reach the PC over Wi-Fi (USB is unaffected).

**中文** — Windows 防火墙会弹一次窗，勾选**专用网络**放行。不放行的话头显走 Wi-Fi 连不上
（USB 不受影响）。

---

## 4. Pick the model / 换模型

**EN** — Default is `models/best.pt`, the weights the demo has been tested with.
To try the newer fine-tuned one, set the variable before starting:

**中文** — 默认是 `models/best.pt`，演示验证过的那份。想试 11-04 微调的新权重，启动前设变量：

```bat
set UNO_MODEL=%~dp0models\best_ft_20261104.pt
run_server.bat
```

**EN** — On a CPU-only machine the four-scale pass is slow. Force one scale:
`set UNO_SCALES=640` before `run_server.bat`. Accuracy drops a little, latency drops a lot.

**中文** — 只有 CPU 的机器上四尺度很慢，可以强制单尺度：`run_server.bat` 之前
`set UNO_SCALES=640`。精度会掉一点，延迟降很多。

---

## 5. Troubleshooting / 故障排查

| Symptom / 现象 | Cause / 原因 | Fix / 处理 |
| --- | --- | --- |
| `[FAIL] cannot reach http://127.0.0.1:8000` | Service window closed, or crashed on start / 服务窗口关了或启动崩了 | Run `run_server.bat` and read its first error line / 重开并看第一条报错 |
| `[WinError 10048] address already in use` | Something else holds port 8000 / 8000 端口被占 | `netstat -ano \| findstr :8000`, kill that PID / 杀掉占端口的进程 |
| `RuntimeError: detect_v2.py was not found` | Folder was moved or `UNO_YOLO_DIR` points elsewhere / 文件夹被移动 | Unzip again and run from the new folder / 重新解压并在此目录运行 |
| Service starts but `/detect` takes >3 s | CPU-only PyTorch / 用的是 CPU 版 | Install the CUDA build above, or `set UNO_SCALES=640` |
| Headset shows `No reachable server` | Wi-Fi firewall, or `adb reverse` not set / 防火墙或没做反向转发 | See the USB section of the run guide / 看流程文档的 USB 部分 |
| Detections are wrong or missing | Wrong model, or cards out of frame / 模型不对或牌不在画面里 | Check `/config` shows the model you expect / 用 `/config` 确认加载的模型 |

---

## 6. Health check from a browser / 浏览器里自检

**EN** — Open `http://127.0.0.1:8000/health`. You should see
`{"status":"ready","scales":[256,416,512,640],...}`. If it shows anything else,
the service on that port is not ours.

**中文** — 浏览器打开 `http://127.0.0.1:8000/health`，应该看到
`{"status":"ready","scales":[256,416,512,640],...}`。如果不是这个内容，
说明 8000 端口上跑的不是我们这套服务。
