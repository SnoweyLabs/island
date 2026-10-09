using Island.Core;
using Island.Sources.Screens;

// Reads this machine's screens and pointer once and prints only counts and yes/no. No window, no names, no
// coordinates, no sizes. It moves nothing.
const double WindowWidthDip = 600;
const double WindowHeightDip = 120;

var changedEvents = 0;
using var reader = new ScreenReader();
reader.Changed += () => Interlocked.Increment(ref changedEvents);
reader.Start();
var finished = reader.WaitForFirstRead(TimeSpan.FromSeconds(10));

var eventsAfterFirstRead = Volatile.Read(ref changedEvents);

// Five wake-ups in a row must be debounced into one re-read, and that re-read finds nothing new: no event.
for (var i = 0; i < 5; i++) reader.NotifyPossibleChange();
Thread.Sleep(900);

var screens = reader.Screens;
var pointer = reader.Pointer;
var onScreen = pointer is { } p && screens.Any(s => s.Full.Contains(p));
var workSmaller = screens.Any(s => s.Work.Width < s.Full.Width || s.Work.Height < s.Full.Height);
var placement = ScreenChooser.Choose(pointer, screens, WindowWidthDip, WindowHeightDip);
var insideWork = !placement.IsFallback && placement.Window.Top == placement.Screen.SafeWork.Top;

Console.WriteLine($"readFinished={finished}");
Console.WriteLine($"changedEventsFirstRead={eventsAfterFirstRead}");
Console.WriteLine($"changedEventsAfterIdenticalReread={Volatile.Read(ref changedEvents) - eventsAfterFirstRead}");
Console.WriteLine($"screens={screens.Count}");
Console.WriteLine($"primaryScreens={screens.Count(s => s.IsPrimary)}");
Console.WriteLine($"pointerRead={pointer is not null}");
Console.WriteLine($"pointerOnAScreen={onScreen}");
Console.WriteLine($"workAreaSmallerThanScreen={workSmaller}");
Console.WriteLine($"scalingAboveOneHundred={screens.Any(s => s.Scale > 1.0)}");
Console.WriteLine($"mixedScaling={screens.Select(s => s.Scale).Distinct().Count() > 1}");
Console.WriteLine($"placementIsFallback={placement.IsFallback}");
Console.WriteLine($"placementAtTopOfWorkArea={insideWork}");
Console.WriteLine($"tilesOnChosenScreen={ScreenFit.Tiles(placement.Screen, 8)}");
return finished && screens.Count > 0 ? 0 : 1;
