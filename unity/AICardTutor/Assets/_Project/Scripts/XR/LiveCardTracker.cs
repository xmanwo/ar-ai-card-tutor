using System;
using System.Collections.Generic;

/// <summary>Preview-only tracks in full-image pixels. IDs never modify confirmed game state.</summary>
public sealed class LiveCardTracker
{
    public enum Status { New, Stable, Missing }
    public readonly struct Box
    {
        public readonly float Left, Top, Right, Bottom;
        public Box(float left, float top, float right, float bottom)
        { Left = left; Top = top; Right = right; Bottom = bottom; }
        public float X => (Left + Right)*.5f;
        public float Y => (Top + Bottom)*.5f;
        public bool Valid => HandCropGeometry.Finite(Left) && HandCropGeometry.Finite(Top) &&
            HandCropGeometry.Finite(Right) && HandCropGeometry.Finite(Bottom) && Right > Left && Bottom > Top;
        public Box Move(float x, float y) => new Box(Left+x, Top+y, Right+x, Bottom+y);
    }
    public readonly struct Detection
    {
        public readonly string Label;
        public readonly float Confidence;
        public readonly Box Bounds;
        public Detection(string label, float confidence, Box bounds)
        { Label = label ?? "Unknown"; Confidence = confidence; Bounds = bounds; }
    }
    public sealed class Snapshot
    {
        public int Id { get; }
        public string Label { get; }
        public float Confidence { get; }
        public Box Bounds { get; }
        public Status State { get; }
        public int Hits { get; }
        public float MissingSeconds { get; }
        public string RawLabel { get; }
        public float RawConfidence { get; }
        public Snapshot(int id, string label, float confidence, Box bounds, Status state, int hits, float missing,string rawLabel=null,float rawConfidence=-1)
        { Id=id; Label=label; Confidence=confidence; Bounds=bounds; State=state; Hits=hits; MissingSeconds=missing;
            RawLabel=rawLabel ?? label;RawConfidence=rawConfidence>=0 ? rawConfidence : confidence; }
    }
    sealed class Track
    {
        public int Id, Hits;
        public float LastSeen, Vx, Vy;
        public Box Bounds;
        public string RawLabel;
        public float RawConfidence;
        public readonly Dictionary<string, float> Votes = new Dictionary<string, float>();
        public readonly Dictionary<string, float> Scores = new Dictionary<string, float>();
    }
    readonly List<Track> tracks = new List<Track>();
    int nextId = 1, lastFrame = -1;
    float lastTime = -1;
    public float ExpirySeconds = 1.2f;
    public int StableHits = 3;
    public int Capacity = 64;
    public bool LabelAwareAssociation;
    public IReadOnlyList<Snapshot> Current { get; private set; } = Array.Empty<Snapshot>();
    public int LastFrameId => lastFrame;

    public void Reset()
    { tracks.Clear(); Current=Array.Empty<Snapshot>(); nextId=1; lastFrame=-1; lastTime=-1; }

