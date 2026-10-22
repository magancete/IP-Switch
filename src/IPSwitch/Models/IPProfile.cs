using CommunityToolkit.Mvvm.ComponentModel;
using IPSwitch.Services;
using System.Web.Script.Serialization;

namespace IPSwitch.Models
{
    /// <summary>
    /// IPv4 configuration profile for a specific network adapter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Persisted in <c>profiles.json</c> through <see cref="JsonStore"/>. Address fields use the sentinel value
    /// <see cref="NetworkService.Dhcp"/> (<c>"DHCP"</c>) to mean "automatic".
    /// </para>
    /// <para>
    /// Properties added in later versions have default values so older files keep loading unchanged.
    /// </para>
    /// </remarks>
    public partial class IPProfile : ObservableObject
    {
        /// <summary>Colors available to label profiles (empty string = no color).</summary>
        public static readonly string[] ColorPalette =
        {
            "", "#E74C3C", "#E67E22", "#F1C40F", "#2ECC71", "#1ABC9C", "#3498DB", "#9B59B6", "#95A5A6"
        };

        /// <summary>Display name of the profile. Unique (case-insensitive) among all profiles.</summary>
        [ObservableProperty] private string _name = string.Empty;

        /// <summary>Primary IPv4 address, or <see cref="NetworkService.Dhcp"/>.</summary>
        [ObservableProperty] private string _iPAddress = NetworkService.Dhcp;

        /// <summary>Dotted-decimal subnet mask, or <see cref="NetworkService.Dhcp"/>.</summary>
        [ObservableProperty] private string _subnetMask = NetworkService.Dhcp;

        /// <summary>Default gateway, or <see cref="NetworkService.Dhcp"/> if none is set.</summary>
        [ObservableProperty] private string _gateway = NetworkService.Dhcp;

        /// <summary>Primary DNS server, or <see cref="NetworkService.Dhcp"/> to obtain it automatically.</summary>
        [ObservableProperty] private string _primaryDNS = NetworkService.Dhcp;

        /// <summary>Secondary DNS server, or <see cref="NetworkService.Dhcp"/> if none is set.</summary>
        [ObservableProperty] private string _secondaryDNS = NetworkService.Dhcp;

        /// <summary>Name of the network adapter (e.g. <c>Ethernet</c>) the profile applies to.</summary>
        [ObservableProperty] private string _interface = string.Empty;

        /// <summary>Additional IPv4 addresses in canonical <c>ip/prefix</c> form. Static IP only.</summary>
        [ObservableProperty] private List<string> _secondaryAddresses = new();

        /// <summary>
        /// Additional default gateways, as in the Windows advanced TCP/IP settings: <c>ip</c> or <c>ip metric</c>
        /// (see <see cref="IpUtils.TryParseGateway"/>). Static IP only.
        /// </summary>
        [ObservableProperty] private List<string> _additionalGateways = new();

        /// <summary>Gateway metric (1–9999). Lower values take priority.</summary>
        [ObservableProperty] private int _gatewayMetric = 1;

        /// <summary>Free-form user notes about the profile.</summary>
        [ObservableProperty] private string _notes = string.Empty;

        /// <summary>Label color in <c>#RRGGBB</c> format, or empty if none.</summary>
        [ObservableProperty] private string _color = string.Empty;

        /// <summary>Global hotkey that applies the profile (e.g. <c>Ctrl+Alt+1</c>), or empty.</summary>
        [ObservableProperty] private string _hotkey = string.Empty;

        /// <summary>WiFi SSID that, when connected to, automatically applies this profile. Empty = disabled.</summary>
        [ObservableProperty] private string _autoApplySsid = string.Empty;

        /// <summary>Whether the adapter's current configuration matches this profile.</summary>
        [ObservableProperty] private bool _isActive;

        /// <summary>Whether the profile obtains its IP address via DHCP.</summary>
        [ScriptIgnore]
        public bool IsDhcp => IPAddress == NetworkService.Dhcp;

