using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VoiceRocorder;

public sealed class TrackItem : INotifyPropertyChanged
{
    private bool _isReversed;
    public string Name { get; set; } = "Track";
    public string OriginalPath { get; set; } = "";
    public string? ReversePath { get; set; }
    public bool IsReversed
    {
        get => _isReversed;
        set { _isReversed = value; OnPropertyChanged(); OnPropertyChanged(nameof(Mode)); }
    }
    public string Mode => IsReversed ? "Reverse" : "Original";
    public string DisplayName => $"{Name}  [{Mode}]";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
