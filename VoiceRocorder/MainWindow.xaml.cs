using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace VoiceRocorder;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<TrackItem> _tracks = new();
    private readonly MMDeviceEnumerator _deviceEnumerator = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private MMDevice? _selectedDevice;
    private WasapiCapture? _capture;
    private WaveFileWriter? _writer;
    private string? _recordingPath;
    private WaveOutEvent? _player;
    private AudioFileReader? _playbackReader;
    private Stopwatch _recordingClock = new();
    private int _trackNumber = 1;
    private bool _finishingCapture;

    public MainWindow()
    {
        InitializeComponent();
        TracksList.ItemsSource = _tracks;
        _timer.Tick += (_, _) => TimerText.Text = _recordingClock.Elapsed.ToString(@"mm\:ss");
        RefreshMicrophones();
        UpdateTrackCount();
    }

    private void RefreshMicrophones()
    {
        if (_capture is not null)
        {
            FooterStatusText.Text = "Нельзя обновить список во время записи.";
            return;
        }

        try
        {
            var devices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            var choices = devices.Select(d => new MicChoice(d, d.FriendlyName)).ToList();
            MicrophoneBox.ItemsSource = choices;
            if (choices.Count > 0) MicrophoneBox.SelectedIndex = 0;
            FooterStatusText.Text = choices.Count == 0
                ? "Активные микрофоны не найдены. Проверь разрешения Windows."
                : $"Найдено устройств записи: {choices.Count}";
        }
        catch (Exception ex)
        {
            MicrophoneBox.ItemsSource = null;
            FooterStatusText.Text = "Ошибка обнаружения аудиоустройств.";
            MessageBox.Show($"Не удалось получить список устройств записи.\\n\\n{ex.GetType().Name}: {ex.Message}", "VoiceRocorder");
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshMicrophones();

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        if (MicrophoneBox.SelectedItem is not MicChoice mic)
        {
            MessageBox.Show("Выбери микрофон в списке.", "VoiceRocorder");
            return;
        }

        try
        {
            StopPlayback();
            _selectedDevice = mic.Device;
            _recordingPath = Path.Combine(Path.GetTempPath(), $"VoiceRocorder_{Guid.NewGuid():N}.wav");

            // Shared-mode WASAPI uses the endpoint mix format and avoids the legacy waveInOpen/MME capture path.
            _capture = new WasapiCapture(_selectedDevice);
            var format = _capture.WaveFormat;
            _writer = new WaveFileWriter(_recordingPath, format);
            _capture.DataAvailable += Capture_DataAvailable;
            _capture.RecordingStopped += Capture_RecordingStopped;
            _finishingCapture = false;
            _capture.StartRecording();

            _recordingClock.Restart();
            TimerText.Text = "00:00";
            _timer.Start();
            RecordButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            MicrophoneBox.IsEnabled = false;
            StatusText.Text = $"Идёт запись с устройства «{mic.Name}»";
            FooterStatusText.Text = $"Формат захвата: {format.SampleRate} Гц · {format.Channels} канал(а)";
        }
        catch (Exception ex)
        {
            CleanupCapture();
            ResetRecordingControls();
            FooterStatusText.Text = "Не удалось запустить аудиозахват.";
            MessageBox.Show($"Не удалось начать запись с устройства «{mic.Name}».\\n\\n{ex.GetType().Name}: {ex.Message}\\n\\nПроверь, что микрофон включён и разрешён в параметрах конфиденциальности Windows.", "Ошибка записи");
        }
    }

    private void Capture_DataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            if (e.BytesRecorded > 0)
                _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        }
        catch (Exception ex)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                FooterStatusText.Text = $"Ошибка записи файла: {ex.Message}";
                StopCapture();
            }));
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => StopCapture();

    private void StopCapture()
    {
        if (_capture is null || _finishingCapture) return;
        _finishingCapture = true;
        StopButton.IsEnabled = false;
        StatusText.Text = "Завершение записи…";
        try { _capture.StopRecording(); }
        catch (Exception ex)
        {
            CleanupCapture();
            ResetRecordingControls();
            MessageBox.Show($"Ошибка остановки записи:\\n{ex.Message}", "VoiceRocorder");
        }
    }

    private void Capture_RecordingStopped(object? sender, StoppedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var path = _recordingPath;
            CleanupCapture();
            _recordingClock.Stop();
            _timer.Stop();
            ResetRecordingControls();

            if (e.Exception is not null)
            {
                FooterStatusText.Text = "Запись завершилась с ошибкой.";
                MessageBox.Show($"Ошибка аудиозахвата:\\n{e.Exception.GetType().Name}: {e.Exception.Message}", "VoiceRocorder");
                return;
            }

            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length > 44)
            {
                var duration = GetDuration(path);
                var track = new TrackItem
                {
                    Name = $"Запись {_trackNumber++}",
                    OriginalPath = path,
                    DurationText = duration.ToString(@"mm\:ss")
                };
                _tracks.Add(track);
                TracksList.SelectedItem = track;
                StatusText.Text = "Запись сохранена в списке дорожек.";
                FooterStatusText.Text = $"Добавлена новая дорожка · {track.DurationText}";
                UpdateTrackCount();
            }
            else
            {
                StatusText.Text = "Пустая запись";
                FooterStatusText.Text = "Аудиоданные не поступили. Проверь выбранное устройство и уровень входа.";
            }
        }));
    }

    private static TimeSpan GetDuration(string path)
    {
        using var reader = new WaveFileReader(path);
        return reader.TotalTime;
    }

    private void CleanupCapture()
    {
        try
        {
            if (_capture is not null)
            {
                _capture.DataAvailable -= Capture_DataAvailable;
                _capture.RecordingStopped -= Capture_RecordingStopped;
                _capture.Dispose();
            }
        }
        catch { }
        try { _writer?.Dispose(); } catch { }
        _capture = null;
        _writer = null;
        _selectedDevice = null;
        _recordingPath = null;
        _finishingCapture = false;
    }

    private void ResetRecordingControls()
    {
        RecordButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        MicrophoneBox.IsEnabled = true;
        TimerText.Text = "00:00";
    }

    private TrackItem? Selected => TracksList.SelectedItem as TrackItem;

    private void PlayOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } track) PlayFile(track.OriginalPath);
        else FooterStatusText.Text = "Сначала выбери дорожку.";
    }

    private void PlayReverse_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } track) { FooterStatusText.Text = "Сначала выбери дорожку."; return; }
        try
        {
            if (string.IsNullOrWhiteSpace(track.ReversePath) || !File.Exists(track.ReversePath))
                track.ReversePath = CreateReverse(track.OriginalPath);
            PlayFile(track.ReversePath);
        }
        catch (Exception ex) { MessageBox.Show($"Не удалось создать или воспроизвести реверс:\\n{ex.Message}", "VoiceRocorder"); }
    }

    private void Reverse_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } track) return;
        try
        {
            if (string.IsNullOrWhiteSpace(track.ReversePath) || !File.Exists(track.ReversePath))
                track.ReversePath = CreateReverse(track.OriginalPath);
            track.IsReversed = true;
            FooterStatusText.Text = "Для выбранной дорожки установлен реверс. Оригинал не изменён.";
        }
        catch (Exception ex) { MessageBox.Show($"Ошибка создания реверса:\\n{ex.Message}", "VoiceRocorder"); }
    }

    private void Original_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } track)
        {
            track.IsReversed = false;
            FooterStatusText.Text = "Для выбранной дорожки выбран оригинал.";
        }
    }

    private static string CreateReverse(string input)
    {
        using var reader = new WaveFileReader(input);
        int align = reader.WaveFormat.BlockAlign;
        if (align <= 0) throw new InvalidDataException("Некорректный формат WAV.");

        if (reader.Length > int.MaxValue)
            throw new InvalidDataException("Файл слишком большой для обработки одним блоком.");

        var bytes = new byte[(int)reader.Length];
        int total = 0, read;
        while ((read = reader.Read(bytes, total, bytes.Length - total)) > 0) total += read;
        total -= total % align;

        // Reverse whole sample frames so stereo channels and sample bytes stay together.
        for (int left = 0, right = total - align; left < right; left += align, right -= align)
        {
            for (int b = 0; b < align; b++)
                (bytes[left + b], bytes[right + b]) = (bytes[right + b], bytes[left + b]);
        }

        string output = Path.Combine(Path.GetTempPath(), $"VoiceRocorder_reverse_{Guid.NewGuid():N}.wav");
        using (var writer = new WaveFileWriter(output, reader.WaveFormat))
            writer.Write(bytes, 0, total);
        return output;
    }

    private void PlayFile(string path)
    {
        if (!File.Exists(path)) { MessageBox.Show("Файл дорожки не найден.", "VoiceRocorder"); return; }
        StopPlayback();
        try
        {
            _playbackReader = new AudioFileReader(path);
            _player = new WaveOutEvent();
            _player.Init(_playbackReader);
            _player.Play();
            FooterStatusText.Text = $"Воспроизведение: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StopPlayback();
            MessageBox.Show($"Не удалось воспроизвести файл:\\n{ex.Message}", "VoiceRocorder");
        }
    }

    private void StopPlayback_Click(object sender, RoutedEventArgs e) => StopPlayback();

    private void StopPlayback()
    {
        try { _player?.Stop(); } catch { }
        try { _player?.Dispose(); } catch { }
        try { _playbackReader?.Dispose(); } catch { }
        _player = null;
        _playbackReader = null;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } track) return;
        string source = track.IsReversed && !string.IsNullOrWhiteSpace(track.ReversePath) && File.Exists(track.ReversePath)
            ? track.ReversePath : track.OriginalPath;
        var dialog = new SaveFileDialog { Filter = "WAV audio (*.wav)|*.wav", FileName = $"{track.Name}.wav" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            File.Copy(source, dialog.FileName, true);
            FooterStatusText.Text = "WAV-файл экспортирован.";
        }
        catch (Exception ex) { MessageBox.Show($"Ошибка экспорта:\\n{ex.Message}", "VoiceRocorder"); }
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
                var track = _tracks[i];
                string originalName = $"track_{i + 1}_original.wav";
                File.Copy(track.OriginalPath, Path.Combine(folder, originalName), true);
                string? reverseName = null;
                if (!string.IsNullOrWhiteSpace(track.ReversePath) && File.Exists(track.ReversePath))
                {
                    reverseName = $"track_{i + 1}_reverse.wav";
                    File.Copy(track.ReversePath, Path.Combine(folder, reverseName), true);
                }
                data.Tracks.Add(new TrackData { Name = track.Name, OriginalFile = originalName, ReverseFile = reverseName, IsReversed = track.IsReversed });
            }
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            FooterStatusText.Text = "Проект и аудиофайлы сохранены.";
        }
        catch (Exception ex) { MessageBox.Show($"Ошибка сохранения проекта:\\n{ex.Message}", "VoiceRocorder"); }
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
                if (reverse is not null && !File.Exists(reverse)) reverse = null;
                loaded.Add(new TrackItem
                {
                    Name = item.Name,
                    OriginalPath = original,
                    ReversePath = reverse,
                    IsReversed = item.IsReversed,
                    DurationText = GetDuration(original).ToString(@"mm\:ss")
                });
            }
            _tracks.Clear();
            foreach (var track in loaded) _tracks.Add(track);
            _trackNumber = _tracks.Count + 1;
            if (_tracks.Count > 0) TracksList.SelectedIndex = 0;
            UpdateTrackCount();
            FooterStatusText.Text = $"Открыт проект · дорожек: {_tracks.Count}";
        }
        catch (Exception ex) { MessageBox.Show($"Ошибка открытия проекта:\\n{ex.Message}", "VoiceRocorder"); }
    }

    private void TracksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selection is reflected by the standard WPF ListBox template.
    }

    private void UpdateTrackCount()
    {
        TrackCountText.Text = _tracks.Count switch
        {
            0 => "Пока нет дорожек",
            1 => "1 дорожка",
            _ => $"{_tracks.Count} дорожек"
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        try { _capture?.StopRecording(); } catch { }
        CleanupCapture();
        StopPlayback();
        foreach (var item in MicrophoneBox.Items)
            if (item is MicChoice choice) { try { choice.Device.Dispose(); } catch { } }
        _deviceEnumerator.Dispose();
        base.OnClosed(e);
    }

    private sealed record MicChoice(MMDevice Device, string Name);
}
