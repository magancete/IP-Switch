using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IPSwitch.Models;
using IPSwitch.Services;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Net.NetworkInformation;

namespace IPSwitch.ViewModels;

/// <summary>
/// ViewModel of the profile editor (add, edit, duplicate and "save current configuration").
/// </summary>
/// <remarks>
/// <para>
/// Validates every field while typing (<see cref="ObservableValidator"/>) with localized messages and also checks
/// rules that depend on the other profiles: unique name, a single DHCP profile per adapter, unique hotkey and a
/// single automatic profile per WiFi network and adapter.
/// </para>
/// <para>
/// Adapters are queried once when the editor opens (the query is slow); whether an adapter is wireless is cached.
/// </para>
/// <para>On save, a new <see cref="IPProfile"/> is created and reported through <see cref="Saved"/>.</para>
/// </remarks>
public partial class AddEditWindowViewModel : ObservableValidator
{
    private readonly List<IPProfile> _existingProfiles;
    private readonly IPProfile? _original;
    private readonly HashSet<string> _wifiInterfaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<HashSet<string>> _disabledWifiInterfaces = new(LoadDisabledWifiInterfaces);

    /// <summary>Creates the ViewModel.</summary>
    /// <param name="existingProfiles">Existing profiles, for the uniqueness checks.</param>
    /// <param name="profile">Profile to edit, or template for a new one; <see langword="null"/> for an empty profile.</param>
    /// <param name="isNew">
    /// <see langword="true"/> if the result will be a new profile (add, duplicate, current configuration);
    /// <see langword="false"/> if it replaces <paramref name="profile"/>.
    /// </param>
    public AddEditWindowViewModel(List<IPProfile> existingProfiles, IPProfile? profile, bool isNew)
    {
        _existingProfiles = existingProfiles;
        _original = isNew ? null : profile;

        LoadInterfaces();

        if (profile == null)
        {
            if (NetworkInterfaces.Count > 0) SelectedInterface = NetworkInterfaces[0];
        }
        else
        {
            LoadFrom(profile);
        }

        // No red error markers right after opening the window
        ClearErrors();
        SaveCommand.NotifyCanExecuteChanged();
    }

    #region Collections

    /// <summary>Available, non-hidden adapters (plus the profile's adapter if it is currently missing or hidden).</summary>
    public ObservableCollection<string> NetworkInterfaces { get; } = new();

    /// <summary>Masks from /32 to /1.</summary>
    public ObservableCollection<string> SubnetMasks { get; } = new(
        Enumerable.Range(1, 32).Reverse().Select(IpUtils.PrefixToMask));

    /// <summary>Available label colors (the first one, empty, means "no color").</summary>
    public IReadOnlyList<string> ColorPalette { get; } = IPProfile.ColorPalette;

    #endregion

    #region Fields

    /// <summary>Profile name.</summary>
    [ObservableProperty]
    [CustomValidation(typeof(AddEditWindowViewModel), nameof(ValidateName))]
    [NotifyDataErrorInfo]
    private string name = string.Empty;

    /// <summary>Selected adapter.</summary>
    [ObservableProperty]
    private string selectedInterface = string.Empty;

    /// <summary>Whether the IP address is obtained via DHCP.</summary>
    [ObservableProperty]
    [CustomValidation(typeof(AddEditWindowViewModel), nameof(ValidateDhcpConflict))]
    [NotifyDataErrorInfo]
    private bool isDhcp;

    /// <summary>Primary IPv4 address.</summary>
    [ObservableProperty]
    [CustomValidation(typeof(AddEditWindowViewModel), nameof(ValidateIpAddress))]
    [NotifyDataErrorInfo]
    private string ipAddress = string.Empty;

    /// <summary>Selected subnet mask.</summary>
    [ObservableProperty]
    private string selectedMask = "255.255.255.0";

    /// <summary>Gateway (optional).</summary>
    [ObservableProperty]
    [CustomValidation(typeof(AddEditWindowViewModel), nameof(ValidateOptionalIp))]
    [NotifyDataErrorInfo]
    private string gateway = string.Empty;

    /// <summary>Primary DNS server (optional).</summary>
    [ObservableProperty]
    [CustomValidation(typeof(AddEditWindowViewModel), nameof(ValidateOptionalIp))]
    [NotifyDataErrorInfo]
    private string dns1 = string.Empty;

