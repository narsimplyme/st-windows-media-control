using NAudio.CoreAudioApi;
using System.Globalization;
using System.Text.Json;

using var enumerator = new MMDeviceEnumerator();
using var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
var audio = endpoint.AudioEndpointVolume;
if (args.Length == 2 && args[0] == "volume") audio.MasterVolumeLevelScalar = float.Parse(args[1], CultureInfo.InvariantCulture);
if (args.Length == 2 && args[0] == "mute") audio.Mute = bool.Parse(args[1]);
Console.WriteLine(JsonSerializer.Serialize(new { volume = audio.MasterVolumeLevelScalar, muted = audio.Mute }));
