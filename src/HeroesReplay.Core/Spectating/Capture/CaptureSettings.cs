namespace HeroesReplay.Core.Spectating.Capture;

public class CaptureSettings
{
    public CaptureMethod Method { get; set; }
    public bool SaveTimerRegion { get; set; }
    public bool SaveCaptureFailureCondition { get; set; }
}