    /// <summary>Secondary DNS server (optional).</summary>
    [ObservableProperty]
    [CustomValidation(typeof(AddEditWindowViewModel), nameof(ValidateOptionalIp))]
    [NotifyDataErrorInfo]
    private string dns2 = string.Empty;

    /// <summary>Additional addresses, one per line (<c>ip/prefix</c> or <c>ip mask</c>).</summary>
    [ObservableProperty]
    [CustomValidation(typeof(AddEditWindowViewModel), nameof(ValidateSecondaryAddresses))]
    [NotifyDataErrorInfo]
    private string secondaryAddressesText = string.Empty;

    /// <summary>Additional gateways, one per line (<c>ip</c> or <c>ip metric</c>).</summary>
    [ObservableProperty]
    [CustomValidation(typeof(AddEditWindowViewModel), nameof(ValidateAdditionalGateways))]
    [NotifyDataErrorInfo]
    private string additionalGatewaysText = string.Empty;

    /// <summary>Gateway metric as text (1–9999).</summary>
    [ObservableProperty]
    [CustomValidation(typeof(AddEditWindowViewModel), nameof(ValidateMetric))]
    [NotifyDataErrorInfo]
    private string gatewayMetricText = "1";

    /// <summary>Free-form notes.</summary>
    [ObservableProperty]
    private string notes = string.Empty;

    /// <summary>Selected label color (empty = no color).</summary>
    [ObservableProperty]
    private string selectedColor = string.Empty;

    /// <summary>Global hotkey (captured by pressing the combination).</summary>
    [ObservableProperty]
    [CustomValidation(typeof(AddEditWindowViewModel), nameof(ValidateHotkey))]
    [NotifyDataErrorInfo]
    private string hotkey = string.Empty;

    /// <summary>SSID that applies this profile automatically when connecting to it.</summary>
    [ObservableProperty]
    [CustomValidation(typeof(AddEditWindowViewModel), nameof(ValidateSsid))]
    [NotifyDataErrorInfo]
    private string autoApplySsid = string.Empty;

    /// <summary>
    /// Whether the selected adapter is wireless. The "apply automatically on a WiFi network" option is only shown
    /// (and only saved) in that case.
    /// </summary>
    public bool IsWifiInterface =>
        _wifiInterfaces.Contains(SelectedInterface) || _disabledWifiInterfaces.Value.Contains(SelectedInterface);

    /// <summary>Whether the advanced section should start expanded (because it has values).</summary>
    public bool HasAdvancedValues =>
        !string.IsNullOrWhiteSpace(SecondaryAddressesText) || !string.IsNullOrWhiteSpace(AdditionalGatewaysText) ||
        GatewayMetricText.Trim() != "1" ||
        !string.IsNullOrEmpty(Hotkey) || !string.IsNullOrEmpty(AutoApplySsid);

    #endregion

    /// <summary>Queries the adapters once and remembers which ones are wireless.</summary>
    private void LoadInterfaces()
    {
        var hidden = App.Settings.HiddenAdapters;
        foreach (var nic in NetworkService.GetUsefulInterfaces().OrderBy(n => n.Name))
        {
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                _wifiInterfaces.Add(nic.Name);

            if (!AdapterService.IsHidden(hidden, nic.Name) && !NetworkInterfaces.Contains(nic.Name))
                NetworkInterfaces.Add(nic.Name);
        }
    }

