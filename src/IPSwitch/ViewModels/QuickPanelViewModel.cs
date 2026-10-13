using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IPSwitch.Models;
using IPSwitch.Services;
using System.Collections.ObjectModel;
using System.Net.NetworkInformation;

namespace IPSwitch.ViewModels;

/// <summary>
/// A network adapter and its profiles, as shown in the quick panel.
/// </summary>
public sealed class QuickAdapter
{
    /// <summary>Creates the item.</summary>
    /// <param name="name">Adapter name.</param>
    /// <param name="ipAddress">Current IP address, or the "no IP" text.</param>
    /// <param name="isUp">Whether the adapter is connected.</param>
    /// <param name="isWifi">Whether it is a wireless adapter.</param>
    /// <param name="profiles">Profiles configured for this adapter.</param>
    public QuickAdapter(string name, string ipAddress, bool isUp, bool isWifi, IEnumerable<IPProfile> profiles)
    {
        Name = name;
        IpAddress = ipAddress;
        IsUp = isUp;
        IsWifi = isWifi;
        Profiles = profiles.ToList();
    }

    /// <summary>Adapter name.</summary>
    public string Name { get; }

    /// <summary>Current IP address, or the "no IP" text.</summary>
    public string IpAddress { get; }

    /// <summary>Whether the adapter is connected.</summary>
    public bool IsUp { get; }

    /// <summary>Whether the adapter is wireless.</summary>
    public bool IsWifi { get; }

    /// <summary>Profiles of this adapter, sorted by name.</summary>
    public IReadOnlyList<IPProfile> Profiles { get; }

    /// <summary>Whether this adapter has no profiles.</summary>
    public bool HasNoProfiles => Profiles.Count == 0;
}

/// <summary>
/// ViewModel of the quick panel used by the compact view and the tray flyout:
/// visible adapters, their IP address and their profiles, applicable with one click.
/// </summary>
/// <remarks>
/// Adapters are read on a background thread (it can take several hundred milliseconds); profiles are matched on the
/// UI thread because <see cref="ProfileService.Profiles"/> is only modified there.
/// </remarks>
public partial class QuickPanelViewModel : ObservableObject
{
    private readonly Func<IPProfile, Task> _apply;
    private int _refreshVersion;

    /// <summary>Creates the ViewModel. The host view loads the data when it is shown (see <see cref="RefreshAsync"/>).</summary>
    /// <param name="apply">Function that applies a profile (with notifications and verification).</param>
    public QuickPanelViewModel(Func<IPProfile, Task> apply) => _apply = apply;

    /// <summary>Adapters with their profiles.</summary>
    public ObservableCollection<QuickAdapter> Adapters { get; } = new();

    /// <summary>Whether there are no adapters to show.</summary>
    [ObservableProperty]
    private bool isEmpty;

    /// <summary>Explanation for the empty state (all hidden, or none connected/enabled).</summary>
    [ObservableProperty]
    private string emptyHint = string.Empty;

    /// <summary>Rebuilds the list from the current network state and profiles.</summary>
    /// <returns>A task that completes when the list has been updated.</returns>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        int version = ++_refreshVersion;
        string noIp = Loc.Get("Dash_NoIp");
        var hidden = App.Settings.HiddenAdapters.ToList();

        var snapshot = await Task.Run(() =>
        {
            var useful = NetworkService.GetUsefulInterfaces().ToList();
            var visible = useful
                .Where(n => !AdapterService.IsHidden(hidden, n.Name))
                .OrderBy(n => n.Name)
                .Select(n => new
                {
                    n.Name,
                    Ip = NetworkService.GetIPv4Address(n) ?? noIp,
                    IsUp = n.OperationalStatus == OperationalStatus.Up,
                    IsWifi = n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                })
                .ToList();
            return new { Visible = visible, HiddenCount = useful.Count - visible.Count };
        });

        if (version != _refreshVersion) return; // A newer refresh is already running

        Adapters.Clear();
        foreach (var nic in snapshot.Visible)
        {
            var profiles = ProfileService.Profiles
                .Where(p => string.Equals(p.Interface, nic.Name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Name);
            Adapters.Add(new QuickAdapter(nic.Name, nic.Ip, nic.IsUp, nic.IsWifi, profiles));
        }

        IsEmpty = Adapters.Count == 0;
        EmptyHint = IsEmpty ? GetEmptyHint(snapshot.HiddenCount) : string.Empty;
    }

    /// <summary>Help text for the empty state.</summary>
    /// <param name="hiddenCount">Adapters that exist but are hidden by the user.</param>
    /// <returns>Localized message.</returns>
    public static string GetEmptyHint(int hiddenCount) =>
        hiddenCount > 0 ? Loc.Format("Empty_AllHidden", hiddenCount) : Loc.Get("Empty_NoAdaptersHint");

    /// <summary>Applies the clicked profile.</summary>
    /// <param name="profile">Profile to apply.</param>
    /// <returns>The apply task.</returns>
    [RelayCommand]
    private Task Apply(IPProfile? profile) => profile == null ? Task.CompletedTask : _apply(profile);
}
