using Island.Core;
using Island.Sources.Media;

// Reads the machine's media sessions once and prints only numbers and yes/no. Under the self-test gate, so the
// one send attempt below (to a made-up id) must be refused. It never sends to a real session and never prints a
// title, an artist or an app id.
OutsideGate.Current = new OutsideGate(selfTest: true);

using var reader = new MediaSessionReader();
reader.Start();
var finished = reader.WaitForFirstRead(TimeSpan.FromSeconds(15));

var control = new OutsideMedia(reader);
var sent = control.Send("made-up-session-id", MediaCommand.PlayPause);

Console.WriteLine($"readFinished={finished}");
Console.WriteLine($"managerCreated={reader.ManagerAvailable}");
Console.WriteLine($"sessions={reader.Sessions.Count}");
Console.WriteLine($"browserSessions={reader.Sessions.Count(s => s.IsBrowser)}");
Console.WriteLine($"sendReturned={sent}");
Console.WriteLine($"gateRefusals={OutsideGate.Current.Refused(OutsideKind.MediaCommand)}");
Console.WriteLine($"gateAllowed={OutsideGate.Current.AllowedCount}");
return sent || OutsideGate.Current.Refused(OutsideKind.MediaCommand) != 1 ? 1 : 0;
