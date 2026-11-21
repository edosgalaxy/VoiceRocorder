using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VoiceRocorder;

public sealed class TrackItem : INotifyPropertyChanged
{
    private bool _isReversed;
    public string Name { get; set; } = "Дорожка";
    public string OriginalPath { get; set; } = "";
    public string? ReversePath { get; set; }
    public string DurationText { get; set; } = "";
    public bool IsReversed
    {
        get => _isReversed;
        set { _isReversed = value; OnPropertyChanged(); OnPropertyChanged(nameof(Mode)); }
    }
    public string Mode => IsReversed ? "Реверс выбран" : "Оригинал выбран";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
