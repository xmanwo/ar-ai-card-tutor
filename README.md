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
  through full-frame recognition.
- **Live = repeated Scan Hand.** Press **Start Live**; no Scan Hand / Confirm
  prerequisite is needed. Live repeatedly executes the ordinary Scan Hand round:
  the same capture, hand-ROI selection, JPEG, server request, per-card association,
  multi-frame class votes, expiry, minimum duration and stable-freeze checks. A new
  round begins automatically after the previous round finishes. There is no separate
  single-frame hand filter, palm prerequisite or scheduled full-frame probe. After
  reliable acquisition, the next round carries the existing hand-focus context instead
  of clearing it. Normal Scan Hand parameters apply (0.3-second request interval, at least
  1.5 seconds and at most 8 seconds / 25 requests per round by default).
- **Reliable focus continuity:** initial focus is carried only after a normally settled
  (not timed-out) complete scan with valid faces and at least 80% of cards free of
  low-confidence/low-vote flags. Later weak rounds keep the previous trusted region;
  they do not replace its geometry. Tracked palm position/scale moves the learned
  envelope with the hand. If hand tracking is temporarily unavailable, its last
  reliable image region is retained; RGB card bounds can also establish the region.
  Empty individual replies do not clear Live focus or trigger full-frame discovery.
- **Full reacquisition threshold:** a complete scan missing/changing at least two
  previously reliable cards schedules the next round to restart from a full image.
  Only the old-card deficit is counted: drawing any number of additional cards alone
  does not trigger this reset, and new cards do not cancel two lost old cards. Repeated
  faces count by multiplicity. One missing/changed card continues following; a legal
  play removes that card from the tracking reference. An unreliable initial scan does
  not establish the reference, so a later reliable scan can still acquire it. After
  full reacquisition succeeds, following resumes. Explicit hand-mode changes and a
  camera image-size change invalidate incompatible geometry; Crop Off stays full-frame.
- The **overlapping card fan displays the latest completed Scan Hand result**,
  including duplicate faces and the same uncertainty flags as the manual scan.
  Intermediate replies do not redraw the fan; the previous result remains while the
  next round scans. Original/cropped photographs remain on the left and use the
  ordinary Scan Hand capture. Debug Tracks / Detections remains optional; every Live
  start defaults to card view. Stop Live ends automatic rescanning.
- The first completed hand scan establishes the session inventory. A newly recognized
  extra card in a larger scan is recorded as **drawing**, preserving existing card IDs.
  Re-observing an already held card does not add another copy. Same-sized label changes
  update the displayed latest result but are not treated as a draw or a play.
- A card absent in **two consecutive completed Scan Hand rounds** counts as played
  only when `StandardUnoRules` says it is legal against the known top, active colour,
  full current hand and configured `RuleSet` (including the +4 restriction). This is
  a round count, not a two-second timer and not two individual HTTP replies. Seeing
  the card in an intervening completed round clears the missing count. Failed requests
  and camera-unavailable rounds clear pending evidence rather than counting as missing.
  Successful empty scans can detect the final card. One eligible play is committed
  per completed round; repeated identical faces retain separate counts.
- The latest fan may show fewer cards before a play is registered: the first missing
  round is displayed immediately, while the inventory/history wait for the second
  round and rules. Illegal disappearances are reported and are **not** appended to
  the discard pile. A known top card and active colour are required for legality;
  without them Live still scans/displays hands but does not invent a legal play.
  Use the existing Scan Discard workflow to establish the top before Live.
- A verified play removes one inventory card and appends its texture to **Discard
  pile · Played** atomically. Wild plays request an explicit active colour before
  committing. **Undo last play** restores hand/history/top and the displayed draft;
  return the physical card too. Network failures retain the latest result and retry
  automatically after 1, 2, 4, then at most 5 seconds. Bounded local
  `LivePlayDiagnostics.log` files record completed rounds, draws and missing-round
  streaks without photos or credentials. The earlier frame/motion-based Live helpers
  remain in source for historical regression checks; the active Live flow does not
  call them. Full turn ownership/opponent turn flow is not implemented.
- Explain / Give a hint request a DeepSeek explanation through the PC service. The
  local rules engine controls legal-play registration and supplies advice. API keys are prompted locally, never
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
`tools/LiveTrackingChecks.cs` adds 43 offline tracking/overlay checks. The Editor
menu **Tools → AR AI Card Tutor → Check live preview isolation** checks actual
texture copies, preview UI and confirmation isolation without a headset or API call.
The repeated-Scan-Hand implementation passes runtime, Editor and Android conditional
compilation and 88 round/draw/rules/focus checks (`tools/LiveScanHandChecks.cs`). These cover
initial acquisition, latest-result publication, first vs second missing rounds,
intervening recovery, duplicate faces, draws, unchanged-count label changes, unknown
top rejection, failed scans, Wild colour, configured +4 rules and Undo. The separate
legacy-verifier/session suite (`tools/LivePlayChecks.cs`) still passes 94 checks.
The updated Unity menu fixture replays the identical replies through manual Scan Hand
and Live, compares their complete results including low-confidence/repeated faces,
and checks the actual latest-result fan, preserved photos, drawing, two-round legal
plays, illegal-play rejection, Undo, retry and card-view defaults. It also verifies
quality-gated focus acquisition, retention across rounds/brief missing tracking,
no reset on drawing or a single missing card, and full reacquisition after two changes. All 31 native Unity checks passed locally. It makes no camera,
server or paid API request. Its latest execution report is local under
`output/live_tracking/unity_smoke.json`.

The new loop needs Build And Run and a Quest test. Offline round parity does not
establish perfect real-world CV accuracy. Each hand update now takes a complete ordinary
Scan Hand round; two missing rounds may take several seconds. Long occlusion spanning
two successful rounds can count as disappearance when the card is legal.

The trained candidate improves the labels recovered on a development hand photo,
but public-test mAP50-95 regressed. Wild misses and opposite-corner duplicate counts
remain observable. Focus fitting was replayed on a saved Quest frame using a supplied
palm position; the latest adaptive focus still needs an updated-APK device test.
Do not interpret detection count as perfect card recognition. Live play detection
needs updated-APK testing on Quest. Persistent visual mistakes can still require Undo. Full-game turn flow and automatic draw events are not implemented.
