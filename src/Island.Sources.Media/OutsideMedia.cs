using Island.Core;
using Windows.Media.Control;

namespace Island.Sources.Media;

/// <summary>
/// The one door through which a play/pause, next or previous reaches a real media session. It asks
/// <see cref="OutsideGate"/> first; when the gate refuses (the self-test), nothing is sent and the answer is false.
/// The command is handed to the session on a worker thread so the caller never waits for a player; true means
/// "the session was found and the command was handed over", not that the player obeyed.
/// </summary>
public sealed class OutsideMedia(MediaSessionReader reader) : IMediaControl
{
    public bool Send(string sessionId, MediaCommand command)
    {
        if (!OutsideGate.Current.Allow(OutsideKind.MediaCommand)) return false;

        var session = reader.Find(sessionId);
        if (session is null) return false;

        _ = Task.Run(() => Dispatch(session, command));
        return true;
    }

    private static async Task Dispatch(GlobalSystemMediaTransportControlsSession session, MediaCommand command)
    {
        try
        {
            _ = command switch
            {
                MediaCommand.PlayPause => await session.TryTogglePlayPauseAsync(),
                MediaCommand.Next => await session.TrySkipNextAsync(),
                MediaCommand.Previous => await session.TrySkipPreviousAsync(),
                _ => false,
            };
        }
        catch
        {
            // A player that has just closed: nothing to do, and nothing to tell the caller.
        }
    }
}