    public bool Update(int frameId, float time, IReadOnlyList<Detection> input, float matchRadius)
    {
        if (frameId <= lastFrame || !HandCropGeometry.Finite(time) || time < lastTime ||
            !HandCropGeometry.Finite(matchRadius) || matchRadius <= 0) return false;
        lastFrame=frameId; lastTime=time;
        tracks.RemoveAll(track => time-track.LastSeen > Math.Max(.1f, ExpirySeconds));
        var valid = new List<Detection>();
        if (input != null)
            foreach (var item in input)
                if (item.Bounds.Valid && HandCropGeometry.Finite(item.Confidence) && item.Confidence >= 0 && item.Confidence <= 1)
                { valid.Add(item); if (valid.Count >= Math.Max(1, Capacity)) break; }
        var points = new CornerTrackMatcher.Point[valid.Count];
        for (int i=0; i<valid.Count; i++) points[i]=new CornerTrackMatcher.Point(valid[i].Bounds.X, valid[i].Bounds.Y);
        var previous = new CornerTrackMatcher.Point[tracks.Count];
        for (int i=0; i<tracks.Count; i++)
        {
            Box predicted = Predict(tracks[i], time);
            previous[i]=new CornerTrackMatcher.Point(predicted.X, predicted.Y);
        }
        int[] matches=LabelAwareAssociation ? Associate(valid,points,previous,time,matchRadius) : CornerTrackMatcher.Match(points, previous, matchRadius);
        var seen = new HashSet<int>();
        for (int i=0; i<valid.Count; i++)
        {
            Track track = matches[i] >= 0 ? tracks[matches[i]] : null;
            if (track == null)
            {
                if (tracks.Count >= Math.Max(1, Capacity)) continue;
                track = new Track { Id=nextId++, Bounds=valid[i].Bounds, LastSeen=time };
                tracks.Add(track);
            }
            float dt = time-track.LastSeen;
            if (track.Hits > 0 && dt > .001f)
            {
                float vx=(valid[i].Bounds.X-track.Bounds.X)/dt, vy=(valid[i].Bounds.Y-track.Bounds.Y)/dt;
                float limit=matchRadius/Math.Max(.05f, dt)*2;
                track.Vx=(track.Vx+Clamp(vx,-limit,limit))*.5f;
                track.Vy=(track.Vy+Clamp(vy,-limit,limit))*.5f;
            }
            track.Hits++; track.LastSeen=time; track.Bounds=valid[i].Bounds;
            track.RawLabel=valid[i].Label;track.RawConfidence=valid[i].Confidence;
            // Label changes do not change identity. Keep a short weighted class consensus.
            foreach (string key in new List<string>(track.Votes.Keys)) track.Votes[key]*=.75f;
            if (!track.Votes.ContainsKey(valid[i].Label) && track.Votes.Count >= 16)
            {
                string weakest=null; float score=float.MaxValue;
                foreach (var vote in track.Votes) if (vote.Value<score) { weakest=vote.Key; score=vote.Value; }
                track.Votes.Remove(weakest); track.Scores.Remove(weakest);
            }
            track.Votes.TryGetValue(valid[i].Label, out float votes);
            track.Votes[valid[i].Label]=votes+valid[i].Confidence;
            track.Scores[valid[i].Label]=valid[i].Confidence;
            seen.Add(track.Id);
        }
        var output = new List<Snapshot>();
        foreach (var track in tracks)
        {
            string label="Unknown"; float score=-1, total=0;
            foreach (var vote in track.Votes)
            { total+=vote.Value; if (vote.Value>score) { score=vote.Value; label=vote.Key; } }
            float confidence=track.Scores.TryGetValue(label,out float value) ? value : 0;
            bool missing=!seen.Contains(track.Id);
            Status state=missing ? Status.Missing : track.Hits>=StableHits && confidence>=.45f &&
                score/Math.Max(.001f,total)>=.6f ? Status.Stable : Status.New;
            output.Add(new Snapshot(track.Id,label,confidence,missing ? Predict(track,time) : track.Bounds,
                state,track.Hits,missing ? Math.Max(0,time-track.LastSeen) : 0,track.RawLabel,track.RawConfidence));
        }
        output.Sort((a,b)=>a.Id.CompareTo(b.Id));
        Current=output.AsReadOnly();
        return true;
    }

    static Box Predict(Track track, float time)
    { float dt=Clamp(time-track.LastSeen,0,.6f); return track.Bounds.Move(track.Vx*dt,track.Vy*dt); }
    static float Clamp(float n,float min,float max) => Math.Max(min,Math.Min(max,n));

