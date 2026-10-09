# AR AI Card Tutor – Setup Guide

How to get the same Unity version, one shared GitHub repository, and a working build on a
Meta Quest 3.

All paths and menu names below are for **Windows**.

**Roles:** one person (the **repo owner**) does Stages 2 and 4–8. Everyone else does
Stages 1, 3, 9 and 10.

## Five golden rules (most problems come from breaking one of these)

1. Everyone uses the **same Unity line** — **Unity 6.3 LTS (`6000.3.x`)**, preferably
   `6000.3.25f1` (written in the README). Any other `6000.3.x` patch is acceptable.
2. **Never commit Unity's `Library/` folder.** The `.gitignore` handles this for you.
3. **Always commit a `.meta` file together with its asset.** Without it, links between
   files break for everyone else.
4. **Only one person edits a given scene at a time.** `Main.unity` has one owner.
5. The `main` branch must always open and build. Work on your own branch and merge
   through a **pull request with at least 1 approval**.

## Stage overview

| # | What | Who |
| --- | --- | --- |
| 1 | Install the tools | Everyone |
| 2 | Create the GitHub repository | Repo owner |
| 3 | Clone the repository | Everyone |
| 4 | Create the folder structure | Repo owner |
| 5 | First commit (skeleton only) | Repo owner |
| 6 | Create and configure the Unity project | Repo owner |
| 7 | Second commit (Unity project) | Repo owner |
| 8 | Add collaborators and protect `main` | Repo owner |
| 9 | Open the project for the first time | Everyone else |
| 10 | Daily workflow | Everyone |

---

## Stage 1 – Install the tools

1. **Unity Hub** from unity.com/download. Sign in with a free Unity account.
2. **Unity Editor `6000.3.25f1`** (Unity 6.3 LTS). If you cannot install this exact patch,
   any other **`6000.3.x`** patch works. In Unity Hub → **Installs → Install
   Editor**, pick that version and tick:
   - **Android Build Support**
     - **OpenJDK** (under Android Build Support)
     - **Android SDK & NDK Tools** (under Android Build Support)
   > Meta Quest runs Android, so these modules are required for Quest builds.
   > Do **not** install Unity 6.5 — the Meta XR SDK currently has incompatibilities with it.
3. **Git** from git-scm.com. Default options are fine.
   Use a short Windows path with no spaces and only English letters.
4. Tell Git who you are (once per computer):
   ```
   git config --global user.name "Your Name"
   git config --global user.email "you@example.com"
   ```
5. **GitHub Desktop** from desktop.github.com (optional but easiest).
6. A **code editor**: Visual Studio Community (Game development with Unity workload) or
   VS Code with the Unity extension. Later set it in Unity under
   Edit → Preferences → External Tools → External Script Editor.

Check it worked: `git --version` and `git config user.name`.

## Stage 2 – Create the GitHub repository (repo owner)

1. On github.com → **New repository**.
   - **Name:** `ar-ai-card-tutor`
   - **Visibility:** Public
   - Leave **Add a README / .gitignore / license** all **unticked** – we add our own.
2. Do **not** add collaborators or branch protection yet (see Stage 8).

> The repo is public, so anyone can read it, including its history. Never commit
> anything internal, and never commit API keys or passwords.

## Stage 3 – Clone the repository

GitHub Desktop → **File → Clone repository** → `ar-ai-card-tutor` → local path
`E:\vrar\tutor1\dev\ar-ai-card-tutor`.

## Stage 4 – Create the folder structure (repo owner)

One repository holds everything except the large assets. In the repository root:

```
ar-ai-card-tutor/                     ← the Git repository
├── .gitignore
├── .gitattributes
├── README.md                         ← Unity version, how to open
├── docs/                             ← setup guide, meeting notes, user-study plan
├── data/                             ← NOT in Git: only a README with the drive link
└── unity/
    └── AICardTutor/                  ← the Unity project (created in Stage 6)
```

Why it is set up this way:
- `.gitignore` and `.gitattributes` must sit in the **root**, not inside `unity/`,
  so Unity's temporary folders and large files are never uploaded.
- `data/` stays out of Git because captures and builds are large and change often.
- `Library/`, `Temp/`, `Logs/` and `UserSettings/` are rebuilt by Unity on every
  computer and are never committed.

## Stage 5 – First commit (skeleton only) (repo owner)

```
cd E:\vrar\tutor1\dev\ar-ai-card-tutor
git add .
git status                 # confirm no Library / Temp / .apk
git commit -m "Repo skeleton: gitignore, gitattributes, README, folders"
git push -u origin main
```

Doing this **before** the Unity project means the `.gitignore` is already in place, so
Unity's generated files are ignored from the very first Unity commit.

## Stage 6 – Create and configure the Unity project (repo owner)

