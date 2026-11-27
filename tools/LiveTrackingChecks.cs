using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

public static class LiveTrackingChecks
{
    static int checks;
    static void Check(bool value,string message) { if(!value) throw new Exception(message); checks++; }
    static void Near(float a,float b,string message) => Check(Math.Abs(a-b)<.001f,message);
    static LiveCardTracker.Detection D(string label,float x,float y=100,float confidence=.9f) =>
        new LiveCardTracker.Detection(label,confidence,new LiveCardTracker.Box(x,y,x+30,y+40));
    public static int Main(string[] args)
    {
        try { if(args.Length==3 && args[0]=="--replay") return Replay(args[1],args[2]); return Run(); }
        catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
    static int Run()
    {
        var tracker=new LiveCardTracker();
        Check(tracker.Update(1,0,new[]{D("Blue 5",100),D("Blue 5",180)},60),"First frame rejected.");
        Check(tracker.Current.Count==2,"Two identical cards must remain two tracks.");
        int left=tracker.Current[0].Id,right=tracker.Current[1].Id;
        Check(left!=right,"Repeated card labels cannot share an ID.");
        Check(tracker.Current[0].State==LiveCardTracker.Status.New,"A single observation is provisional.");
        var old=tracker.Current[0];
        tracker.Update(2,.3f,new[]{D("Blue 5",185),D("Blue 5",105)},60);
        Check(tracker.Current[0].Id==left && tracker.Current[1].Id==right,"Response order must not change identity.");
        Near(tracker.Current[0].Bounds.Left,105,"Left track associated with the wrong card.");
        Near(old.Bounds.Left,100,"Published snapshots must not mutate when the next frame arrives.");
        tracker.Update(3,.6f,new[]{D("Blue 5",110),D("Blue 5",190)},60);
        Check(tracker.Current[0].State==LiveCardTracker.Status.Stable,"Repeated consistent observations should become stable.");
        tracker.Update(4,.9f,new[]{D("Blue 5",195)},60);
        Check(tracker.Current[0].Id==left && tracker.Current[0].State==LiveCardTracker.Status.Missing,"Occlusion should show a missing track, not remove a card from game state.");
        Near(tracker.Current[0].MissingSeconds,.3f,"Missing duration is incorrect.");
        Check(tracker.Current[0].Bounds.Left>110,"Missing boxes should make only a bounded velocity prediction.");
        tracker.Update(5,1.15f,new[]{D("Blue 5",120),D("Blue 5",200)},60);
        Check(tracker.Current[0].Id==left && tracker.Current[0].State==LiveCardTracker.Status.Stable,"A briefly occluded card must recover its track.");
        Check(!tracker.Update(5,1.2f,new[]{D("Red 9",500)},60),"Duplicate replies must be rejected.");
        Check(!tracker.Update(4,1.3f,new[]{D("Red 9",500)},60),"Older frame replies must be rejected.");
        Check(!tracker.Update(6,1.0f,null,60),"Backward capture timestamps must be rejected.");
        Check(tracker.LastFrameId==5,"Rejected replies must not advance the acknowledged image.");
        tracker.Update(6,3,new[]{D("Blue 5",120)},60);
        Check(tracker.Current.Count==1 && tracker.Current[0].Id!=left,"Expired debug tracks must not silently revive after long loss.");
        var labels=new LiveCardTracker();
        labels.Update(1,0,new[]{D("Green 4",100)},50);int labelId=labels.Current[0].Id;
        labels.Update(2,.3f,new[]{D("Green 3",102)},50);
        Check(labels.Current[0].Id==labelId,"A classification change must not create a new identity.");
        for(int i=3;i<7;i++) labels.Update(i,i*.3f,new[]{D("Green 3",102)},50);
        Check(labels.Current[0].Label=="Green 3","Consensus must eventually accept a repeatedly corrected label.");
        var weak=new LiveCardTracker();
        for(int i=1;i<=4;i++) weak.Update(i,i*.3f,new[]{D("Wild",100,100,.3f)},50);
        Check(weak.Current[0].State==LiveCardTracker.Status.New,"Low-confidence repeats must not look fully stable.");
        var invalid=new LiveCardTracker();
        invalid.Update(1,0,new[]{new LiveCardTracker.Detection("Bad",.9f,new LiveCardTracker.Box(float.NaN,0,20,20)),D("Bad",100,100,float.PositiveInfinity),D("Bad",100,100,1.5f)},50);
        Check(invalid.Current.Count==0,"Invalid boxes/confidences must not reach UI geometry.");
        Check(!invalid.Update(2,float.NaN,null,50),"Invalid timestamps accepted.");
        Check(!invalid.Update(2,.3f,null,float.NaN),"Invalid match radius accepted.");
        var bounded=new LiveCardTracker{Capacity=2};
        bounded.Update(1,0,new[]{D("A",100),D("B",300),D("C",500)},50);
        Check(bounded.Current.Count==2,"The track pool must stay bounded.");
        bounded.Reset(); Check(bounded.Current.Count==0 && bounded.LastFrameId==-1,"New Live sessions must clear previous IDs/frames.");
        bounded.Update(1,0,new[]{D("A",100)},50);Check(bounded.Current[0].Id==1,"New session numbering should be explicit and deterministic.");

        var viewport=new HandCropGeometry.Crop(672,576,704,576);
        Check(TrackingOverlayGeometry.Project(new LiveCardTracker.Box(672,576,1376,1152),viewport,out var rect),"Crop viewport projection failed.");
        Near(rect.Left,0,"Crop x offset not removed."); Near(rect.Bottom,0,"Crop bottom must map to UI bottom.");
        Near(rect.Right,1,"Crop width scaling incorrect."); Near(rect.Top,1,"Crop top must map to UI top.");
        Check(TrackingOverlayGeometry.Project(new LiveCardTracker.Box(650,550,800,700),viewport,out rect),"Partially visible track should clip to the crop.");
        Near(rect.Left,0,"Partial left clipping failed."); Near(rect.Top,1,"Partial top clipping failed.");
        Check(rect.Right>0 && rect.Right<1 && rect.Bottom>0 && rect.Bottom<1,"Clipped coordinates must stay within the upload preview.");
        Check(!TrackingOverlayGeometry.Project(new LiveCardTracker.Box(50,50,100,100),viewport,out rect),"A full-image track outside the ROI must not appear in its crop.");
        Check(!TrackingOverlayGeometry.Project(new LiveCardTracker.Box(10,10,5,5),viewport,out rect),"Reversed boxes accepted by the overlay.");
        Check(!TrackingOverlayGeometry.Project(new LiveCardTracker.Box(0,0,10,10),default,out rect),"Zero-sized image viewport accepted.");
        Check(TrackingOverlayGeometry.Project(new LiveCardTracker.Box(100,50,300,150),new HandCropGeometry.Crop(0,0,1000,500),out rect),"Non-square original projection failed.");
        Near(rect.Left,.1f,"Original x scaling incorrect."); Near(rect.Right,.3f,"Original width scaling incorrect.");
        Near(rect.Top,.9f,"Top-left JPEG origin must flip exactly once for Unity UI."); Near(rect.Bottom,.7f,"Original y scaling incorrect.");
        Console.WriteLine("Live tracking and overlay checks passed: "+checks);
        return 0;
    }

    // Literal recorded-frame replay for offline UI inspection; no accuracy claim or cloud call.
    static int Replay(string input,string output)
    {
        var tracker=new LiveCardTracker();
        var detections=new List<LiveCardTracker.Detection>();
        int frame=-1,width=0,height=0;float time=0;
        using(var writer=new StreamWriter(output))
        {
            Action flush=()=>{
                if(frame<0)return;
                tracker.Update(frame,time,detections,(float)Math.Sqrt((double)width*width+(double)height*height)*.08f);
                foreach(var t in tracker.Current)
                    writer.WriteLine(string.Join("|",frame,t.Id,t.Label,t.Confidence.ToString(CultureInfo.InvariantCulture),t.State,
                        t.Bounds.Left.ToString(CultureInfo.InvariantCulture),t.Bounds.Top.ToString(CultureInfo.InvariantCulture),
                        t.Bounds.Right.ToString(CultureInfo.InvariantCulture),t.Bounds.Bottom.ToString(CultureInfo.InvariantCulture)));
            };
            foreach(string line in File.ReadAllLines(input))
            {
                var p=line.Split('|');
                if(p[0]=="F")
                { flush();frame=int.Parse(p[1]);time=float.Parse(p[2],CultureInfo.InvariantCulture);width=int.Parse(p[3]);height=int.Parse(p[4]);detections.Clear(); }
                else if(p[0]=="D") detections.Add(new LiveCardTracker.Detection(p[1],float.Parse(p[2],CultureInfo.InvariantCulture),
                    new LiveCardTracker.Box(float.Parse(p[3],CultureInfo.InvariantCulture),float.Parse(p[4],CultureInfo.InvariantCulture),
                        float.Parse(p[5],CultureInfo.InvariantCulture),float.Parse(p[6],CultureInfo.InvariantCulture))));
            }
            flush();
        }
        Console.WriteLine("Recorded-frame replay written: "+output);
        return 0;
    }
}
