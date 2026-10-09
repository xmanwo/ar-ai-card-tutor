# AR AI Card Tutor

Mixed-reality coaching app for the **real** card game **UNO**, built for **Meta Quest 3** with Unity.

Players play with **physical cards**. Using the headset camera (passthrough), the app reads
the cards in the player's hand and the card played to the table, then a **rules engine**
decides whether a move is legal and an **on-screen AI text layer** explains the rules and
coaches the player.

## Unity version

**Everyone must use exactly this version:** `6000.3.25f1`

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
4. One person edits a scene at a time; `Main.unity` has one owner.
5. Work on a branch (`name/topic`, where `name` is your GitHub username), and merge into `main` through a pull request
   (**at least 1 approval required**). Do not push directly to `main`.
6. No Git LFS: photos, videos and `.apk` builds go on the shared drive, never in Git.

Full setup guide: [`docs/Setup_Guide.md`](docs/Setup_Guide.md).
