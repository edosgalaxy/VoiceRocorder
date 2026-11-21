using Microsoft.Win32;
using NAudio.Wave;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace VoiceRocorder;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<TrackItem> _tracks = new();
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _recordingPath;
    private WaveOutEvent? _player;
    private bool _stopping;
    private int _trackNumber = 1;
    private const int SampleRate = 44100;

    public MainWindow()
    {
        InitializeComponent();
        TracksList.ItemsSource = _tracks;
        RefreshMicrophones();
    }

    private void RefreshMicrophones()
    {
        var devices = Enumerable.Range(0, WaveInEvent.DeviceCount)
            .Select(i => new MicChoice(i, WaveInEvent.GetCapabilities(i).ProductName))
            .ToList();
        MicrophoneBox.ItemsSource = devices;
        if (devices.Count > 0) MicrophoneBox.SelectedIndex = 0;
        StatusText.Text = devices.Count == 0 ? "Микрофоны не найдены." : $"Найдено микрофонов: {devices.Count}.";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshMicrophones();

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        if (MicrophoneBox.SelectedItem is not MicChoice mic)
        {
            MessageBox.Show("Выберите микрофон.", "VoiceRocorder");
            return;
        }
        try
        {
            StopPlayback();
            _recordingPath = Path.Combine(Path.GetTempPath(), $"VoiceRocorder_{Guid.NewGuid():N}.wav");
            _waveIn = new WaveInEvent
            {
                DeviceNumber = mic.Index,
                WaveFormat = new WaveFormat(SampleRate, 16, 1),
                BufferMilliseconds = 100
            };
            _writer = new WaveFileWriter(_recordingPath, _waveIn.WaveFormat);
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
            _waveIn.StartRecording();
            RecordButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            StatusText.Text = "Идёт запись...";
        }
        catch (Exception ex)
        {
            CleanupRecorder();
            MessageBox.Show($"Не удалось начать запись:\n{ex.Message}", "Ошибка");
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try { _writer?.Write(e.Buffer, 0, e.BytesRecorded); _writer?.Flush(); }
        catch { }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_waveIn is null) return;
        StopButton.IsEnabled = false;
        _waveIn.StopRecording();
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var path = _recordingPath;
            CleanupRecorder();
            RecordButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length > 44)
            {
                var track = new TrackItem { Name = $"Дорожка {_trackNumber++}", OriginalPath = path };
                _tracks.Add(track);
                TracksList.SelectedItem = track;
                StatusText.Text = "Запись добавлена в проект.";
            }
            else StatusText.Text = "Запись не содержит аудио.";
            if (e.Exception is not null) MessageBox.Show(e.Exception.Message, "Ошибка записи");
        });
    }

    private void CleanupRecorder()
    {
        try { if (_waveIn is not null) { _waveIn.DataAvailable -= OnDataAvailable; _waveIn.RecordingStopped -= OnRecordingStopped; _waveIn.Dispose(); } } catch { }
        try { _writer?.Dispose(); } catch { }
        _waveIn = null; _writer = null;
    }

    private TrackItem? Selected => TracksList.SelectedItem as TrackItem;

    private void PlayOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } t) PlayFile(t.OriginalPath);
    }
    private void PlayReverse_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } t) return;
        if (string.IsNullOrEmpty(t.ReversePath) || !File.Exists(t.ReversePath))
        {
            try { t.ReversePath = CreateReverse(t.OriginalPath); }
            catch (Exception ex) { MessageBox.Show($"Не удалось создать реверс:\n{ex.Message}"); return; }
        }
        PlayFile(t.ReversePath);
    }
    private void Reverse_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } t) return;
        try
        {
            t.ReversePath ??= CreateReverse(t.OriginalPath);
            t.IsReversed = true;
            TracksList.Items.Refresh();
            StatusText.Text = "Для дорожки выбран реверс. Оригинал сохранён.";
        }
        catch (Exception ex) { MessageBox.Show($"Ошибка реверса:\n{ex.Message}"); }
    }
    private void Original_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } t) { t.IsReversed = false; TracksList.Items.Refresh(); StatusText.Text = "Выбран оригинал."; }
    }

    private static string CreateReverse(string input)
    {
        using var reader = new WaveFileReader(input);
        if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm || reader.WaveFormat.BitsPerSample != 16)
            throw new InvalidDataException("Поддерживается только 16-битный PCM WAV.");
        int align = reader.WaveFormat.BlockAlign;
        if (align <= 0) throw new InvalidDataException("Некорректный формат WAV.");
        var bytes = new byte[reader.Length];
        int total = 0, read;
        while ((read = reader.Read(bytes, total, bytes.Length - total)) > 0) total += read;
        total -= total % align;
        for (int left = 0, right = total - align; left < right; left += align, right -= align)
        {
            for (int b = 0; b < align; b++)
                (bytes[left + b], bytes[right + b]) = (bytes[right + b], bytes[left + b]);
        }
        string output = Path.Combine(Path.GetTempPath(), $"VoiceRocorder_reverse_{Guid.NewGuid():N}.wav");
        using var writer = new WaveFileWriter(output, reader.WaveFormat);
        writer.Write(bytes, 0, total);
        return output;
    }

    private void PlayFile(string path)
    {
        if (!File.Exists(path)) { MessageBox.Show("Файл дорожки не найден."); return; }
        StopPlayback();
        try
        {
            _player = new WaveOutEvent();
            _player.Init(new AudioFileReader(path));
            _player.Play();
            StatusText.Text = $"Воспроизведение: {Path.GetFileName(path)}";
        }
        catch (Exception ex) { StopPlayback(); MessageBox.Show(ex.Message, "Ошибка воспроизведения"); }
    }
    private void StopPlayback_Click(object sender, RoutedEventArgs e) => StopPlayback();
    private void StopPlayback()
    {
        try { _player?.Stop(); _player?.Dispose(); } catch { }
        _player = null;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } t) return;
        string source = t.IsReversed && !string.IsNullOrEmpty(t.ReversePath) ? t.ReversePath : t.OriginalPath;
        var dialog = new SaveFileDialog { Filter = "WAV audio (*.wav)|*.wav", FileName = $"{t.Name}.wav" };
        if (dialog.ShowDialog() != true) return;
        try { File.Copy(source, dialog.FileName, true); StatusText.Text = "WAV экспортирован."; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Ошибка экспорта"); }
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "VoiceRocorder project (*.vrcproj)|*.vrcproj", FileName = "MyProject.vrcproj" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            string folder = Path.Combine(Path.GetDirectoryName(dialog.FileName)!, Path.GetFileNameWithoutExtension(dialog.FileName) + "_Audio");
            Directory.CreateDirectory(folder);
            var data = new ProjectData();
            for (int i = 0; i < _tracks.Count; i++)
            {
                var t = _tracks[i];
                string originalName = $"track_{i + 1}_original.wav";
                File.Copy(t.OriginalPath, Path.Combine(folder, originalName), true);
                string? reverseName = null;
                if (!string.IsNullOrEmpty(t.ReversePath) && File.Exists(t.ReversePath))
                {
                    reverseName = $"track_{i + 1}_reverse.wav";
                    File.Copy(t.ReversePath, Path.Combine(folder, reverseName), true);
                }
                data.Tracks.Add(new TrackData { Name = t.Name, OriginalFile = originalName, ReverseFile = reverseName, IsReversed = t.IsReversed });
            }
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            StatusText.Text = "Проект сохранён вместе с папкой аудио.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Ошибка сохранения"); }
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "VoiceRocorder project (*.vrcproj)|*.vrcproj" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var data = JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(dialog.FileName)) ?? new ProjectData();
            string folder = Path.Combine(Path.GetDirectoryName(dialog.FileName)!, Path.GetFileNameWithoutExtension(dialog.FileName) + "_Audio");
            var loaded = new List<TrackItem>();
            foreach (var item in data.Tracks)
            {
                string original = Path.Combine(folder, item.OriginalFile);
                if (!File.Exists(original)) continue;
                string? reverse = item.ReverseFile is null ? null : Path.Combine(folder, item.ReverseFile);
                loaded.Add(new TrackItem { Name = item.Name, OriginalPath = original, ReversePath = reverse, IsReversed = item.IsReversed });
            }
            _tracks.Clear();
            foreach (var t in loaded) _tracks.Add(t);
            _trackNumber = _tracks.Count + 1;
            if (_tracks.Count > 0) TracksList.SelectedIndex = 0;
            StatusText.Text = $"Загружено дорожек: {_tracks.Count}.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Ошибка открытия проекта"); }
    }

    private void TracksList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    protected override void OnClosed(EventArgs e)
    {
        try { _waveIn?.StopRecording(); } catch { }
        CleanupRecorder();
        StopPlayback();
        base.OnClosed(e);
    }

    private sealed record MicChoice(int Index, string Name);
}
