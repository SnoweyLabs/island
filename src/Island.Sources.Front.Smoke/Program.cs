using Island.Core;
using Island.Sources.Front;

// Reads what is in front of this laptop once and prints only the kind of state, yes/no answers and counts. It shows
// no window, asks for nothing, and prints no program name, no class name and no rectangle.
var reading = FrontReader.ReadForeground();
var notification = FrontReader.ReadNotificationState();
var window = FrontReader.ForegroundWindow();
var screen = FrontReader.ScreenOf(window);

// The rectangle test alone, with the foreground window counted as the island's own: it must answer Clear whatever is running.
var ownIsClear = FrontReader.ClassifyWindow(window, screen, _ => true) == FrontState.Clear;
// A window handed a screen of a different size is never fullscreen.
var shifted = screen is { } s ? new FrontRect(s.Left + 1, s.Top, s.Right + 1, s.Bottom) : (FrontRect?)null;
var mismatchIsClear = FrontReader.ClassifyWindow(window, shifted) == FrontState.Clear;

// Handles that are not windows, and a missing screen, must give Clear and never throw.
var badHandleIsClear = FrontReader.Read(0x7FFF_FFF0, new FrontRect(0, 0, 10, 10), null).State == FrontState.Clear
                       && FrontReader.Read(0, null, null).State == FrontState.Clear
                       && FrontReader.Read(window, null, null).State == FrontState.Clear;

Console.WriteLine($"frontState={reading.State}");
Console.WriteLine($"badInputAnswersClear={badHandleIsClear}");
Console.WriteLine($"foregroundWindowFound={window != 0}");
Console.WriteLine($"screenRead={screen is not null}");
Console.WriteLine($"exeNameRead={reading.ExeFileName is not null}");
Console.WriteLine($"notificationStateRead={notification is not null}");
Console.WriteLine($"notificationStateIsHard={notification is { } n && FrontClassifier.FromNotificationState(n) is not null}");
Console.WriteLine($"ownWindowAnswersClear={ownIsClear}");
Console.WriteLine($"mismatchedScreenAnswersClear={mismatchIsClear}");
Console.WriteLine($"stateIsDefined={Enum.IsDefined(reading.State)}");
return ownIsClear && mismatchIsClear && badHandleIsClear && Enum.IsDefined(reading.State) ? 0 : 1;
