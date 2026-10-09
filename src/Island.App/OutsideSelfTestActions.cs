using Island.Core;

namespace Island.App;

/// <summary>
/// The outside door as the self-test uses it: every request asks <see cref="OutsideGate"/> first. Under the
/// self-test the gate lets only the self-test's own windows be brought forward, so starting a program, opening a
/// folder or a site is refused and counted, and nothing of Dan's is ever touched.
/// </summary>
internal sealed class OutsideSelfTestActions : IOutsideActions
{
    public bool BringForward(long windowHandle) => OutsideForeground.BringForward(new IntPtr(windowHandle));

    public bool StartProgram(Pick pick) => OutsideGate.Current.Allow(OutsideKind.StartProgram) && false; // never started: the gate refuses it under the self-test

    public bool OpenFolder(string knownFolder) => OutsideGate.Current.Allow(OutsideKind.OpenFolder) && false;

    public bool OpenFolderAt(string realPath) => OutsideGate.Current.Allow(OutsideKind.OpenFolder) && false;

    public bool OpenFile(string realPath) => OutsideGate.Current.Allow(OutsideKind.OpenFile) && false;

    public bool OpenSite(string host) => OutsideGate.Current.Allow(OutsideKind.OpenAddress) && false;

    public bool OpenSearch(SearchService service, string text) => OutsideGate.Current.Allow(OutsideKind.OpenAddress) && false;
}
