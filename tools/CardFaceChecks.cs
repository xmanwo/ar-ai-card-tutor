using System;
using System.Collections.Generic;

public static class CardFaceChecks
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    public static int Main()
    {
        var keys = new HashSet<string>();
        foreach (string color in new[] { "Red", "Blue", "Green", "Yellow" })
        {
            for (int n = 0; n < 10; n++) Check(keys.Add(UnoCardKey.For(color, n.ToString())), "Every numbered face needs a unique key.");
            foreach (string action in new[] { "Skip", "Reverse", "Draw Two" })
                Check(keys.Add(UnoCardKey.For(color, action)), "Action faces must not collide.");
        }
        Check(keys.Add(UnoCardKey.For("", "Wild")), "Wild face missing.");
        Check(keys.Add(UnoCardKey.For("Wild", "Draw 4")), "Wild Draw Four face missing.");
        Check(keys.Count == 54, "The game requires 54 distinct face types.");
        Check(UnoCardKey.For("green", "+2") == "GA2", "Green +2 alias mismatch.");
        Check(UnoCardKey.For("Red", "draw 2") == "RA2", "Draw 2 alias mismatch.");
        Check(UnoCardKey.For("", "Wild Draw Four") == "W4", "Wild Draw Four recognition alias mismatch.");
        Check(UnoCardKey.For("Blue", "Unknown") == null, "Unknown cards must not show an invented face.");
        Check(HandCardLayout.PageCount(0) == 1 && HandCardLayout.PageCount(10) == 1 && HandCardLayout.PageCount(11) == 2, "Hand pagination boundary mismatch.");
        Check(HandCardLayout.PageCount(21) == 3, "Large hands need all their pages.");
        foreach (int count in new[] { 1, 4, 8, 10 })
        {
            float last = -10000;
            for (int i = 0; i < count; i++)
            {
                float x = HandCardLayout.PositionX(i, count, 530, 96);
                Check(x-48 >= -265 && x+48 <= 265, "Cards must stay within the hand row.");
                Check(x > last, "Duplicate cards must retain separate positions.");
                last = x;
            }
        }
        Check(HandCardLayout.PositionX(0, 1, 530, 96) == 0, "A single card should be centered.");
        Console.WriteLine("Card face and layout checks passed: " + checks);
        foreach (string key in keys) Console.WriteLine("FACE " + key);
        return 0;
    }
}
