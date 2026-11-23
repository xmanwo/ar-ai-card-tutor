using System;
using P = CornerTrackMatcher.Point;

public static class CornerTrackMatcherChecks
{
    static int checks;
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }

    public static int Main()
    {
        var empty = CornerTrackMatcher.Match(new[] { new P(10, 10), new P(20, 10) }, new P[0], 128);
        Check(empty[0] == -1 && empty[1] == -1, "New neighbouring detections must create separate tracks.");
        var one = CornerTrackMatcher.Match(new[] { new P(1, 0), new P(2, 0) }, new[] { new P(0, 0) }, 128);
        Check(one[0] == 0 && one[1] == -1, "Two detections cannot reuse one track.");
        var reversed = CornerTrackMatcher.Match(new[] { new P(19, 0), new P(1, 0) }, new[] { new P(0, 0), new P(20, 0) }, 128);
        Check(reversed[0] == 1 && reversed[1] == 0, "Detector enumeration order must not merge nearby cards.");
        var far = CornerTrackMatcher.Match(new[] { new P(500, 500) }, new[] { new P(0, 0) }, 128);
        Check(far[0] == -1, "Out-of-range detections must remain unmatched.");
        var old = new P[8];
        var current = new P[8];
        for (int i = 0; i < 8; i++) { old[i] = new P(i*20, 100); current[7-i] = new P(i*20+1, 101); }
        var dense = CornerTrackMatcher.Match(current, old, 128);
        var seen = new bool[8];
        foreach (int id in dense)
        {
            Check(id >= 0 && !seen[id], "Eight dense corners must retain eight independent tracks.");
            seen[id] = true;
        }
        Console.WriteLine("Corner track regression checks passed: " + checks);
        return 0;
    }
}
