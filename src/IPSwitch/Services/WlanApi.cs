using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;

namespace IPSwitch.Services
{
    /// <summary>
    /// Queries the connected WiFi networks through the native Windows WLAN API (<c>wlanapi.dll</c>).
    /// </summary>
    /// <remarks>
    /// Preferred over parsing the output of <c>netsh wlan show interfaces</c>, which is localized.
    /// If the WLAN service is not available (computers without WiFi) an empty dictionary is returned.
    /// </remarks>
    public static class WlanApi
    {
        private const uint ClientVersion = 2;
        private const int WlanIntfOpcodeCurrentConnection = 7;
        private const int InterfaceStateConnected = 1;

        // Sizes and offsets of the native structures (4-byte alignment)
        private const int InterfaceInfoSize = 16 + 512 + 4;      // GUID + WCHAR[256] + WLAN_INTERFACE_STATE
        private const int InterfaceListHeaderSize = 8;           // dwNumberOfItems + dwIndex
        private const int SsidOffsetInConnectionAttributes = 4 + 4 + 512; // isState + wlanConnectionMode + strProfileName

        /// <summary>
        /// Returns the SSID each WiFi adapter is connected to.
        /// </summary>
        /// <returns>Dictionary "adapter name" → "SSID". Empty if no WiFi is connected.</returns>
        public static Dictionary<string, string> GetConnectedSsids()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IntPtr client = IntPtr.Zero;
            IntPtr list = IntPtr.Zero;

            try
            {
                if (WlanOpenHandle(ClientVersion, IntPtr.Zero, out _, out client) != 0) return result;
                if (WlanEnumInterfaces(client, IntPtr.Zero, out list) != 0) return result;

                int count = Marshal.ReadInt32(list);
                var nicsById = NetworkService.GetAllInterfaces()
                    .GroupBy(n => n.Id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < count; i++)
                {
                    IntPtr item = IntPtr.Add(list, InterfaceListHeaderSize + i * InterfaceInfoSize);
                    var guid = (Guid)Marshal.PtrToStructure(item, typeof(Guid))!;
                    int state = Marshal.ReadInt32(item, 16 + 512);
                    if (state != InterfaceStateConnected) continue;

                    string? ssid = QuerySsid(client, guid);
                    if (ssid != null && nicsById.TryGetValue(guid.ToString("B"), out string? name))
                        result[name] = ssid;
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException)
            {
                Debug.WriteLine($"[WlanApi] {ex.Message}");
            }
            finally
            {
                if (list != IntPtr.Zero) WlanFreeMemory(list);
                if (client != IntPtr.Zero) WlanCloseHandle(client, IntPtr.Zero);
            }

            return result;
        }

        private static string? QuerySsid(IntPtr client, Guid interfaceGuid)
        {
            IntPtr data = IntPtr.Zero;
            try
            {
                if (WlanQueryInterface(client, ref interfaceGuid, WlanIntfOpcodeCurrentConnection,
                        IntPtr.Zero, out _, out data, IntPtr.Zero) != 0)
                    return null;

                int length = Marshal.ReadInt32(data, SsidOffsetInConnectionAttributes);
                if (length <= 0 || length > 32) return null;

                var bytes = new byte[length];
                Marshal.Copy(IntPtr.Add(data, SsidOffsetInConnectionAttributes + 4), bytes, 0, length);
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                if (data != IntPtr.Zero) WlanFreeMemory(data);
            }
        }

        #region Win32

        [DllImport("wlanapi.dll")]
        private static extern int WlanOpenHandle(uint dwClientVersion, IntPtr pReserved, out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

        [DllImport("wlanapi.dll")]
        private static extern int WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

        [DllImport("wlanapi.dll")]
        private static extern int WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);

        [DllImport("wlanapi.dll")]
        private static extern int WlanQueryInterface(IntPtr hClientHandle, ref Guid pInterfaceGuid, int opCode,
            IntPtr pReserved, out int pdwDataSize, out IntPtr ppData, IntPtr pWlanOpcodeValueType);

        [DllImport("wlanapi.dll")]
        private static extern void WlanFreeMemory(IntPtr pMemory);

        #endregion
    }
}
