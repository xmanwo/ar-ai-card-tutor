using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Replay identical replies through the actual manual and automatic Scan Hand round pipeline.
/// Uses an isolated preview scene; no camera, recognition server or paid API calls.</summary>
public static class LivePreviewSmokeCheck
{
    static int checks;
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    static void Set(object target,string name,object value)
    {
        var type=target.GetType();var field=type.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);
        if(field!=null)field.SetValue(target,value);
        else type.GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(target,value);
    }
    static object Get(object target,string name)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
    static object Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);
    sealed class Source:ICardScanSource
    {
        public string DisplayName=>"Offline Scan Hand parity fixture";public bool IsAvailable=>true;
        public IEnumerator Scan(ScanFrame frame,Action<CardScanResult> ok,Action<string> error){yield break;}
    }
    static Texture2D Texture(int size,Color color)
    {
        var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);var pixels=new Color[size*size];
        for(int i=0;i<pixels.Length;i++)pixels[i]=color;texture.SetPixels(pixels);texture.Apply();return texture;
    }
    static List<CardEntry> Cards(params string[] labels)
    {
        var list=new List<CardEntry>();for(int i=0;i<labels.Length;i++)list.Add(CardEntry.FromLabel(labels[i],i==1 ? .35f : .9f,new float[]{50+i*55,150,75+i*55,180}));return list;
    }
    static string Signature(IReadOnlyList<CardEntry> cards)
    {string s="";foreach(var c in cards)s+=UnoCardKey.For(c.color,c.value)+":"+c.uncertain+";";return s;}
    static CardScanClient Client(Scene scene,string name,FrameCapture capture)
    {
        var fixture=new GameObject(name);SceneManager.MoveGameObjectToScene(fixture,scene);
        var client=fixture.AddComponent<CardScanClient>();client.capture=capture;Set(client,"source",new Source());return client;
    }
    static void ReplayRound(CardScanClient client,FrameCapture capture,int frameBase,List<CardEntry> cards,bool injectError=false)
    {
        Call(client,"BeginRound");Set(client,"scanStartedAt",Time.unscaledTime-2f);
        for(int request=1;request<=8;request++)
        {
            var reply=new List<CardEntry>(cards);
            if(injectError && request==2 && reply.Count>0)reply[0]=CardEntry.FromLabel("Red 0",.99f,(float[])cards[0].bbox.Clone());
            int id=frameBase+request;Set(capture,"LastFrameId",id);
            ((ScanPreviewFrame)Get(client,"pendingPreview")).Capture(capture,ScanTarget.Hand,Time.unscaledTime);
            Call(client,"PublishPreview",new ScanFrame{frameId=id,fullWidth=512,fullHeight=512},Time.unscaledTime,reply);
            Set(client,"requestCount",request);Set(client,"roundReplyCount",request);Set(client,"lastRoundReplyAt",Time.unscaledTime);
            Call(client,"ApplyDetections",reply,client.DisplayPreview.Original);
            if((bool)Call(client,"TryFreeze",reply.Count))return;
        }
        throw new Exception("The shared Scan Hand freeze rules failed to finish a stable replay.");
    }
    [MenuItem("Tools/AR AI Card Tutor/Check live preview isolation")]
    public static void Run()
    {
        checks=0;var scene=EditorSceneManager.NewPreviewScene();Texture2D full=null,crop=null;
        try
        {
            var root=new GameObject("CaptureFixture");SceneManager.MoveGameObjectToScene(root,scene);var capture=root.AddComponent<FrameCapture>();
            capture.handRoi=root.GetComponent<HandRoiProvider>();if(capture.handRoi==null)capture.handRoi=root.AddComponent<HandRoiProvider>();
            full=Texture(512,Color.red);crop=Texture(256,Color.blue);
            Set(capture,"LastTexture",full);Set(capture,"LastRecognitionTexture",crop);
            Set(capture,"LastScanCrop",new HandCropGeometry.Crop(0,64,256,256));Set(capture,"LastScanCropped",true);Set(capture,"LastCropNote","Scan Hand fixture crop");
            var initial=Cards("Yellow 7","Green 4","Green 4","Red 9","Blue 8");
            var manual=Client(scene,"ManualScan",capture);ReplayRound(manual,capture,0,initial,true);
            Check(manual.State==ScanState.Reviewing && manual.Cards.Count==5,"Manual Scan Hand fixture did not freeze five cards.");
            string manualResult=Signature(manual.Cards);
            var live=Client(scene,"LiveScan",capture);var session=live.gameObject.AddComponent<GameSessionState>();
            Check(session.TrySave(ScanTarget.Discard,Cards("Yellow 2"),null,out var error),error);
            Check(live.ToggleLiveTracking(session) && live.State==ScanState.Scanning,"Live did not start an ordinary Scan Hand round without hand Confirm.");
            ReplayRound(live,capture,100,initial,true);
            Check(Signature(live.LiveLatestHand)==manualResult,"Live changed the manual Scan Hand result, class votes, uncertain cards or duplicate count.");
            Check(live.LiveLatestHand.Count==5 && session.HasHand && session.Hand.Count==5,"A Live-only filter dropped cards from the manual result.");
            Check(capture.handRoi.HasLiveFocus,"A reliable first round did not establish reusable focus.");
            var reliableCrop=capture.handRoi.GetCrop(null,512,512,ScanTarget.Hand);
            Check(capture.handRoi.LastCropped,"Temporary missing hand/camera tracking replaced the trusted crop with full frame.");
            capture.handRoi.RecordResult(new List<CardEntry>());capture.handRoi.RecordResult(new List<CardEntry>());
            var heldCrop=capture.handRoi.GetCrop(null,512,512,ScanTarget.Hand);
            Check(capture.handRoi.HasLiveFocus && capture.handRoi.LastCropped && heldCrop.X==reliableCrop.X && heldCrop.Width==reliableCrop.Width,
                "Two empty raw replies erased focus before a complete round could assess the hand.");
            capture.handRoi.AcceptLiveRound(Cards("Red 0"),512,512,false);
            var rejectedCrop=capture.handRoi.GetCrop(null,512,512,ScanTarget.Hand);
            Check(rejectedCrop.X==heldCrop.X && rejectedCrop.Width==heldCrop.Width,"An unreliable result replaced the last reliable input region.");
            Check(live.DisplayPreview.Original.GetPixel(0,0)==Color.red && live.DisplayPreview.Recognition.GetPixel(0,0)==Color.blue,"Live photos do not match the acknowledged original and actual Scan Hand crop.");
            var panelObject=new GameObject("Panel",typeof(RectTransform),typeof(Canvas));SceneManager.MoveGameObjectToScene(panelObject,scene);panelObject.SetActive(false);
            var owner=panelObject.AddComponent<ScanPanel>();owner.client=live;owner.session=session;
            owner.frameImage=new GameObject("Original",typeof(RectTransform),typeof(RawImage)).GetComponent<RawImage>();owner.frameImage.transform.SetParent(panelObject.transform,false);
            owner.statusText=new GameObject("Status",typeof(RectTransform),typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();owner.statusText.transform.SetParent(panelObject.transform,false);owner.statusText.font=TMP_Settings.defaultFontAsset;
            owner.savedStateText=new GameObject("Saved",typeof(RectTransform),typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();owner.savedStateText.transform.SetParent(panelObject.transform,false);
            owner.listText=new GameObject("List",typeof(RectTransform),typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();owner.listText.transform.SetParent(panelObject.transform,false);
            var view=panelObject.GetComponent<CardHandView>();if(view==null)view=panelObject.AddComponent<CardHandView>();view.Initialize(owner);Set(owner,"cardFaces",view);view.RefreshPhotoPanel();Call(owner,"RefreshLivePresentation");
            var panel=panelObject.transform.Find("LiveTrackingPanel");var faces=panel.Find("LiveCardFaces");var row=faces.Find("LiveHandRow");var played=faces.Find("PlayedCards");
            Check(faces.gameObject.activeSelf && row.childCount==5,"Live did not default to the latest Scan Hand as overlapping card textures.");
            Check(!owner.listText.gameObject.activeSelf && !owner.savedStateText.gameObject.activeSelf,"Old text obscures the card fan.");
            var firstFace=row.GetChild(0);var photo=live.DisplayPreview;
            Call(live,"BeginRound");view.RefreshPhotoPanel();
            Check(row.childCount==5 && row.GetChild(0)==firstFace && live.DisplayPreview==photo,"Beginning the next Rescan cleared the displayed hand or its photos.");
            Check(capture.handRoi.HasLiveFocus && (string)Get(live,"liveFocusMode")=="following last reliable hand","Next round reset the reliable hand focus.");
            var drawn=Cards("Yellow 7","Green 4","Green 4","Red 9","Blue 8","Blue 5");ReplayRound(live,capture,200,drawn);view.RefreshPhotoPanel();
            Check(row.childCount==6 && session.Hand.Count==6 && live.LivePlayStatus.Contains("Drew 1"),"An extra completed-scan card did not appear as drawing.");
            Check(!(bool)Get(live,"nextLiveFullRescan") && capture.handRoi.HasLiveFocus,"Drawing forced an unnecessary full reacquisition.");
            var reduced=Cards("Green 4","Green 4","Red 9","Blue 8","Blue 5");ReplayRound(live,capture,300,reduced);view.RefreshPhotoPanel();
            Check(row.childCount==5 && played.childCount==0 && session.Hand.Count==6,"First missing scan was not displayed or was counted as played too early.");
            Check(!(bool)Get(live,"nextLiveFullRescan") && capture.handRoi.HasLiveFocus,"One missing card unnecessarily cleared the followed region.");
            ReplayRound(live,capture,400,reduced);view.RefreshPhotoPanel();
            Check(row.childCount==5 && played.childCount==1 && session.Hand.Count==5,"Two completed missing scans did not move the legal Yellow 7 to Discard.");
            Check(played.GetChild(0).Find("Card").GetComponent<Image>().sprite==UnoCardArt.GetByKey("Y7"),"Discard uses the wrong card texture.");
            var illegal=Cards("Green 4","Red 9","Blue 8","Blue 5");ReplayRound(live,capture,500,illegal);ReplayRound(live,capture,600,illegal);view.RefreshPhotoPanel();
            Check(row.childCount==4 && played.childCount==1 && session.Hand.Count==5 && live.LivePlayStatus.Contains("not playable"),"Illegal disappearance was played, or the newest Scan Hand result was hidden behind the ledger.");
            faces.Find("UndoLivePlay").GetComponent<Button>().onClick.Invoke();view.RefreshPhotoPanel();
            Check(row.childCount==6 && played.childCount==0 && live.Cards.Count==6,"Undo did not restore fan/ledger/draft.");
            Call(live,"MarkLiveFailure","Offline fixture");view.RefreshPhotoPanel();
            Check(row.childCount==6 && live.LiveRetrySeconds>0 && !live.CanShowTrackingBoxes,"Network error cleared the hand or did not schedule an automatic retry.");
            panel.Find("LiveDisplayMode").GetComponent<Button>().onClick.Invoke();view.RefreshPhotoPanel();live.StopLiveTracking();view.RefreshPhotoPanel();
            Check(!panel.gameObject.activeSelf,"Stopping Live left its panel visible.");
            Check(live.ToggleLiveTracking(session),"Live cannot restart.");view.RefreshPhotoPanel();
            Check(faces.gameObject.activeSelf && !panel.Find("TrackingDebugView").gameObject.activeSelf,"Restarting Live did not return to stacked cards.");
            live.StopLiveTracking();
            var acquire=Client(scene,"FocusAcquisition",capture);var acquireSession=acquire.gameObject.AddComponent<GameSessionState>();
            acquireSession.TrySave(ScanTarget.Discard,Cards("Yellow 2"),null,out _);acquire.ToggleLiveTracking(acquireSession);
            var low=Cards("Yellow 7","Green 4","Green 4","Red 9","Blue 8");foreach(var card in low)card.confidence=.3f;
            ReplayRound(acquire,capture,800,low);
            Check(!capture.handRoi.HasLiveFocus,"A poor initial round locked in an unverified region.");
            ReplayRound(acquire,capture,900,initial);
            Check(capture.handRoi.HasLiveFocus,"A later reliable round could not establish focus after poor initial recognition.");
            ReplayRound(acquire,capture,1000,Cards("Yellow 8","Green 5","Green 4","Red 9","Blue 8"));
            Check((bool)Get(acquire,"nextLiveFullRescan"),"Two changed cards failed to request full reacquisition.");
            Call(acquire,"BeginRound");var searching=capture.handRoi.GetCrop(null,512,512,ScanTarget.Hand);
            Check(!capture.handRoi.HasLiveFocus && !capture.handRoi.LastCropped && searching.Width==512,
                "A requested thorough rescan retained the stale crop instead of starting from full frame.");
            ReplayRound(acquire,capture,1100,initial);Call(acquire,"BeginRound");
            Check(capture.handRoi.HasLiveFocus && (string)Get(acquire,"liveFocusMode")=="following last reliable hand",
                "Successful full reacquisition did not resume continued following.");acquire.StopLiveTracking();
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../output/live_tracking"));Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"unity_smoke.json"),"{\"checks_passed\":"+checks+",\"headset_verified\":false,\"manual_and_live_round_parity\":true}");
            Debug.Log("Live Scan Hand parity / UI checks passed: "+checks);
        }
        catch(Exception error){Debug.LogException(error);throw;}
        finally
        {
            if(full!=null)UnityEngine.Object.DestroyImmediate(full);if(crop!=null)UnityEngine.Object.DestroyImmediate(crop);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
