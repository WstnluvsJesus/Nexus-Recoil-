using System.IO;
using NAudio.Wave;

namespace Nexus_Recoil.Core;

internal sealed class SoundManager : IDisposable
{
    private float   _volume;
    private bool    _enabled;
    private string? _customSoundPath;
    private readonly byte[] _defaultClickBuffer;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0f, 1f);
    }

    public string? CustomSoundPath
    {
        get => _customSoundPath;
        set => _customSoundPath = value;
    }

    public static bool HasAudioDevice => WaveOut.DeviceCount > 0;

    public SoundManager(bool enabled, float volume, string? customSoundPath)
    {
        _enabled          = enabled;
        _volume           = Math.Clamp(volume, 0f, 1f);
        _customSoundPath  = customSoundPath;
        _defaultClickBuffer = GenerateMechanicalClick();
    }

    public void PlayActivate()   => PlaySound();
    public void PlayDeactivate() => PlaySound();
    public void PlayPreview()    => PlayInternal();

    private void PlaySound()
    {
        if (!_enabled) return;
        PlayInternal();
    }

    private void PlayInternal()
    {
        Task.Run(() =>
        {
            try
            {
                if (_customSoundPath != null && File.Exists(_customSoundPath))
                    PlayFile(_customSoundPath);
                else
                    PlayBuffer(_defaultClickBuffer);
            }
            catch { /* never crash on audio failure */ }
        });
    }

    private void PlayFile(string path)
    {
        using var reader = new AudioFileReader(path);
        reader.Volume = _volume;
        using var output = new WaveOutEvent();
        output.Init(reader);
        output.Play();
        while (output.PlaybackState == PlaybackState.Playing)
            Thread.Sleep(10);
    }

    private void PlayBuffer(byte[] buffer)
    {
        var waveFormat = new WaveFormat(44100, 16, 1);
        using var ms     = new MemoryStream(buffer);
        using var raw    = new RawSourceWaveStream(ms, waveFormat);
        var       vol    = new VolumeWaveProvider16(raw) { Volume = _volume };
        using var output = new WaveOutEvent();
        output.Init(vol);
        output.Play();
        while (output.PlaybackState == PlaybackState.Playing)
            Thread.Sleep(10);
    }

    private static byte[] GenerateMechanicalClick()
    {
        int    sampleRate  = 44100;
        double durationMs  = 80;
        int    samples     = (int)(sampleRate * durationMs / 1000);
        byte[] buffer      = new byte[samples * 2]; // 16-bit mono
        for (int i = 0; i < samples; i++)
        {
            double t        = i / (double)sampleRate;
            double progress = i / (double)samples;
            double envelope = Math.Exp(-progress * 18);
            double noise    = new Random(i * 1337).NextDouble() * 2 - 1;
            double tone     = Math.Sin(2 * Math.PI * 800 * t);
            double sample   = (noise * 0.6 + tone * 0.4) * envelope;
            short  value    = (short)(sample * short.MaxValue * 0.6);
            buffer[i * 2]       = (byte)(value & 0xFF);
            buffer[i * 2 + 1]   = (byte)((value >> 8) & 0xFF);
        }
        return buffer;
    }

    public void Dispose() { }
}
