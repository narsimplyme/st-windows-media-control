using NAudio.CoreAudioApi;
using System.Globalization;
using System.Text.Json;

using var enumerator = new MMDeviceEnumerator();
if (args.Length >= 2 && args[0] == "app")
{
    var pid = uint.Parse(args[1], CultureInfo.InvariantCulture);
    var rows = new List<object>();
    foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
    using (device)
    {
        var sessions = device.AudioSessionManager.Sessions;
        for (var i = 0; i < sessions.Count; i++)
        using (var session = sessions[i])
        {
            if (session.GetProcessID != pid) continue;
            var channel = session.SimpleAudioVolume;
            if (args.Length == 4 && args[2] == "volume") channel.Volume = float.Parse(args[3], CultureInfo.InvariantCulture);
            if (args.Length == 4 && args[2] == "mute") channel.Mute = bool.Parse(args[3]);
            rows.Add(new { volume = channel.Volume, muted = channel.Mute });
        }
    }
    Console.WriteLine(JsonSerializer.Serialize(rows));
    return;
}
using var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
var audio = endpoint.AudioEndpointVolume;
if (args.Length == 2 && args[0] == "volume") audio.MasterVolumeLevelScalar = float.Parse(args[1], CultureInfo.InvariantCulture);
if (args.Length == 2 && args[0] == "mute") audio.Mute = bool.Parse(args[1]);
Console.WriteLine(JsonSerializer.Serialize(new { volume = audio.MasterVolumeLevelScalar, muted = audio.Mute }));
