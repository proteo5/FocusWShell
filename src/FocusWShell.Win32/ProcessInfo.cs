using System.Runtime.InteropServices;
using FocusWShell.Core.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace FocusWShell.Win32;

/// <summary>Consulta datos de un proceso con los permisos mínimos.</summary>
internal static class ProcessInfo
{
    public static (string Path, IntegrityLevel Integrity) Query(uint processId)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process.IsInvalid)
        {
            return (string.Empty, IntegrityLevel.Unknown);
        }

        return (ImagePath(process), Integrity(process));
    }

    public static IntegrityLevel CurrentIntegrity()
    {
        using var process = PInvoke.GetCurrentProcess_SafeHandle();
        return Integrity(process);
    }

    private static string ImagePath(SafeHandle process)
    {
        Span<char> buffer = stackalloc char[1024];
        uint length = (uint)buffer.Length;
        return PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, ref length)
            ? new string(buffer[..(int)length])
            : string.Empty;
    }

    private static unsafe IntegrityLevel Integrity(SafeHandle process)
    {
        if (!PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token))
        {
            return IntegrityLevel.Unknown;
        }

        using (token)
        {
            PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenIntegrityLevel, null, 0, out uint size);
            if (size == 0)
            {
                return IntegrityLevel.Unknown;
            }

            var buffer = new byte[size];
            fixed (byte* p = buffer)
            {
                if (!PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenIntegrityLevel, p, size, out _))
                {
                    return IntegrityLevel.Unknown;
                }

                var label = (TOKEN_MANDATORY_LABEL*)p;
                var sid = label->Label.Sid;
                byte count = *PInvoke.GetSidSubAuthorityCount(sid);
                uint rid = *PInvoke.GetSidSubAuthority(sid, (uint)(count - 1));
                return FromRid(rid);
            }
        }
    }

    private static IntegrityLevel FromRid(uint rid) => rid switch
    {
        >= PInvoke.SECURITY_MANDATORY_SYSTEM_RID => IntegrityLevel.System,
        >= PInvoke.SECURITY_MANDATORY_HIGH_RID => IntegrityLevel.High,
        >= PInvoke.SECURITY_MANDATORY_MEDIUM_RID => IntegrityLevel.Medium,
        >= PInvoke.SECURITY_MANDATORY_LOW_RID => IntegrityLevel.Low,
        _ => IntegrityLevel.Untrusted,
    };
}
