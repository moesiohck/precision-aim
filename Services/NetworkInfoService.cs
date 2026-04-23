using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AimAssistPro.Services
{
    /// <summary>
    /// Coleta silenciosa de identifiers de rede — IP público, IP local e MAC.
    /// Dados são enviados junto com login/ativação para fins de segurança.
    /// </summary>
    internal static class NetworkInfoService
    {
        private static readonly HttpClient _http = new(new HttpClientHandler
        {
            AllowAutoRedirect = true
        })
        { Timeout = TimeSpan.FromSeconds(6) };

        // Cache para não buscar múltiplas vezes
        private static string? _cachedPublicIp;
        private static string? _cachedLocalIp;
        private static string? _cachedMac;

        /// <summary>
        /// Coleta todas as informações de rede em paralelo.
        /// Retorna silenciosamente em caso de falha.
        /// </summary>
        public static async Task<NetworkInfo> CollectAsync()
        {
            var publicIpTask = GetPublicIpAsync();
            var localIp      = GetLocalIp();
            var mac          = GetPrimaryMac();

            string publicIp;
            try   { publicIp = await publicIpTask; }
            catch { publicIp = "unknown"; }

            return new NetworkInfo
            {
                PublicIp = publicIp,
                LocalIp  = localIp,
                Mac      = mac
            };
        }

        private static async Task<string> GetPublicIpAsync()
        {
            if (_cachedPublicIp != null) return _cachedPublicIp;
            try
            {
                // api.ipify.org — leve, retorna só o IP em texto plano
                var ip = (await _http.GetStringAsync("https://api.ipify.org")).Trim();
                _cachedPublicIp = ip;
                return ip;
            }
            catch
            {
                return "unknown";
            }
        }

        private static string GetLocalIp()
        {
            if (_cachedLocalIp != null) return _cachedLocalIp;
            try
            {
                using var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                sock.Connect("8.8.8.8", 65530);
                var ep = sock.LocalEndPoint as IPEndPoint;
                _cachedLocalIp = ep?.Address.ToString() ?? "unknown";
                return _cachedLocalIp;
            }
            catch { return "unknown"; }
        }

        private static string GetPrimaryMac()
        {
            if (_cachedMac != null) return _cachedMac;
            try
            {
                // Pega o primeiro adaptador de rede ativo (não loopback) com MAC real
                var mac = NetworkInterface
                    .GetAllNetworkInterfaces()
                    .Where(n =>
                        n.OperationalStatus == OperationalStatus.Up &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                        n.GetPhysicalAddress().GetAddressBytes().Length == 6)
                    .Select(n =>
                    {
                        var bytes = n.GetPhysicalAddress().GetAddressBytes();
                        return string.Join(":", bytes.Select(b => b.ToString("X2")));
                    })
                    .FirstOrDefault() ?? "unknown";

                _cachedMac = mac;
                return mac;
            }
            catch { return "unknown"; }
        }
    }

    internal class NetworkInfo
    {
        public string PublicIp { get; set; } = "unknown";
        public string LocalIp  { get; set; } = "unknown";
        public string Mac      { get; set; } = "unknown";
    }
}