    string Label(Track track)
    {string label="Unknown";float best=-1;foreach(var vote in track.Votes)if(vote.Value>best){best=vote.Value;label=vote.Key;}return label;}
    int[] Associate(List<Detection> detections,CornerTrackMatcher.Point[] current,CornerTrackMatcher.Point[] previous,float time,float radius)
    {
        // Median common motion prevents a camera/hand translation from swapping neighbours.
        var dxs=new List<float>();var dys=new List<float>();
        for(int d=0;d<detections.Count;d++)
        {
            int matching=-1,count=0;for(int t=0;t<tracks.Count;t++)if(Label(tracks[t])==detections[d].Label){matching=t;count++;}
            int same=0;foreach(var item in detections)if(item.Label==detections[d].Label)same++;
            if(count!=1 || same!=1 || detections[d].Confidence<.45f)continue;
            float dx=current[d].X-previous[matching].X,dy=current[d].Y-previous[matching].Y;
            if(dx*dx+dy*dy<radius*radius*4){dxs.Add(dx);dys.Add(dy);}
        }
        if(dxs.Count>=3)
        {
            dxs.Sort();dys.Sort();float dx=dxs[dxs.Count/2],dy=dys[dys.Count/2];
            for(int t=0;t<tracks.Count;t++){tracks[t].Bounds=tracks[t].Bounds.Move(dx,dy);previous[t]=new CornerTrackMatcher.Point(previous[t].X+dx,previous[t].Y+dy);}
        }
        var result=new int[current.Length];for(int i=0;i<result.Length;i++)result[i]=-1;
        var used=new bool[previous.Length];
        var edges=new List<Tuple<float,int,int>>();
        // Strong geometric continuity may survive a single wrong class label.
        for(int d=0;d<current.Length;d++)
        {
            int nearest=-1;float first=float.MaxValue,second=float.MaxValue;
            for(int t=0;t<previous.Length;t++)
            {float x=current[d].X-previous[t].X,y=current[d].Y-previous[t].Y,value=x*x+y*y;
                if(value<first){second=first;first=value;nearest=t;}else second=Math.Min(second,value);}
            if(nearest>=0 && first<radius*radius*.0144f && second>Math.Max(first*4,radius*radius*.04f))
                edges.Add(Tuple.Create(first,d,nearest));
        }
        Assign(edges,result,used);edges.Clear();
        for(int d=0;d<current.Length;d++)for(int t=0;t<previous.Length;t++)
        {
            if(result[d]>=0 || used[t] || detections[d].Label!=Label(tracks[t]))continue;
            float x=current[d].X-previous[t].X,y=current[d].Y-previous[t].Y;
            float recovery=radius*(1+Math.Min(1.5f,time-tracks[t].LastSeen)*.75f),distance=x*x+y*y;
            if(distance<recovery*recovery)edges.Add(Tuple.Create(distance,d,t));
        }
        Assign(edges,result,used);edges.Clear();
        for(int d=0;d<current.Length;d++)for(int t=0;t<previous.Length;t++)
        {
            if(result[d]>=0 || used[t])continue;float x=current[d].X-previous[t].X,y=current[d].Y-previous[t].Y,value=x*x+y*y;
            if(value<radius*radius*.1225f)edges.Add(Tuple.Create(value,d,t));
        }
        Assign(edges,result,used);return result;
    }
    static void Assign(List<Tuple<float,int,int>> edges,int[] result,bool[] used)
    {
        edges.Sort((a,b)=>{int c=a.Item1.CompareTo(b.Item1);if(c!=0)return c;c=a.Item2.CompareTo(b.Item2);return c!=0 ? c : a.Item3.CompareTo(b.Item3);});
        foreach(var edge in edges)if(result[edge.Item2]<0 && !used[edge.Item3]){result[edge.Item2]=edge.Item3;used[edge.Item3]=true;}
    }
}

/// <summary>Clips full-image boxes into either the original photo or its actual upload crop.</summary>
public static class TrackingOverlayGeometry
{
    public readonly struct Rect
    {
        public readonly float Left, Bottom, Right, Top;
        public Rect(float left,float bottom,float right,float top)
        { Left=left; Bottom=bottom; Right=right; Top=top; }
    }
    public static bool Project(LiveCardTracker.Box box, HandCropGeometry.Crop viewport, out Rect rect)
    {
        rect=default;
        if (!box.Valid || viewport.Width<=0 || viewport.Height<=0) return false;
        float left=Math.Max(box.Left,viewport.X), right=Math.Min(box.Right,viewport.X+viewport.Width);
        float top=Math.Max(box.Top,viewport.Y), bottom=Math.Min(box.Bottom,viewport.Y+viewport.Height);
        if (right<=left || bottom<=top) return false;
        rect=new Rect((left-viewport.X)/viewport.Width,1-(bottom-viewport.Y)/viewport.Height,
            (right-viewport.X)/viewport.Width,1-(top-viewport.Y)/viewport.Height);
        return true;
    }
}
