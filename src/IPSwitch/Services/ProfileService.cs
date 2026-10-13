using IPSwitch.Models;
using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace IPSwitch.Services
{
    /// <summary>
    /// Central profile store: observable collection shared by every view, persistence in <c>profiles.json</c>
    /// and detection of the active profile.
    /// </summary>
    /// <remarks>
    /// Every change must be made on the UI thread and end with <see cref="Save"/>, which raises <see cref="Saved"/>
    /// so hotkeys, the tray and the views are updated.
    /// </remarks>
    public static class ProfileService
    {
        private static readonly string FilePath = AppPaths.GetDataFile("profiles.json");

        /// <summary>Profile collection shared by the Profiles page, the compact view and the tray.</summary>
        public static ObservableCollection<IPProfile> Profiles { get; } = new(LoadFromDisk());

        /// <summary>Raised after the profiles have been saved to disk.</summary>
        public static event EventHandler? Saved;

        /// <summary>Saves every profile to disk and raises <see cref="Saved"/>.</summary>
        public static void Save()
        {
            JsonStore.Save(FilePath, Profiles.ToList());
            Saved?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Applies a profile with netsh and, if it succeeds, marks it as active (and the other profiles of the same
        /// adapter as inactive).
        /// </summary>
        /// <param name="profile">Profile to apply.</param>
        /// <returns>The netsh result.</returns>
        /// <remarks>Does not check connectivity; use <see cref="ProfileApplier"/> for the complete flow.</remarks>
        public static async Task<CommandResult> ApplyAsync(IPProfile profile)
        {
            var result = await NetworkService.ApplyProfile(profile);
            if (!result.Success) return result;

            foreach (var p in Profiles.Where(p => p.Interface == profile.Interface))
                p.IsActive = ReferenceEquals(p, profile);

            Save();
            return result;
        }

        /// <summary>Finds a profile by name (case-insensitive).</summary>
        /// <param name="name">Profile name.</param>
        /// <returns>The profile, or <see langword="null"/>.</returns>
        public static IPProfile? FindByName(string name) =>
            Profiles.FirstOrDefault(p => string.Equals(p.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Marks as active the profiles that match the real configuration of each adapter.
        /// </summary>
        /// <returns>A task that completes when the flags have been updated.</returns>
        /// <remarks>
        /// Querying the adapters is slow (hundreds of milliseconds), so it runs on a background thread; the
        /// <see cref="IPProfile.IsActive"/> flags are updated on the calling (UI) thread. The file is only saved
        /// when a flag actually changed. Must be called from the UI thread.
        /// </remarks>
        public static async Task DetectActiveProfilesAsync()
        {
            var snapshot = Profiles.ToList();
            var active = await Task.Run(() => ComputeActiveFlags(snapshot));

            bool changed = false;
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i].IsActive == active[i]) continue;
                snapshot[i].IsActive = active[i];
                changed = true;
            }

            if (changed) Save();
        }

        /// <summary>Computes, for each profile, whether it matches its adapter's current configuration.</summary>
        private static bool[] ComputeActiveFlags(IReadOnlyList<IPProfile> profiles)
        {
            var nics = NetworkService.GetAllInterfaces();
            return profiles
                .Select(profile =>
                {
                    var nic = nics.FirstOrDefault(n =>
                        string.Equals(n.Name, profile.Interface, StringComparison.OrdinalIgnoreCase));
                    return nic?.OperationalStatus == OperationalStatus.Up && Matches(profile, nic);
                })
                .ToArray();
        }

        private static List<IPProfile> LoadFromDisk()
        {
            var profiles = JsonStore.Load<List<IPProfile>>(FilePath) ?? new List<IPProfile>();

            // Old or hand-edited files may contain null lists
            foreach (var p in profiles)
                p.SecondaryAddresses ??= new List<string>();

            return profiles;
        }

        /// <summary>Whether the adapter's current configuration matches the profile (primary address only).</summary>
        private static bool Matches(IPProfile profile, NetworkInterface nic)
        {
            IPInterfaceProperties ipProps;
            bool isDhcp;
            try
            {
                ipProps = nic.GetIPProperties();
                isDhcp = ipProps.GetIPv4Properties()?.IsDhcpEnabled == true;
            }
            catch (NetworkInformationException)
            {
                return false;
            }

            if (profile.IsDhcp)
                return isDhcp;

            if (isDhcp) return false;

            var unicast = ipProps.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

            var gateway = ipProps.GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);

            bool ipMatch = unicast?.Address.ToString() == profile.IPAddress;
            bool maskMatch = unicast?.IPv4Mask?.ToString() == profile.SubnetMask;
            bool gatewayMatch = !NetworkService.IsSet(profile.Gateway) ||
                                gateway?.Address.ToString() == profile.Gateway;

            return ipMatch && maskMatch && gatewayMatch;
        }
    }
}
