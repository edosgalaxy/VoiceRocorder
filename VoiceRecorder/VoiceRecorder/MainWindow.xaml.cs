using Microsoft.Win32;
using NAudio.Wave;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace VoiceRecorder;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<TrackItem> _tracks = new();
    private readonly string _folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VoiceRecorder");
    private readonly string _manifestPath;
    private WaveInEvent? _recorder;
    private WaveFileWriter? _writer;
    private WaveOutEvent? _player;
    private AudioFileReader? _audioReader;
    private string? _currentRecordingPath;
    private int _selectedDevice;
    private bool _isRecording;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _recordingStarted;

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(_folder);
        _manifestPath = Path.Combine(_folder, "project.json");
        TracksList.ItemsSource = _tracks;
        FolderText.Text = $"Файлы хранятся здесь: {_folder}";
        LoadTracks();
        LoadInputDevices();
        _timer.Tick += (_, _) => StatusText.Text = $"Идёт запись · {DateTime.Now - _recordingStarted:mm\\:ss}";
        UpdateStatus();
    }

    private void LoadInputDevices()
    {
        InputDeviceBox.Items.Clear();
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            InputDeviceBox.Items.Add($"{i}: {caps.ProductName}");
        }
        if (InputDeviceBox.Items.Count > 0)
            InputDeviceBox.SelectedIndex = 0;
        else
            StatusText.Text = "Микрофон не найден";
    }

    private void InputDeviceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InputDeviceBox.SelectedIndex >= 0)
            _selectedDevice = InputDeviceBox.SelectedIndex;
    }

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRecording) return;
        StopPlayback();
        var fileName = $"Voice_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
        _currentRecordingPath = Path.Combine(_folder, fileName);
        try
        {
            _recorder = new WaveInEvent
            {
                DeviceNumber = _selectedDevice,
                WaveFormat = new WaveFormat(44100, 16, 1),
                BufferMilliseconds = 100
            };
            _writer = new WaveFileWriter(_currentRecordingPath, _recorder.WaveFormat);
            _recorder.DataAvailable += (_, args) =>
            {
                _writer?.Write(args.Buffer, 0, args.BytesRecorded);
                _writer?.Flush();
            };
            _recorder.RecordingStopped += (_, args) =>
            {
                _writer?.Dispose();
                _writer = null;
                _recorder?.Dispose();
                _recorder = null;
                Dispatcher.Invoke(() =>
                {
                    if (args.Exception != null)
                        MessageBox.Show(this, args.Exception.Message, "Ошибка записи",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    AddFinishedRecording();
                });
            };
            _recordingStarted = DateTime.Now;
            _isRecording = true;
            _recorder.StartRecording();
            _timer.Start();
            RecordButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            StatusText.Text = "Идёт запись · 00:00";
        }
        catch (Exception ex)
        {
            _isRecording = false;
            _writer?.Dispose();
            _writer = null;
            _recorder?.Dispose();
            _recorder = null;
            MessageBox.Show(this, ex.Message, "Не удалось начать запись",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) => StopRecording();

    private void StopRecording()
    {
        if (!_isRecording) return;
        _isRecording = false;
        _timer.Stop();
        RecordButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        StatusText.Text = "Сохраняю запись…";
        _recorder?.StopRecording();
    }

    private void AddFinishedRecording()
    {
        if (!string.IsNullOrWhiteSpace(_currentRecordingPath) &&
            File.Exists(_currentRecordingPath) && new FileInfo(_currentRecordingPath).Length > 44)
        {
            var index = _tracks.Count + 1;
            var info = new FileInfo(_currentRecordingPath);
            var duration = GetDuration(_currentRecordingPath);
            _tracks.Add(new TrackItem
            {
                Name = $"Голос {index:00}",
                OriginalPath = _currentRecordingPath,
                Details = $"{duration:mm\\:ss} · {info.Length / 1024.0:0} КБ"
            });
            SaveTracks();
            TracksList.SelectedIndex = _tracks.Count - 1;
        }
        _currentRecordingPath = null;
        UpdateStatus();
    }

    private void NewRecording_Click(object sender, RoutedEventArgs e)
    {
        if (_isRecording)
        {
            MessageBox.Show(this, "Сначала останови текущую запись.", "Запись активна",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        RecordButton_Click(sender, e);
    }

    private void Reverse_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TrackItem track }) return;
        try
        {
            StopPlayback();
            string reversePath = Path.Combine(_folder,
                Path.GetFileNameWithoutExtension(track.OriginalPath) + "_REVERSED.wav");
            CreateReversedWave(track.OriginalPath, reversePath);
            track.ReversePath = reversePath;
            TracksList.Items.Refresh();
            SaveTracks();
            StatusText.Text = $"{track.Name}: реверс готов";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка обработки аудио",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Reverse complete sample frames, not individual bytes, so stereo/channel data stays aligned.
    private static void CreateReversedWave(string source, string destination)
    {
        using var reader = new WaveFileReader(source);
        var format = reader.WaveFormat;
        int frameSize = format.BlockAlign;
        if (frameSize <= 0) throw new InvalidDataException("Неизвестный формат аудио.");
        using var input = new MemoryStream();
        reader.CopyTo(input);
        byte[] data = input.ToArray();
        int usableLength = data.Length - data.Length % frameSize;
        byte[] reversed = new byte[usableLength];
        for (int src = 0, dst = usableLength - frameSize; src < usableLength; src += frameSize, dst -= frameSize)
            Buffer.BlockCopy(data, src, reversed, dst, frameSize);
        using var writer = new WaveFileWriter(destination, format);
        writer.Write(reversed, 0, reversed.Length);
    }

    private void PlayOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TrackItem track }) PlayFile(track.OriginalPath);
    }

    private void PlayReverse_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TrackItem track } && track.HasReverse) PlayFile(track.ReversePath!);
    }

    private void PlayFile(string path)
    {
        try
        {
            StopPlayback();
            _audioReader = new AudioFileReader(path);
            _player = new WaveOutEvent();
            _player.Init(_audioReader);
            _player.Play();
            StatusText.Text = $"Воспроизведение: {Path.GetFileName(path)}";
            _player.PlaybackStopped += (_, _) => Dispatcher.Invoke(() =>
            {
                StopPlayback();
                UpdateStatus();
            });
        }
        catch (Exception ex)
        {
            StopPlayback();
            MessageBox.Show(this, ex.Message, "Ошибка воспроизведения",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopPlayback()
    {
        try { _player?.Stop(); } catch { }
        _player?.Dispose();
        _player = null;
        _audioReader?.Dispose();
        _audioReader = null;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TrackItem track }) return;
        var dialog = new SaveFileDialog
        {
            Filter = "WAV audio (*.wav)|*.wav",
            FileName = Path.GetFileName(track.OriginalPath),
            DefaultExt = ".wav"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            string source = track.HasReverse ? track.ReversePath! : track.OriginalPath;
            File.Copy(source, dialog.FileName, true);
            StatusText.Text = "Файл экспортирован";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка экспорта",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TrackItem track }) return;
        var result = MessageBox.Show(this, $"Удалить «{track.Name}» и её файлы?",
            "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        try
        {
            if (File.Exists(track.OriginalPath)) File.Delete(track.OriginalPath);
            if (track.HasReverse && File.Exists(track.ReversePath)) File.Delete(track.ReversePath);
            _tracks.Remove(track);
            SaveTracks();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка удаления",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadTracks()
    {
        try
        {
            if (!File.Exists(_manifestPath)) return;
            var items = JsonSerializer.Deserialize<List<TrackItem>>(File.ReadAllText(_manifestPath)) ?? new();
            foreach (var item in items.Where(t => File.Exists(t.OriginalPath)))
                _tracks.Add(item);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Не удалось загрузить список проекта: {ex.Message}",
                "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveTracks()
    {
        File.WriteAllText(_manifestPath,
            JsonSerializer.Serialize(_tracks.ToList(), new JsonSerializerOptions { WriteIndented = true }));
    }

    private static TimeSpan GetDuration(string path)
    {
        using var reader = new WaveFileReader(path);
        return reader.TotalTime;
    }

    private void UpdateStatus()
    {
        if (!_isRecording)
            StatusText.Text = $"Готово · записей: {_tracks.Count}";
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_isRecording)
        {
            var answer = MessageBox.Show(this, "Остановить запись и закрыть программу?",
                "Запись активна", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) { e.Cancel = true; return; }
            _isRecording = false;
            _timer.Stop();
            _recorder?.StopRecording();
        }
        StopPlayback();
        base.OnClosing(e);
    }
}