        /// <summary>Whether the profile can be applied (it has no validation errors).</summary>
        [ScriptIgnore]
        public bool IsValid => GetValidationErrorKey() == null;

        /// <summary>Localized validation message, or <see langword="null"/> if the profile is valid.</summary>
        [ScriptIgnore]
        public string? ValidationMessage => GetValidationErrorKey() is { } key ? Loc.Get(key) : null;

        /// <summary>Whether the profile has a label color.</summary>
        [ScriptIgnore]
        public bool HasColor => !string.IsNullOrEmpty(Color);

        /// <summary>Whether the profile has notes.</summary>
        [ScriptIgnore]
        public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

        /// <summary>
        /// Validates the whole profile and returns the resource key of the first error found.
        /// </summary>
        /// <returns>Key in <c>Resources/Strings.*.xaml</c> describing the error, or <see langword="null"/> if valid.</returns>
        public string? GetValidationErrorKey()
        {
            if (string.IsNullOrWhiteSpace(Name)) return "Val_NameRequired";
            if (string.IsNullOrWhiteSpace(Interface)) return "Val_InterfaceRequired";

            var secondary = SecondaryAddresses ?? new List<string>();
            var gateways = AdditionalGateways ?? new List<string>();
            if (IsDhcp)
            {
                if (secondary.Count > 0) return "Val_SecondaryDhcp";
                if (gateways.Count > 0) return "Val_GatewaysDhcp";
            }
            else
            {
                if (!IpUtils.IsValidIPv4(IPAddress)) return "Val_IpInvalid";
                if (!IpUtils.IsValidMask(SubnetMask)) return "Val_MaskInvalid";
                if (NetworkService.IsSet(Gateway) && !IpUtils.IsValidIPv4(Gateway)) return "Val_IpInvalid";
                if (secondary.Any(a => !IpUtils.TryParseAddressWithMask(a, out _, out _))) return "Val_SecondaryInvalid";
                if (gateways.Any(g => !IpUtils.TryParseGateway(g, out _, out _))) return "Val_GatewaysInvalid";
            }

            if (NetworkService.IsSet(PrimaryDNS) && !IpUtils.IsValidIPv4(PrimaryDNS)) return "Val_IpInvalid";
            if (NetworkService.IsSet(SecondaryDNS) && !IpUtils.IsValidIPv4(SecondaryDNS)) return "Val_IpInvalid";
            if (GatewayMetric < 1 || GatewayMetric > 9999) return "Val_MetricInvalid";
            if (!string.IsNullOrEmpty(Hotkey) && !HotkeyService.TryParse(Hotkey, out _, out _)) return "Val_HotkeyInvalid";

            return null;
        }

        /// <summary>
        /// Creates an independent copy of the profile. The copy is never marked as active.
        /// </summary>
        /// <param name="newName">Name for the copy; if <see langword="null"/> the original name is kept.</param>
        /// <returns>A new <see cref="IPProfile"/> with the same values.</returns>
        public IPProfile Clone(string? newName = null) => new()
        {
            Name = newName ?? Name,
            IPAddress = IPAddress,
            SubnetMask = SubnetMask,
            Gateway = Gateway,
            PrimaryDNS = PrimaryDNS,
            SecondaryDNS = SecondaryDNS,
            Interface = Interface,
            SecondaryAddresses = new List<string>(SecondaryAddresses ?? new List<string>()),
            AdditionalGateways = new List<string>(AdditionalGateways ?? new List<string>()),
            GatewayMetric = GatewayMetric,
            Notes = Notes,
            Color = Color,
            Hotkey = Hotkey,
            AutoApplySsid = AutoApplySsid,
            IsActive = false
        };

        /// <inheritdoc />
        public override string ToString() => $"{Name} ({Interface}: {IPAddress})";
    }
}
