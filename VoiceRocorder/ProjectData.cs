namespace VoiceRocorder;

public sealed class ProjectData
{
    public List<TrackData> Tracks { get; set; } = new();
}
public sealed class TrackData
{
    public string Name { get; set; } = "";
    public string OriginalFile { get; set; } = "";
    public string? ReverseFile { get; set; }
    public bool IsReversed { get; set; }
}
