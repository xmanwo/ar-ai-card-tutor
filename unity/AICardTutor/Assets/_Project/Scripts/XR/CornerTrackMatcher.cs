using System;
using System.Collections.Generic;

/// <summary>One detection and one existing track can each participate at most once per frame.</summary>
public static class CornerTrackMatcher
{
    public readonly struct Point
    {
        public readonly float X, Y;
        public Point(float x, float y) { X = x; Y = y; }
    }

    readonly struct Edge
    {
        public readonly int Detection, Track;
        public readonly float DistanceSquared;
        public Edge(int detection, int track, float distance)
        { Detection = detection; Track = track; DistanceSquared = distance; }
    }

    public static int[] Match(Point[] detections, Point[] tracks, float radius)
    {
        var result = new int[detections.Length];
        for (int i = 0; i < result.Length; i++) result[i] = -1;
        var used = new bool[tracks.Length];
        var edges = new List<Edge>();
        float limit = radius * radius;
        for (int d = 0; d < detections.Length; d++)
            for (int t = 0; t < tracks.Length; t++)
            {
                float dx = detections[d].X - tracks[t].X;
                float dy = detections[d].Y - tracks[t].Y;
                float distance = dx * dx + dy * dy;
                if (distance < limit) edges.Add(new Edge(d, t, distance));
            }
        edges.Sort((a, b) => {
            int order = a.DistanceSquared.CompareTo(b.DistanceSquared);
            if (order != 0) return order;
            order = a.Detection.CompareTo(b.Detection);
            return order != 0 ? order : a.Track.CompareTo(b.Track);
        });
        foreach (var edge in edges)
        {
            if (result[edge.Detection] >= 0 || used[edge.Track]) continue;
            result[edge.Detection] = edge.Track;
            used[edge.Track] = true;
        }
        return result;
    }
}
