using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NcmNative;

/// <summary>
/// 播放核心。解码走 Windows 媒体基础，输出优先 WASAPI。
///
/// 音量在采样数据层做（VolumeSampleProvider），不去动设备/会话音量。
/// 之前用 WaveOut.Volume 会改系统会话音量，切歌时新建输出对象它先回到 100%，
/// 造成瞬间爆音，这个坑在这一版彻底避开。
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    private IWavePlayer _out;
    private MediaFoundationReader _reader;
    private VolumeSampleProvider _volumeProvider;
    private float _volume = 0.4f;
    private int _generation;

    public bool UseExclusive { get; set; }
    public string LastOutputMode { get; private set; } = "-";

    public event Action Finished;

    public bool HasAudio => _reader != null;
    public bool IsPlaying => _out != null && _out.PlaybackState == PlaybackState.Playing;

    public TimeSpan Position
    {
        get { try { return _reader?.CurrentTime ?? TimeSpan.Zero; } catch { return TimeSpan.Zero; } }
    }

    public TimeSpan Duration
    {
        get { try { return _reader?.TotalTime ?? TimeSpan.Zero; } catch { return TimeSpan.Zero; } }
    }

    public string FormatInfo
    {
        get
        {
            if (_reader == null) return "-";
            var f = _reader.WaveFormat;
            return $"{f.SampleRate}Hz/{f.BitsPerSample}bit/{f.Channels}ch";
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_volumeProvider != null)
                _volumeProvider.Volume = _volume;   // 数据层增益，切歌不会跳
            SaveVolume();
        }
    }

    private static readonly string VolumeFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NcmLite", "volume.txt");

    public static float LoadVolume(float fallback = 0.4f)
    {
        try
        {
            if (File.Exists(VolumeFile) &&
                float.TryParse(File.ReadAllText(VolumeFile).Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                return Math.Clamp(v, 0f, 1f);
        }
        catch { }
        return fallback;
    }

    private void SaveVolume()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(VolumeFile));
            File.WriteAllText(VolumeFile,
                _volume.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch { }
    }

    public async Task<bool> LoadAsync(string url)
    {
        var gen = ++_generation;
        Teardown();

        MediaFoundationReader reader = null;
        try
        {
            reader = await Task.Run(() =>
            {
                var r = new MediaFoundationReader(url);
                var _ = r.TotalTime;
                return r;
            });
        }
        catch (Exception ex)
        {
            Log.W("[audio] load failed: " + ex.Message);
            return false;
        }

        if (gen != _generation) { reader.Dispose(); return false; }

        _reader = reader;
        _volumeProvider = new VolumeSampleProvider(_reader.ToSampleProvider()) { Volume = _volume };
        LastOutputMode = "none";

        if (!TryInitOutput(gen))
        {
            Teardown();
            return false;
        }
        Log.W($"[audio] format={FormatInfo} out={LastOutputMode} vol={_volume:0.00}");
        return true;
    }

    private bool TryInitOutput(int gen)
    {
        var attempts = UseExclusive
            ? new[]
            {
                (mode: AudioClientShareMode.Exclusive, label: "WASAPI独占"),
                (mode: AudioClientShareMode.Shared, label: "WASAPI共享"),
            }
            : new[] { (mode: AudioClientShareMode.Shared, label: "WASAPI共享") };

        foreach (var (mode, label) in attempts)
        {
            try
            {
                var w = new WasapiOut(mode, 100);
                w.Init(_volumeProvider.ToWaveProvider());
                // 绝不设置 w.Volume：那是音频会话音量（WaveOut 更是直接改设备波形音量），
                // 每次切歌新建输出对象都会把它重置，造成音量大跳。衰减只放在数据层。
                w.PlaybackStopped += (_, _) => { if (gen == _generation) Finished?.Invoke(); };
                _out = w;
                LastOutputMode = label;
                return true;
            }
            catch (Exception ex)
            {
                Log.W($"[audio] {label} 不可用: {ex.Message}");
            }
        }

        try
        {
            var w = new WaveOut();
            w.Init(_volumeProvider.ToWaveProvider());
            // 同上：不碰 w.Volume
            w.PlaybackStopped += (_, _) => { if (gen == _generation) Finished?.Invoke(); };
            _out = w;
            LastOutputMode = "WinMM";
            return true;
        }
        catch (Exception ex)
        {
            Log.W("[audio] WinMM 也不可用: " + ex.Message);
            return false;
        }
    }

    public void Play()
    {
        try { _out?.Play(); } catch (Exception ex) { Log.W("[audio] play 失败: " + ex.Message); }
    }

    public void Pause()
    {
        try { _out?.Pause(); } catch { }
    }

    public void Seek(TimeSpan to)
    {
        if (_reader == null) return;
        var d = Duration;
        if (d <= TimeSpan.Zero) return;
        if (to < TimeSpan.Zero) to = TimeSpan.Zero;
        if (to > d) to = d;
        try { _reader.CurrentTime = to; } catch { }
    }

    public void Stop()
    {
        _generation++;
        Teardown();
    }

    private void Teardown()
    {
        try { _out?.Stop(); } catch { }
        try { _out?.Dispose(); } catch { }
        _out = null;
        _volumeProvider = null;
        try { _reader?.Dispose(); } catch { }
        _reader = null;
    }

    public void Dispose() => Stop();
}
