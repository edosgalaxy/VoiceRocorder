namespace VoiceRecorder;

public sealed class TrackItem
{
    public string Name { get; set; } = "";
    public string OriginalPath { get; set; } = "";
    public string? ReversePath { get; set; }
    public string Details { get; set; } = "";
    public bool HasReverse => !string.IsNullOrWhiteSpace(ReversePath) && File.Exists(ReversePath);
    public string ReverseStatus => HasReverse ? "✓ Перевёрнутая версия готова" : "Реверс ещё не создан";
}