1. Unity Hub → **New project** → editor version `6000.3.25f1` (or another `6000.3.x`) → template **Mixed Reality**
   (if your Hub names it slightly differently, pick the Mixed Reality template — **not**
   the "Mixed Reality multiplayer tabletop" one, which adds a networking stack we don't need).
   - Project name: `AICardTutor`
   - Location: `E:\vrar\tutor1\dev\ar-ai-card-tutor\unity`
   Unity creates `unity\AICardTutor`.
2. **Switch platform:** File → Build Profiles → **Meta Quest** → Enable Platform
   (accept installing `com.unity.xr.openxr`). If Meta Quest is missing, select **Android**
   → Switch Platform.
3. **Check the packages** (Window → Package Manager → In Project). The Mixed Reality
   template usually brings OpenXR / OpenXR Meta / AR Foundation / XRI / Input System.
   Make sure these are present; add any that are missing:
   - `com.unity.xr.openxr` – OpenXR Plugin
   - `com.unity.xr.meta-openxr` – Unity OpenXR: Meta (pulls in AR Foundation)
   - `com.unity.xr.arfoundation`
   - `com.unity.xr.interaction.toolkit` – XRI 3.x
   - `com.unity.xr.hands` – hand tracking
   - `com.meta.xr.sdk.all` – Meta XR All-in-One SDK
   - `com.meta.xr.mrutilitykit` – Meta MR Utility Kit
   > Do **not** install the Oculus XR Plugin (deprecated). Do not keep two XR providers at once.
4. **Configure XR:** Edit → Project Settings → XR Plug-in Management → **Android** tab →
   tick **OpenXR**. Then under OpenXR → Android, tick the **Meta XR** feature group
   (Meta XR Feature, Foveated Rendering, Subsampling Layout) and add the **Oculus Touch
   Controller Profile** and hand-tracking profiles.
5. **Fix the rest automatically:** Meta → Tools → Project Setup Tool → **Fix All**, then
   **Apply All**.
6. **Version-control settings:** Edit → Project Settings → Editor:
   - Version Control Mode: **Visible Meta Files**
   - Asset Serialization Mode: **Force Text**
   Both are the default in new projects; just confirm.
7. **Name the app:** Project Settings → Player: Company Name = the app publisher name,
   Product Name = `AI Card Tutor`.
8. File → **Save Project**, then **close Unity** before the commit.

## Stage 7 – Second commit (Unity project) (repo owner)

```
git add .
git status                 # again confirm no Library
git commit -m "Initial Quest 3 MR project (Unity 6000.3.25f1)"
git push
```

Check on github.com: there is **no Library folder** and no `.apk` files.

## Stage 8 – Add collaborators and protect `main` (repo owner)

Only after the first pushes to `main` are done:

1. Settings → **Collaborators → Add people** → enter each collaborator's GitHub username.
   They must accept the email invitation.
2. Settings → **Branches** (or Rules → Rulesets) → add a rule for `main`:
   - **Require a pull request before merging**
   - **Require 1 approval**
   Branch rules are free on public repositories.

> Do this after the initial pushes: with "require 1 approval" on and no other
> collaborator, you could not approve your own pull request.

## Stage 9 – Open the project for the first time

1. Accept the GitHub invitation from your email.
2. Clone: GitHub Desktop → File → Clone repository → `ar-ai-card-tutor` → local path
   `E:\vrar\tutor1\dev\ar-ai-card-tutor`.
3. Open: Unity Hub → Add → Add project from disk → choose
   `E:\vrar\tutor1\dev\ar-ai-card-tutor\unity\AICardTutor` (the folder that contains `Assets`).
   If Hub says the editor version is missing, install a **`6000.3.x`** patch (preferably
   `6000.3.25f1`). Do not jump to 6.2 / 6.4 / 6.5.
4. The first open takes several minutes while Unity builds its `Library` folder.
5. Make your own test scene: right-click `Assets/_Project/Scenes/Sandbox` →
   Create → Scene → name it after your GitHub username.

## Stage 10 – Daily workflow

Every work session:
1. **Get the latest.** GitHub Desktop: switch to `main` → Fetch origin → Pull origin.
   Do this with Unity closed, or Unity may need to re-import.
2. **Make a branch:** Current branch → New branch → name it `name/topic`, where
   `name` is your GitHub username (e.g. `xmanwo/rule-engine`).
3. **Work in Unity.** Save often (Ctrl+S for scenes, File → Save Project for other assets).
   Unity only writes changes to disk when you save.
4. **Commit small pieces** with a clear message. Before committing, check the list:
   every new asset has its `.meta` file next to it; no `Library`/`Temp`/`.csproj`;
   no scene changed by accident.
5. **Push your branch**, then on github.com click **Compare & pull request**. Write two
   lines: what you changed and how to test it.
6. **One teammate reviews and approves**, then click **Merge**, and delete the branch.
7. Everyone pulls `main` again.

**Avoiding scene conflicts:** `.unity` and `.prefab` files are long text files Git cannot
merge safely. `Main.unity` has one owner; others build their feature in their own Sandbox
scene, save it as a prefab, and ask the owner to place it in `Main.unity`. Keep logic in
scripts and settings in ScriptableObject assets (`Config/`), which merge much more easily.

**Secrets:** never commit secrets — the repository is public. The AI layer needs an API
key. Copy `Assets/StreamingAssets/secrets.example.json` to `secrets.json` and put your key
there; `.gitignore` excludes `secrets.json` but keeps the empty template.

---

## Common problems and fixes

| Problem | Likely cause | Fix |
| --- | --- | --- |
| `Library/` files appear in the change list | `.gitignore` not in the repo root | Move it to the root. If already committed: `git rm -r --cached unity/AICardTutor/Library`, then commit |
| "Missing script" or pink objects after pulling | Someone forgot to commit `.meta` files | Ask them to commit the missing `.meta` files; never create new ones yourself |
| Cloning/pulling gets slow | Large files committed by mistake | Keep `data/`, videos and `.apk` on the shared drive |
| Unity Hub says the editor version is missing | Different Unity version | Install a `6000.3.x` patch (preferably `6000.3.25f1`); don't jump Unity lines |
| Quest does not appear / build fails with Gradle or SDK errors | Android modules missing, or unusual project path | Re-add the Android modules in Unity Hub; use a short path with no spaces |
| Merge conflict in a `.unity` / `.prefab` file | Two people edited the same scene | Keep one version, redo the smaller change, follow the one-editor-per-scene rule |
| Push rejected because a file is too large | GitHub blocks files over 100 MB | Do not commit it; move it to the shared drive |
