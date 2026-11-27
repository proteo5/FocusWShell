using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace FocusWShell.App.DeckWeb;

/// <summary>
/// Las direcciones del PC en las redes que Windows marca como **privadas o de dominio** (ADR 0021). Una red
/// pública (un café, un aeropuerto) no cuenta, aunque use direcciones 192.168.x; tampoco los adaptadores que
/// Windows no reconoce como red conectada (Hyper-V, Docker). Lo dice el Network List Manager de Windows.
/// </summary>
internal static class PrivateNetworks
{
    /// <summary>Categoría de red de Windows (<c>NLM_NETWORK_CATEGORY</c>).</summary>
    private const int Public = 0;

    /// <summary>Lo que dice Windows de las redes conectadas.</summary>
    /// <param name="Addresses">Las IPv4 de las redes privadas o de dominio, ordenadas.</param>
    /// <param name="PublicOnly">Hay redes conectadas, pero Windows las marca todas como públicas.</param>
    public sealed record Result(IReadOnlyList<IPAddress> Addresses, bool PublicOnly);

    public static Result Read()
    {
        var (trusted, anyPublic) = Adapters();
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && Guid.TryParse(n.Id, out var id) && trusted.Contains(id))
            .SelectMany(n => n.GetIPProperties().UnicastAddresses.Select(u => u.Address))
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && DeckWebServer.IsPrivate(a))
            .Distinct()
            .OrderBy(a => a.ToString(), StringComparer.Ordinal)
            .ToList();
        return new Result(addresses, addresses.Count == 0 && anyPublic);
    }

    /// <summary>Los adaptadores de las conexiones que no son públicas. Si Windows no responde, ninguno: no se escucha.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Sin respuesta de Windows, no se escucha: es lo seguro.")]
    private static (HashSet<Guid> Trusted, bool AnyPublic) Adapters()
    {
        var trusted = new HashSet<Guid>();
        bool anyPublic = false;
        INetworkListManager? manager = null;
        try
        {
            manager = (INetworkListManager)new NetworkListManagerClass();
            var connections = manager.GetNetworkConnections();
            while (true)
            {
                connections.Next(1, out var connection, out var fetched);
                if (fetched == 0 || connection is null)
                {
                    break;
                }

                if (connection.IsConnected() && connection.GetNetwork().GetCategory() is var category)
                {
                    if (category == Public)
                    {
                        anyPublic = true;
                    }
                    else
                    {
                        trusted.Add(connection.GetAdapterId());
                    }
                }

                Marshal.ReleaseComObject(connection);
            }

            Marshal.ReleaseComObject(connections);
        }
        catch (Exception)
        {
            trusted.Clear();
        }
        finally
        {
            if (manager is not null)
            {
                Marshal.FinalReleaseComObject(manager);
            }
        }

        return (trusted, anyPublic);
    }

    // --- Network List Manager (netlistmgr.h): sólo lo que se usa, en el orden de su vtable -------------

    [ComImport]
    [Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B")]
    private class NetworkListManagerClass
    {
    }

    [ComImport]
    [Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetworkListManager
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetNetworks(int flags);

        INetwork GetNetwork(Guid networkId);

        IEnumNetworkConnections GetNetworkConnections();
    }

    [ComImport]
    [Guid("DCB00006-570F-4A9B-8D69-199FDBA5723B")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IEnumNetworkConnections
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object NewEnum();

        void Next(uint count, out INetworkConnection? element, out uint fetched);
    }

    [ComImport]
    [Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetworkConnection
    {
        INetwork GetNetwork();

        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool IsConnectedToInternet();

        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool IsConnected();

        int GetConnectivity();

        Guid GetConnectionId();

        Guid GetAdapterId();
    }

    [ComImport]
    [Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetwork
    {
        [return: MarshalAs(UnmanagedType.BStr)]
        string GetName();

        void SetName([MarshalAs(UnmanagedType.BStr)] string name);

        [return: MarshalAs(UnmanagedType.BStr)]
        string GetDescription();

        void SetDescription([MarshalAs(UnmanagedType.BStr)] string description);

        Guid GetNetworkId();

        int GetDomainType();

        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetNetworkConnections();

        void GetTimeCreatedAndConnected(out uint createdLow, out uint createdHigh, out uint connectedLow, out uint connectedHigh);

        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool IsConnectedToInternet();

        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool IsConnected();

        int GetConnectivity();

        int GetCategory();
    }
}
