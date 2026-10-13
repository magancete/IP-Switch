using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IPSwitch.Models;
using IPSwitch.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace IPSwitch.ViewModels;

/// <summary>
/// ViewModel of the Home page: current state of every useful network adapter.
/// </summary>
/// <remarks>
/// Reading adapters is slow (<see cref="NetworkInterface.GetAllNetworkInterfaces"/> and WMI can take several hundred
/// milliseconds), so <see cref="RefreshAsync"/> collects the data on a background thread and only touches the
/// observable collection on the UI thread. If several refreshes overlap, only the most recent one is applied.
/// </remarks>
public partial class DashboardPageViewModel : ObservableObject
{
    private int _refreshVersion;

    /// <summary>Adapters with their current state.</summary>
    public ObservableCollection<NetworkInterfaceDetail> Interfaces { get; } = new();

    /// <summary>Whether there is any adapter to show.</summary>
    [ObservableProperty]
    private bool hasInterfaces = true;

    /// <summary>Whether hidden adapters are shown as well.</summary>
    [ObservableProperty]
    private bool showHidden;

    /// <summary>Number of hidden adapters (for the "Show hidden adapters (N)" label).</summary>
    [ObservableProperty]
    private int hiddenCount;

    /// <summary>Explanation for the empty state (all hidden, or none available).</summary>
    [ObservableProperty]
    private string emptyHint = string.Empty;

    partial void OnShowHiddenChanged(bool value) => _ = RefreshAsync();

    /// <summary>
    /// Reads the state of every adapter again: enabled ones (with their IP data) and adapters disabled in Windows
    /// (name and description only), filtering hidden ones unless requested.
    /// </summary>
    /// <returns>A task that completes when the list has been updated.</returns>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        int version = ++_refreshVersion;

        // Localized texts are read on the UI thread: Application resources are not thread-safe
        var texts = new DetailTexts();
        var hidden = App.Settings.HiddenAdapters.ToList();

        var details = await Task.Run(() => CollectDetails(texts));
        if (version != _refreshVersion) return; // A newer refresh is already running

        foreach (var detail in details)
            detail.IsHidden = AdapterService.IsHidden(hidden, detail.Name);

        HiddenCount = details.Count(d => d.IsHidden);

        Interfaces.Clear();
        foreach (var detail in details.Where(d => ShowHidden || !d.IsHidden).OrderBy(d => d.Name))
            Interfaces.Add(detail);

        HasInterfaces = Interfaces.Count > 0;
        EmptyHint = HasInterfaces ? string.Empty : QuickPanelViewModel.GetEmptyHint(HiddenCount);
    }

    /// <summary>Collects the details of enabled and disabled adapters (runs on a background thread).</summary>
    private static List<NetworkInterfaceDetail> CollectDetails(DetailTexts texts)
    {
        var details = NetworkService.GetUsefulInterfaces().Select(n => CreateDetail(n, texts)).ToList();

        foreach (var disabled in AdapterService.GetDisabledAdapters())
        {
            if (details.Any(d => string.Equals(d.Name, disabled.Name, StringComparison.OrdinalIgnoreCase))) continue;
            details.Add(CreateDisabledDetail(disabled, texts));
        }

        return details;
    }

    private static NetworkInterfaceDetail CreateDisabledDetail(DisabledAdapter adapter, DetailTexts texts)
    {
        string na = texts.NotAvailable;
        return new NetworkInterfaceDetail
        {
            Name = adapter.Name,
            Description = adapter.Description,
            IsEnabled = false,
            Status = texts.Disabled,
            InterfaceType = adapter.Description.IndexOf("Wireless", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            adapter.Description.IndexOf("Wi-Fi", StringComparison.OrdinalIgnoreCase) >= 0
                ? "WiFi" : "Ethernet",
            MacAddress = na, IpAddress = na, SubnetMask = na, Gateway = na, DnsServers = na, Mode = na, Speed = na
        };
    }

    private static NetworkInterfaceDetail CreateDetail(NetworkInterface nic, DetailTexts texts)
    {
        string na = texts.NotAvailable;
        string mac = string.Join(":", nic.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
        bool isUp = nic.OperationalStatus == OperationalStatus.Up;

        var detail = new NetworkInterfaceDetail
        {
            Name = nic.Name,
            Description = nic.Description,
            IsUp = isUp,
            Status = isUp ? texts.Up : texts.Down,
            MacAddress = string.IsNullOrEmpty(mac) ? na : mac,
            Speed = isUp && nic.Speed > 0 ? FormatSpeed(nic.Speed) : na,
            InterfaceType = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "WiFi" : "Ethernet",
            IpAddress = na,
            SubnetMask = na,
            Gateway = na,
            DnsServers = na,
            Mode = na
        };

        PopulateIpProperties(nic, detail, texts);
        return detail;
    }

    /// <summary>Formats a speed in bits per second as Mbps or Gbps.</summary>
    /// <param name="bitsPerSecond">Link speed.</param>
    /// <returns>Text with units.</returns>
    public static string FormatSpeed(long bitsPerSecond) =>
        bitsPerSecond >= 1_000_000_000
            ? (bitsPerSecond / 1_000_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + " Gbps"
            : $"{bitsPerSecond / 1_000_000} Mbps";

    private static void PopulateIpProperties(NetworkInterface nic, NetworkInterfaceDetail detail, DetailTexts texts)
    {
        try
        {
            if (!nic.Supports(NetworkInterfaceComponent.IPv4)) return;

            var props = nic.GetIPProperties();
            detail.IsDhcp = props.GetIPv4Properties()?.IsDhcpEnabled == true;
            detail.Mode = detail.IsDhcp ? texts.Dhcp : texts.Static;

            var ipv4 = props.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

            if (ipv4 != null)
            {
                detail.IpAddress = ipv4.Address.ToString();
                detail.SubnetMask = ipv4.IPv4Mask?.ToString() ?? detail.SubnetMask;
            }
            else
            {
                detail.IpAddress = texts.NoIp;
            }

            detail.Gateway = props.GatewayAddresses
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?
                .ToString() ?? detail.Gateway;

            var dns = props.DnsAddresses
                .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                .Select(d => d.ToString())
                .ToList();

            detail.DnsServers = dns.Count > 0 ? string.Join(", ", dns) : texts.None;
        }
        catch (NetworkInformationException)
        {
            // No IP information available for this adapter: keep the N/A values
        }
    }

    /// <summary>Localized texts captured on the UI thread for use by the background collection.</summary>
    private sealed class DetailTexts
    {
        public readonly string NotAvailable = Loc.Get("Common_NotAvailable");
        public readonly string None = Loc.Get("Common_None");
        public readonly string NoIp = Loc.Get("Dash_NoIp");
        public readonly string Up = Loc.Get("Status_Up");
        public readonly string Down = Loc.Get("Status_Down");
        public readonly string Disabled = Loc.Get("Status_Disabled");
        public readonly string Dhcp = Loc.Get("Mode_Dhcp");
        public readonly string Static = Loc.Get("Mode_Static");
    }
}
