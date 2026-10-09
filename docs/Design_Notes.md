# Design Notes

Working notes for the AR AI Card Tutor. Project content only.

## What the app does

A mixed-reality coach for the **real** card game UNO on **Meta Quest 3**.

The player plays with **physical cards**. Using the headset camera (passthrough), the app reads:

- the cards in the learner's hand, and
- the top card of the discard pile (the last card played).

A **rules engine** decides which of the learner's cards are legal this turn, and an
**on-screen AI text layer** shows the legal cards and explains **why** each one is legal.

## Scope

**In scope now**
- "Which cards can I play, and why" — legality plus a short text explanation.
- Basic rule explanations for the learner.
- Prompting the learner to call UNO at the right moment.

**Out of scope (later, optional)**
- Counting the remaining cards in the deck.
- Porting the app to other card games.

## Confirmed decisions

- **Players: 2–4, all real people.** One learner wears the headset; the others play normally.
- **The opponent is a real person.** The app does not play as an opponent.
- **The learner's own play is read by camera first, with an optional manual confirm/edit
  step** as a double-check.

## Design principles

1. **Confirm-first.** Recognition is an accelerator, not a dependency. When the camera is
   unsure, the player taps to confirm or fix. The system still works if recognition fails.
2. **Watch for change, not the whole table.** The app only reacts when the top of the
   discard pile changes, then asks the learner to confirm it is their turn. It does not try
   to track the full state of several real players.
3. **Rules as data.** Player count, house rules and (later) other card games are
   configuration (ScriptableObjects), not hard-coded branches in code.

## Turn flow

1. The top of the discard pile changes → the app shows a light prompt: "Your turn?"
2. The learner confirms (pinch / tap). A persistent **"My turn"** button is also available.
3. The app reads the learner's hand (or the learner confirms it). The hand is read once and
   afterwards only updated by **subtracting the card that was played**.
4. The app lists the legal cards and, in text, explains why each is legal.
5. If a Wild is played, the app shows four colour chips; the player who played it picks the
   colour. The next card played is used to cross-check. A persistent HUD shows the current
   colour.
6. If the learner has exactly **2 cards and a legal move**, the app prompts: **"Say UNO"**
   (they are about to play down to one card).

## Reading the hand

- The passthrough camera sits on the front of the headset and faces the same way as the
  eyes, so **what the learner can see, the camera can roughly see too**.
- Use the **corner indices** (the small number + colour in the top-left and bottom-right
  corners): only the number and colour are needed, so overlapping cards are fine.
- Practical notes:
  - The app receives a **1280×960 crop**, narrower than the full passthrough view — keep
    the cards in the middle of the view.
  - A thumb often covers the top-left corner — the **bottom-right** corner works too.
  - Keep the cards about an arm's length away and angle them slightly toward the headset.
- A deliberate **scan motion** (sweeping the fan across the view) is an optional fallback if
  reading a still fan is unreliable.

## Rules (locked defaults)

| Rule | Setting |
| --- | --- |
| Player count | 2–4 (real people) |
| Can't play — draw | **Draw until a playable card is drawn** |
| Stacking (+2 / +4) | **Allowed** — a +2 or +4 may be stacked onto another +2 / +4 |
| Reverse | Normal with 3–4 players; **acts as Skip in a 2-player game** |
| UNO call | **Required.** The app prompts the learner when they play down to one card |
| Colour after a Wild | Chosen by the player who played the Wild; shown in a HUD; cross-checked by the next card |
| Jump-in and other house rules | Not used |

## Open questions

- Which camera source to use (left or right passthrough camera), and how to crop reliably.
- How the learner reports the opponent's Wild colour quickly (the app cannot see it).
- Exact wording and placement of the text panel so it does not block the view of the hand.
