using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only checks for the rules and for the tutor chain.
///
/// Two menus, on purpose:
///
///   Run Rule Tests   - builds card lists in memory and calls the rule engine directly. Touches no
///                      scene object, needs no Play mode, writes no file. Use it to answer "is the
///                      rule right?".
///
///   Fill Sample Hand - runs only in Play mode and writes a fixed position into the live
///                      GameSessionState, then asks TutorService to evaluate. Use it to answer
///                      "is the whole chain wired?" - the panel should print English sentences.
///
/// Nothing here ships: the file lives under Editor/, so Unity keeps it out of player builds.
/// </summary>
public static class TutorDebugMenu
{
    const string Menu = "Tools/AI Card Tutor/Debug/";

    // ---- Fill a position into the running scene -------------------------

    [MenuItem(Menu + "Fill Sample Hand", priority = 10)]
    public static void FillSampleHand()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Debug] Enter Play mode first. The session and the tutor only " +
                             "exist at runtime, and editing them outside Play mode would dirty " +
                             "the scene.");
            return;
        }

        var session = UnityEngine.Object.FindFirstObjectByType<GameSessionState>();

        if (session == null)
        {
            Debug.LogError("[Debug] No GameSessionState in the scene. Add one or check that " +
                           "the GameSession object is active.");
            return;
        }

        // A position with three playable cards and two dead ones, so the panel has something to
        // say in every list.
        var hand = new List<CardEntry>
        {
            CardEntry.FromUser("Red", "7"),
            CardEntry.FromUser("Red", "Skip"),
            CardEntry.FromUser("Blue", "5"),
            CardEntry.FromUser("", "Wild"),
            CardEntry.FromUser("Green", "3")
        };

        if (!session.TrySave(ScanTarget.Hand, hand, null, out string error))
        {
            Debug.LogError($"[Debug] hand rejected: {error}");
            return;
        }

        var top = new List<CardEntry> { CardEntry.FromUser("Red", "3") };

        if (!session.TrySave(ScanTarget.Discard, top, null, out error))
        {
            Debug.LogError($"[Debug] discard rejected: {error}");
            return;
        }

        Debug.Log($"[Debug] position set - hand {session.Hand.Count}, " +
                  $"top {session.Discard.DisplayName}, colour {session.ActiveColor}");

        var tutor = UnityEngine.Object.FindFirstObjectByType<TutorService>();

        if (tutor == null)
        {
            Debug.LogWarning("[Debug] No TutorService in the scene. The session is filled but " +
                             "nothing will be evaluated.");
            return;
        }

        tutor.Evaluate();
    }

    // ---- Rule tests -----------------------------------------------------

    [MenuItem(Menu + "Run Rule Tests", priority = 20)]
    public static void RunRuleTests()
    {
        var rules = new StandardUnoRules(null);
        var cases = BuildCases();
        var report = new StringBuilder();
        int passed = 0;

        for (int i = 0; i < cases.Length; i++)
        {
            RuleCase c = cases[i];
            PlayCheck check = rules.Check(c.Card, c.Hand, c.Top, c.ActiveColor);

            bool ok = check.Legal == c.ExpectedLegal && check.Reason == c.ExpectedReason;

            if (ok)
                passed++;

            report.AppendLine($"[Rules] {(ok ? "PASS" : "FAIL")} {i + 1:00} " +
                              $"{c.Name} -> {check}");

            if (!ok)
            {
                report.AppendLine($"        expected: {(c.ExpectedLegal ? "playable" : "blocked")}" +
                                  $"/{c.ExpectedReason}");
            }
        }

        string summary = $"[Rules] {passed}/{cases.Length} passed";
        report.AppendLine(summary);

        if (passed == cases.Length)
            Debug.Log(report.ToString());
        else
            Debug.LogError(report.ToString());
    }

    /// <summary>
    /// The same ten positions written down in the P0 plan. Kept here rather than in a test
    /// assembly so the project does not need an asmdef split, which would break the assembly name
    /// recorded in the scene and prefab files.
    /// </summary>
    static RuleCase[] BuildCases()
    {
        return new[]
        {
            new RuleCase
            {
                Name = "red 7 vs red 3",
                Card = MakeCard("Red", "7"),
                Hand = new[] { MakeCard("Red", "7") },
                Top = MakeCard("Red", "3"),
                ActiveColor = "Red",
                ExpectedLegal = true,
                ExpectedReason = PlayReason.SameColor
            },
            new RuleCase
            {
                Name = "blue 7 vs red 3",
                Card = MakeCard("Blue", "7"),
                Hand = new[] { MakeCard("Blue", "7") },
                Top = MakeCard("Red", "3"),
                ActiveColor = "Red",
                ExpectedLegal = false,
                ExpectedReason = PlayReason.NoMatch
            },
            new RuleCase
            {
                Name = "red 7 vs blue 7",
                Card = MakeCard("Red", "7"),
                Hand = new[] { MakeCard("Red", "7") },
                Top = MakeCard("Blue", "7"),
                ActiveColor = "Blue",
                ExpectedLegal = true,
                ExpectedReason = PlayReason.SameValue
            },
            new RuleCase
            {
                Name = "red skip vs blue skip",
                Card = MakeCard("Red", "Skip"),
                Hand = new[] { MakeCard("Red", "Skip") },
                Top = MakeCard("Blue", "Skip"),
                ActiveColor = "Blue",
                ExpectedLegal = true,
                ExpectedReason = PlayReason.SameValue
            },
            new RuleCase
            {
                Name = "green skip vs green 3",
                Card = MakeCard("Green", "Skip"),
                Hand = new[] { MakeCard("Green", "Skip") },
                Top = MakeCard("Green", "3"),
                ActiveColor = "Green",
                ExpectedLegal = true,
                ExpectedReason = PlayReason.SameColor
            },
            new RuleCase
            {
                Name = "wild vs red 3",
                Card = MakeCard("", "Wild"),
                Hand = new[] { MakeCard("", "Wild") },
                Top = MakeCard("Red", "3"),
                ActiveColor = "Red",
                ExpectedLegal = true,
                ExpectedReason = PlayReason.WildCard
            },
            new RuleCase
            {
                Name = "+4 vs red 3, red still held",
                Card = MakeCard("Wild", "Draw 4"),
                Hand = new[] { MakeCard("Wild", "Draw 4"), MakeCard("Red", "9") },
                Top = MakeCard("Red", "3"),
                ActiveColor = "Red",
                ExpectedLegal = false,
                ExpectedReason = PlayReason.WildDrawFourBlocked
            },
            new RuleCase
            {
                Name = "+4 vs red 3, no red held",
                Card = MakeCard("Wild", "Draw 4"),
                Hand = new[] { MakeCard("Wild", "Draw 4"), MakeCard("Blue", "9") },
                Top = MakeCard("Red", "3"),
                ActiveColor = "Red",
                ExpectedLegal = true,
                ExpectedReason = PlayReason.WildDrawFour
            },
            new RuleCase
            {
                Name = "blue 5 vs wild top, colour blue",
                Card = MakeCard("Blue", "5"),
                Hand = new[] { MakeCard("Blue", "5") },
                Top = MakeCard("", "Wild"),
                ActiveColor = "Blue",
                ExpectedLegal = true,
                ExpectedReason = PlayReason.SameColor
            },
            new RuleCase
            {
                Name = "red 5 vs wild top, colour blue",
                Card = MakeCard("Red", "5"),
                Hand = new[] { MakeCard("Red", "5") },
                Top = MakeCard("", "Wild"),
                ActiveColor = "Blue",
                ExpectedLegal = false,
                ExpectedReason = PlayReason.NoMatch
            }
        };
    }

    static ConfirmedCard MakeCard(string color, string value)
    {
        return new ConfirmedCard(CardEntry.FromUser(color, value));
    }

    sealed class RuleCase
    {
        public string Name;
        public ConfirmedCard Card;
        public ConfirmedCard[] Hand;
        public ConfirmedCard Top;
        public string ActiveColor;
        public bool ExpectedLegal;
        public PlayReason ExpectedReason;
    }
}
