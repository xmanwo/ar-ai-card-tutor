# AR AI Card Tutor

Mixed-reality coaching app for the **real** card game **UNO**, built for **Meta Quest 3** with Unity.

Players play with **physical cards**. Using the headset camera (passthrough), the app reads
the cards in the player's hand and the card played to the table, then a **rules engine**
decides whether a move is legal and an **on-screen AI text layer** explains the rules and
coaches the player.

## Unity version

**Use Unity 6.3 LTS — i.e. any `6000.3.x` patch.**

- Preferred exact version: **`6000.3.25f1`**.
- If your machine cannot install that patch, **any other `6000.3.x` LTS patch is fine**.
  Open the project, let Unity update `ProjectVersion.txt`, and commit that change on its own.
- Do **not** use Unity 6.2, 6.4 or 6.5.

Modules needed: **Android Build Support**, **OpenJDK**, **Android SDK & NDK Tools**.
Target platform: **Meta Quest 3** (Meta Quest build profile, OpenXR + Meta XR SDK).

## Open the project

1. Clone this repository (GitHub Desktop: File → Clone repository).
2. Unity Hub → Add → Add project from disk → select `unity/AICardTutor`.
3. The first open takes a few minutes while Unity builds its Library folder.

## Repository layout

| Folder | Contents |
| --- | --- |
| `unity/AICardTutor/` | The Unity project – open this folder in Unity Hub |
| `docs/` | Setup guide, meeting notes, sketches, user-study plan |
| `data/` | Not in Git – only a README with the shared-drive link |

## Team and ownership

| Person | Role | Owns |
| --- | --- | --- |
| `<github-username>` | <role> | e.g. `Scripts/Rules`, `Main.unity` |
| `<github-username>` | <role> | e.g. `Scripts/AI` |
| `<github-username>` | <role> | e.g. `Scripts/Cards` |
| `<github-username>` | <role> | e.g. `Scripts/UI` |

## Rules

1. Same Unity version for everyone.
2. Never commit `Library/`.
3. Always commit `.meta` files together with their assets.
4. One person edits a scene at a time; `SampleScene.unity` has one owner.
5. Work on a branch (`name/topic`, where `name` is your GitHub username), and merge into `main` through a pull request
   (**at least 1 approval required**). Do not push directly to `main`.
6. No Git LFS: photos, videos and `.apk` builds go on the shared drive, never in Git.

Full setup guide: [`docs/Setup_Guide.md`](docs/Setup_Guide.md).

## Current Quest implementation

The working scene is `unity/AICardTutor/Assets/Scenes/SampleScene.unity`.
Build and run the Android app on Quest 3. The existing manual workflow is preserved:
scan the discard and hand separately, review each result, and Confirm. A Wild discard
also needs its active colour selected. Confirmed results stay frozen until Rescan.

- Card faces replace the long hand text list, including repeated cards and pagination.
- The left preview shows the original photo and the actual cropped recognition input
  from the same capture. Green marks an uploaded crop; amber marks full-frame fallback.
- Auto focus uses tracked palms to choose a nearby card-corner cluster. The cluster
  determines crop size; head/hand tracking follows its movement. Left, Right and Off
  remain optional overrides. Missing tracking, empty crops and tracking jumps recover
  through full-frame recognition. This is still a manual scan, not continuous game tracking.
- Explain / Give a hint request a DeepSeek explanation through the PC service. The
  local rules engine remains authoritative. API keys are prompted locally, never
  stored in Unity assets, and no model request is made until an AI button is used.

### PC service and USB

1. Install the Python environment with
   `yolo/uno-recognition-2026-11-20/setup_windows.bat`.
2. Both runnable weights are included in `yolo/uno-recognition-2026-11-20/models/`:
   `best.pt` (baseline) and `best_hand_candidate_20261121.pt` (trained trial).
   No separate model download or Git LFS is required. `weights-manifest.json`
   records file sizes and SHA-256 checksums; the candidate's adjacent JSON model
   card records data credits and evaluation limits. See [`yolo/README.md`](yolo/README.md).
3. Run **one** service launcher: `run_trained_hand_test.bat` for the candidate,
   or `run_ai_server.bat` for the original `best.pt`. Enter your own DeepSeek key
   and keep the window open. `check_deepseek.bat` is an optional connectivity test.
4. Connect Quest by USB, allow USB debugging, and run `connect_quest_usb.bat`.
   Repeat the forwarding step after reconnecting USB. The app uses
   `http://127.0.0.1:8000`; changing PC model weights does not require rebuilding the APK.
5. For hand-guided focus, put down the controllers and wait for hand tracking before
   starting a new hand scan. Code/UI updates do require Build And Run again.

Training preparation, training and evaluation scripts are in `tools/`. They use local
data under `data/` and write checkpoints to `training_runs/`; neither is committed.
Only the two explicitly published inference weights are tracked; other `.pt` files
remain ignored.
Private camera captures and local experiment reports under `.debug/` and `output/`
are also excluded. Card-face PNG assets include their CC0 licence and source manifest.

### Validation and remaining limitations

The current hand focus geometry/selection suite has 76 offline checks. Runtime,
editor and Android conditional C# compilation passed locally. The PC API and
recognition geometry have offline regression tests in `tools/test_*.py`.

The trained candidate improves the labels recovered on a development hand photo,
but public-test mAP50-95 regressed. Wild misses and opposite-corner duplicate counts
remain observable. Focus fitting was replayed on a saved Quest frame using a supplied
palm position; the latest adaptive focus still needs an updated-APK device test.
Do not interpret detection count as perfect card recognition. Turn history, automatic
play/draw events and continuous full-game recognition are not implemented.
