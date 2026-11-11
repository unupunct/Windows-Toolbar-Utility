using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace WinTab.Core.Audio;

/// <summary>One app that has an audio session on the default output device.</summary>
public sealed record AppAudioSession(int ProcessId, string Name, float Volume, bool IsActive, bool IsMuted);

/// <summary>
/// Output volume, microphone volume and a volume slider per app, through the
/// Windows Core Audio API. Windows (unlike macOS 12) lets each app's volume
/// be changed individually.
/// </summary>
public sealed class AudioService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();

    private MMDevice? Default(DataFlow flow, Role role = Role.Multimedia)
    {
        try
        {
            return _enumerator.HasDefaultAudioEndpoint(flow, role)
                ? _enumerator.GetDefaultAudioEndpoint(flow, role)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public string? OutputDeviceName
    {
        get
        {
            using var device = Default(DataFlow.Render);
            return device?.FriendlyName;
        }
    }

    public string? InputDeviceName
    {
        get
        {
            using var device = Default(DataFlow.Capture, Role.Communications);
            return device?.FriendlyName;
        }
    }

    public float? GetOutputVolume()
    {
        using var device = Default(DataFlow.Render);
        return device?.AudioEndpointVolume.MasterVolumeLevelScalar;
    }

    public void SetOutputVolume(float volume)
    {
        using var device = Default(DataFlow.Render);
        if (device != null) device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0f, 1f);
    }

    public bool? GetOutputMuted()
    {
        using var device = Default(DataFlow.Render);
        return device?.AudioEndpointVolume.Mute;
    }

    public void SetOutputMuted(bool muted)
    {
        using var device = Default(DataFlow.Render);
        if (device != null) device.AudioEndpointVolume.Mute = muted;
    }

    public float? GetInputVolume()
    {
        using var device = Default(DataFlow.Capture, Role.Communications);
        return device?.AudioEndpointVolume.MasterVolumeLevelScalar;
    }

    public void SetInputVolume(float volume)
    {
        using var device = Default(DataFlow.Capture, Role.Communications);
        if (device != null) device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0f, 1f);
    }

    public bool? GetInputMuted()
    {
        using var device = Default(DataFlow.Capture, Role.Communications);
        return device?.AudioEndpointVolume.Mute;
    }

    public void SetInputMuted(bool muted)
    {
        using var device = Default(DataFlow.Capture, Role.Communications);
        if (device != null) device.AudioEndpointVolume.Mute = muted;
    }

    /// <summary>Apps with an audio session right now, one row per process, loudest-playing first.</summary>
    public IReadOnlyList<AppAudioSession> GetAppSessions()
    {
        var result = new Dictionary<int, AppAudioSession>();
        try
        {
            using var device = Default(DataFlow.Render);
            if (device == null) return Array.Empty<AppAudioSession>();
            var sessions = device.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.IsSystemSoundsSession) continue;
                if (session.State == AudioSessionState.AudioSessionStateExpired) continue;
                int pid = (int)session.GetProcessID;
                if (pid == 0) continue;
                var volume = session.SimpleAudioVolume;
                bool active = session.State == AudioSessionState.AudioSessionStateActive;
                if (result.TryGetValue(pid, out var existing))
                {
                    if (active && !existing.IsActive)
                        result[pid] = existing with { IsActive = true };
                    continue;
                }
                result[pid] = new AppAudioSession(pid, DescribeProcess(pid, session.DisplayName),
                    volume.Volume, active, volume.Mute);
            }
        }
        catch
        {
            // A device that disappears mid-read just gives an empty list this time.
        }
        return result.Values
            .OrderByDescending(s => s.IsActive)
            .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public void SetAppVolume(int processId, float volume)
    {
        ForEachSession(processId, v => v.Volume = Math.Clamp(volume, 0f, 1f));
    }

    public void SetAppMuted(int processId, bool muted)
    {
        ForEachSession(processId, v => v.Mute = muted);
    }

    private void ForEachSession(int processId, Action<SimpleAudioVolume> action)
    {
        try
        {
            using var device = Default(DataFlow.Render);
            if (device == null) return;
            var sessions = device.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.GetProcessID == (uint)processId) action(session.SimpleAudioVolume);
            }
        }
        catch
        {
        }
    }

    /// <summary>A readable name for a process: its file description, else its process name.</summary>
    public static string DescribeProcess(int pid, string? sessionName = null)
    {
        if (!string.IsNullOrWhiteSpace(sessionName) && !sessionName.StartsWith('@')) return sessionName;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            try
            {
                var description = process.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) return description;
            }
            catch
            {
                // Protected processes refuse module access; fall back to the name.
            }
            return process.ProcessName;
        }
        catch
        {
            return $"Process {pid}";
        }
    }

    public void Dispose() => _enumerator.Dispose();
}
