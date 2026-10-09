using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Island.Agents;

/// <summary>
/// Who may open the pipe: the current Windows user (by SID) and nobody else, and no one over the network. Built with
/// the access-control types of System.IO.Pipes (PipeSecurity, PipeAccessRule; NamedPipeServerStreamAcl.Create takes it,
/// as on Microsoft Learn). Reading the current user's identity is not an outside action.
/// </summary>
public static class AgentPipeSecurity
{
    public static PipeSecurity ForCurrentUser()
    {
        var me = WindowsIdentity.GetCurrent().User
                 ?? throw new InvalidOperationException("The current user has no SID.");
        var security = new PipeSecurity();
        // A deny for network logons first: a remote session of the same user must not reach it either.
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(me, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }
}
