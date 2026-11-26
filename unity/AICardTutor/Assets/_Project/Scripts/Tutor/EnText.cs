using System.Collections.Generic;

/// <summary>
/// English text pack. This is the product default, so it must stay complete: every key used by
/// the rules, the tutor and the panel belongs here. Other packs may be partial.
///
/// Keys are grouped by prefix so it stays obvious which layer owns them:
///   colour.*  - a card colour
///   value.*   - a card value, including the ten digits
///   why.*     - one sentence explaining a play check
///   tutor.*   - the summary line above the list
/// </summary>
public static class EnText
{
    public static readonly Dictionary<string, string> Pack = new Dictionary<string, string>
    {
        // ---- Colours -----------------------------------------------------
        { "colour.red", "Red" },
        { "colour.green", "Green" },
        { "colour.blue", "Blue" },
        { "colour.yellow", "Yellow" },
        { "colour.wild", "Wild" },
        { "colour.unknown", "Unreadable" },

        // ---- Values ------------------------------------------------------
        { "value.0", "0" },
        { "value.1", "1" },
        { "value.2", "2" },
        { "value.3", "3" },
        { "value.4", "4" },
        { "value.5", "5" },
        { "value.6", "6" },
        { "value.7", "7" },
        { "value.8", "8" },
        { "value.9", "9" },
        { "value.skip", "Skip" },
        { "value.reverse", "Reverse" },
        { "value.drawTwo", "Draw Two" },
        { "value.wild", "Wild" },
        { "value.wildDrawFour", "Wild +4" },
        { "value.unknown", "Unknown" },

        // ---- Why a card is or is not playable ----------------------------
        { "why.sameColor",
          "{card} can be played: same colour as the top card ({colour})" },
        { "why.activeColourMatch", "{card} can be played: matches the active colour ({colour})." },
        { "why.activeColourMismatch", "{card} cannot be played: active colour is {colour}." },
        { "why.sameValue",
          "{card} can be played: same value as the top card ({value})" },
        { "why.wild",
          "{card} can be played on anything" },
        { "why.wildDrawFour",
          "{card} can be played: choose a colour" },
        { "why.wildDrawFourBlocked",
          "{card} cannot be played: you still hold a {colour} card" },
        { "why.noMatch",
          "{card} cannot be played: the top card is {top}, so neither colour nor value matches" },
        { "why.unknownCard",
          "{card} is unresolved - set its colour and value first" },
        { "why.noTopCard",
          "Scan the discard pile first" },
        { "why.notReady",
          "No hand confirmed yet" },
        { "why.uncertain",
          "{card} (low confidence - please check)" },

        // ---- Tutor summary ----------------------------------------------
        { "tutor.canPlay", "Can play ({n}): {list}" },
        { "tutor.playableOne", "Playable: 1 card (green border)" },
        { "tutor.playableMany", "Playable: {n} cards (green border)" },
        { "tutor.activeColourLabel", "ACTIVE: {colour}" },
        { "tutor.callUno", "CALL UNO!" },
        { "scan.rescan", "Rescan" },
        { "scan.waitingHand", "Waiting for hand..." },
        { "scan.startDiscard", "Scan Discard to start." },
        { "scan.showDiscard", "Show the top card to the camera." },
        { "scan.autoHand", "Auto {seconds}s · Hold 0.5s" },
        { "scan.holding", "Holding... {seconds}s" },
        { "scan.locked", "LOCKED · Hold 0.5s" },
        { "scan.handScanning", "Scanning hand..." },
        { "scan.detectedOne", "1 card detected" },
        { "scan.detectedMany", "{n} cards detected" },
        { "scan.confirmedOne", "Confirmed 1 card." },
        { "scan.confirmedMany", "Confirmed {n} cards." },
        { "tutor.cannotPlay", "No playable cards - draw one" },
        { "tutor.notReady", "Confirm a hand and the discard card first" }
    };
}