    /// <summary>Wireless adapters that are disabled in Windows (only queried if needed, through WMI).</summary>
    private static HashSet<string> LoadDisabledWifiInterfaces() =>
        new(AdapterService.GetDisabledAdapters()
                .Where(a => a.Description.IndexOf("Wireless", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            a.Description.IndexOf("Wi-Fi", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(a => a.Name),
            StringComparer.OrdinalIgnoreCase);

    private void LoadFrom(IPProfile profile)
    {
        // If the profile's adapter is not present (or hidden) right now, keep it in the list anyway
        if (!string.IsNullOrEmpty(profile.Interface) && !NetworkInterfaces.Contains(profile.Interface))
            NetworkInterfaces.Add(profile.Interface);

        Name = profile.Name;
        SelectedInterface = profile.Interface;
        IsDhcp = profile.IsDhcp;

        if (!IsDhcp)
        {
            IpAddress = profile.IPAddress;
            if (!SubnetMasks.Contains(profile.SubnetMask)) SubnetMasks.Add(profile.SubnetMask);
            SelectedMask = profile.SubnetMask;
            Gateway = NetworkService.IsSet(profile.Gateway) ? profile.Gateway : string.Empty;
        }

        Dns1 = NetworkService.IsSet(profile.PrimaryDNS) ? profile.PrimaryDNS : string.Empty;
        Dns2 = NetworkService.IsSet(profile.SecondaryDNS) ? profile.SecondaryDNS : string.Empty;
        SecondaryAddressesText = string.Join(Environment.NewLine, profile.SecondaryAddresses ?? new List<string>());
        AdditionalGatewaysText = string.Join(Environment.NewLine, profile.AdditionalGateways ?? new List<string>());
        GatewayMetricText = profile.GatewayMetric.ToString();
        Notes = profile.Notes;
        SelectedColor = IPProfile.ColorPalette.Contains(profile.Color) ? profile.Color : string.Empty;
        Hotkey = profile.Hotkey;
        AutoApplySsid = profile.AutoApplySsid;
    }

    /// <summary>Fills the SSID with the WiFi network the selected adapter is connected to right now.</summary>
    /// <returns><see langword="true"/> if a network was connected.</returns>
    public bool UseCurrentSsid()
    {
        if (WlanApi.GetConnectedSsids().TryGetValue(SelectedInterface, out string? ssid))
        {
            AutoApplySsid = ssid;
            return true;
        }
        return false;
    }

    #region Validation

    private IEnumerable<IPProfile> OtherProfiles => _existingProfiles.Where(p => !ReferenceEquals(p, _original));

    /// <summary>The name is required and unique.</summary>
    /// <param name="name">Value to validate.</param>
    /// <param name="context">Context with the ViewModel instance.</param>
    /// <returns>Validation result.</returns>
    public static ValidationResult? ValidateName(string name, ValidationContext context)
    {
        var vm = (AddEditWindowViewModel)context.ObjectInstance;

        if (string.IsNullOrWhiteSpace(name))
            return new ValidationResult(Loc.Get("Val_NameRequired"));

        bool duplicate = vm.OtherProfiles.Any(p =>
            string.Equals(p.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

        return duplicate ? new ValidationResult(Loc.Get("Val_NameDuplicate")) : ValidationResult.Success;
    }

    /// <summary>A single DHCP profile per adapter.</summary>
    /// <param name="isDhcp">Value to validate.</param>
    /// <param name="context">Context with the ViewModel instance.</param>
    /// <returns>Validation result.</returns>
    public static ValidationResult? ValidateDhcpConflict(bool isDhcp, ValidationContext context)
    {
        var vm = (AddEditWindowViewModel)context.ObjectInstance;

        bool conflict = isDhcp && vm.OtherProfiles.Any(p =>
            p.Interface == vm.SelectedInterface && p.IsDhcp);

        return conflict ? new ValidationResult(Loc.Get("Val_DhcpConflict")) : ValidationResult.Success;
    }

    /// <summary>The IP address is required and valid unless DHCP is used.</summary>
    /// <param name="ip">Value to validate.</param>
    /// <param name="context">Context with the ViewModel instance.</param>
    /// <returns>Validation result.</returns>
    public static ValidationResult? ValidateIpAddress(string ip, ValidationContext context)
    {
        var vm = (AddEditWindowViewModel)context.ObjectInstance;
        if (vm.IsDhcp) return ValidationResult.Success;

        if (string.IsNullOrWhiteSpace(ip))
            return new ValidationResult(Loc.Get("Val_IpRequired"));

        return IpUtils.IsValidIPv4(ip) ? ValidationResult.Success : new ValidationResult(Loc.Get("Val_IpInvalid"));
    }

    /// <summary>Optional IP address: empty or valid.</summary>
    /// <param name="ip">Value to validate.</param>
    /// <param name="context">Validation context.</param>
    /// <returns>Validation result.</returns>
    public static ValidationResult? ValidateOptionalIp(string ip, ValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(ip)) return ValidationResult.Success;
        return IpUtils.IsValidIPv4(ip) ? ValidationResult.Success : new ValidationResult(Loc.Get("Val_IpInvalid"));
    }

    /// <summary>Each line must be an address with mask; not allowed with DHCP.</summary>
    /// <param name="text">Value to validate.</param>
    /// <param name="context">Context with the ViewModel instance.</param>
    /// <returns>Validation result.</returns>
    public static ValidationResult? ValidateSecondaryAddresses(string text, ValidationContext context)
    {
        var vm = (AddEditWindowViewModel)context.ObjectInstance;
        var lines = SplitLines(text);
        if (lines.Count == 0) return ValidationResult.Success;

        if (vm.IsDhcp) return new ValidationResult(Loc.Get("Val_SecondaryDhcp"));

        return lines.All(l => IpUtils.TryParseAddressWithMask(l, out _, out _))
            ? ValidationResult.Success
            : new ValidationResult(Loc.Get("Val_SecondaryInvalid"));
    }

    /// <summary>Each line must be a gateway with an optional metric; not allowed with DHCP.</summary>
    /// <param name="text">Value to validate.</param>
    /// <param name="context">Context with the ViewModel instance.</param>
    /// <returns>Validation result.</returns>
    public static ValidationResult? ValidateAdditionalGateways(string text, ValidationContext context)
    {
        var vm = (AddEditWindowViewModel)context.ObjectInstance;
        var lines = SplitLines(text);
        if (lines.Count == 0) return ValidationResult.Success;

        if (vm.IsDhcp) return new ValidationResult(Loc.Get("Val_GatewaysDhcp"));

        return lines.All(l => IpUtils.TryParseGateway(l, out _, out _))
            ? ValidationResult.Success
            : new ValidationResult(Loc.Get("Val_GatewaysInvalid"));
    }

    /// <summary>Integer metric between 1 and 9999.</summary>
    /// <param name="text">Value to validate.</param>
    /// <param name="context">Validation context.</param>
    /// <returns>Validation result.</returns>
    public static ValidationResult? ValidateMetric(string text, ValidationContext context) =>
        int.TryParse(text?.Trim(), out int metric) && metric is >= 1 and <= 9999
            ? ValidationResult.Success
            : new ValidationResult(Loc.Get("Val_MetricInvalid"));

    /// <summary>Empty hotkey, or valid and not used by another profile.</summary>
    /// <param name="hotkey">Value to validate.</param>
    /// <param name="context">Context with the ViewModel instance.</param>
    /// <returns>Validation result.</returns>
    public static ValidationResult? ValidateHotkey(string hotkey, ValidationContext context)
    {
        if (string.IsNullOrEmpty(hotkey)) return ValidationResult.Success;
        if (!HotkeyService.TryParse(hotkey, out _, out _)) return new ValidationResult(Loc.Get("Val_HotkeyInvalid"));

        var vm = (AddEditWindowViewModel)context.ObjectInstance;
        bool duplicate = vm.OtherProfiles.Any(p => string.Equals(p.Hotkey, hotkey, StringComparison.OrdinalIgnoreCase));
        return duplicate ? new ValidationResult(Loc.Get("Val_HotkeyDuplicate")) : ValidationResult.Success;
    }

    /// <summary>A single automatic profile per SSID and adapter.</summary>
    /// <param name="ssid">Value to validate.</param>
    /// <param name="context">Context with the ViewModel instance.</param>
    /// <returns>Validation result.</returns>
    public static ValidationResult? ValidateSsid(string ssid, ValidationContext context)
    {
        if (string.IsNullOrEmpty(ssid)) return ValidationResult.Success;

        var vm = (AddEditWindowViewModel)context.ObjectInstance;
        bool duplicate = vm.OtherProfiles.Any(p => p.AutoApplySsid == ssid && p.Interface == vm.SelectedInterface);
        return duplicate ? new ValidationResult(Loc.Get("Val_SsidDuplicate")) : ValidationResult.Success;
    }

    private static List<string> SplitLines(string? text) =>
        (text ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

    #endregion

    #region Change handlers

    partial void OnNameChanged(string value) => ValidateAndRefresh(value, nameof(Name));
    partial void OnIpAddressChanged(string value) => ValidateAndRefresh(value, nameof(IpAddress));
    partial void OnGatewayChanged(string value) => ValidateAndRefresh(value, nameof(Gateway));
    partial void OnDns1Changed(string value) => ValidateAndRefresh(value, nameof(Dns1));
    partial void OnDns2Changed(string value) => ValidateAndRefresh(value, nameof(Dns2));
    partial void OnSecondaryAddressesTextChanged(string value) => ValidateAndRefresh(value, nameof(SecondaryAddressesText));
    partial void OnAdditionalGatewaysTextChanged(string value) => ValidateAndRefresh(value, nameof(AdditionalGatewaysText));
    partial void OnGatewayMetricTextChanged(string value) => ValidateAndRefresh(value, nameof(GatewayMetricText));
    partial void OnHotkeyChanged(string value) => ValidateAndRefresh(value, nameof(Hotkey));
    partial void OnAutoApplySsidChanged(string value) => ValidateAndRefresh(value, nameof(AutoApplySsid));

    partial void OnSelectedInterfaceChanged(string value)
    {
        OnPropertyChanged(nameof(IsWifiInterface));
        ValidateProperty(IsDhcp, nameof(IsDhcp));
        ValidateAndRefresh(AutoApplySsid, nameof(AutoApplySsid));
    }

    partial void OnIsDhcpChanged(bool value)
    {
        if (value)
        {
            ClearErrors(nameof(IpAddress));
            ClearErrors(nameof(Gateway));
        }
        else
        {
            ValidateProperty(IpAddress, nameof(IpAddress));
            ValidateProperty(Gateway, nameof(Gateway));
        }
        ValidateProperty(SecondaryAddressesText, nameof(SecondaryAddressesText));
        ValidateProperty(AdditionalGatewaysText, nameof(AdditionalGatewaysText));
        ValidateAndRefresh(value, nameof(IsDhcp));
    }

    private void ValidateAndRefresh(object? value, string propertyName)
    {
        ValidateProperty(value, propertyName);
        SaveCommand.NotifyCanExecuteChanged();
    }

    #endregion

    #region Save

    /// <summary>Raised on save, with the resulting profile.</summary>
    public event EventHandler<IPProfile>? Saved;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (!CanSave()) return;
        Saved?.Invoke(this, CreateProfileFromForm());
    }

    private bool CanSave()
    {
        if (string.IsNullOrWhiteSpace(SelectedInterface)) return false;
        if (ValidateMetric(GatewayMetricText, new ValidationContext(this)) != ValidationResult.Success) return false;
        if (ValidateSecondaryAddresses(SecondaryAddressesText, new ValidationContext(this)) != ValidationResult.Success) return false;
        if (ValidateAdditionalGateways(AdditionalGatewaysText, new ValidationContext(this)) != ValidationResult.Success) return false;

        if (!CreateProfileFromForm().IsValid) return false;

        var context = new ValidationContext(this);
        return ValidateName(Name, context) == ValidationResult.Success &&
               ValidateDhcpConflict(IsDhcp, context) == ValidationResult.Success &&
               ValidateHotkey(Hotkey, context) == ValidationResult.Success &&
               (!IsWifiInterface || ValidateSsid(AutoApplySsid, context) == ValidationResult.Success);
    }

    /// <summary>Builds the profile from the form values (without validating).</summary>
    /// <returns>A new profile.</returns>
    public IPProfile CreateProfileFromForm() => new()
    {
        Name = Name.Trim(),
        Interface = SelectedInterface,
        IPAddress = IsDhcp ? NetworkService.Dhcp : IpAddress.Trim(),
        SubnetMask = IsDhcp ? NetworkService.Dhcp : SelectedMask,
        Gateway = IsDhcp || string.IsNullOrWhiteSpace(Gateway) ? NetworkService.Dhcp : Gateway.Trim(),
        PrimaryDNS = string.IsNullOrWhiteSpace(Dns1) ? NetworkService.Dhcp : Dns1.Trim(),
        SecondaryDNS = string.IsNullOrWhiteSpace(Dns2) ? NetworkService.Dhcp : Dns2.Trim(),
        SecondaryAddresses = SplitLines(SecondaryAddressesText)
            .Select(l => IpUtils.TryNormalizeAddressWithMask(l, out var n) ? n : l)
            .ToList(),
        AdditionalGateways = IsDhcp
            ? new List<string>()
            : SplitLines(AdditionalGatewaysText).Select(l => IpUtils.TryNormalizeGateway(l, out var n) ? n : l).ToList(),
        GatewayMetric = int.TryParse(GatewayMetricText?.Trim(), out int metric) ? metric : 1,
        Notes = Notes.Trim(),
        Color = SelectedColor,
        Hotkey = Hotkey,
        // WiFi auto-switch makes no sense on wired adapters
        AutoApplySsid = IsWifiInterface ? AutoApplySsid.Trim() : string.Empty
    };

    #endregion
}
