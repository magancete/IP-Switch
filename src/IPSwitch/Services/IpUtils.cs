using System.Net;
using System.Net.Sockets;

namespace IPSwitch.Services
{
    /// <summary>
    /// Pure (side-effect free) helpers to validate and convert IPv4 addresses and masks.
    /// </summary>
    /// <remarks>
    /// Every function is deterministic and thread-safe, so they are fully covered by unit tests.
    /// </remarks>
    public static class IpUtils
    {
        /// <summary>
        /// Whether <paramref name="text"/> is a dotted-decimal IPv4 address with exactly four octets.
        /// </summary>
        /// <param name="text">Text to validate. Leading and trailing whitespace is ignored.</param>
        /// <returns><see langword="true"/> if it is a complete IPv4 address (e.g. <c>192.168.1.10</c>).</returns>
        /// <remarks>
        /// <see cref="IPAddress.TryParse(string, out IPAddress)"/> accepts abbreviated forms such as <c>10</c> or
        /// <c>1.2.3</c>; all four octets are required here to avoid unexpected configurations.
        /// </remarks>
        public static bool IsValidIPv4(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string trimmed = text!.Trim();

            var parts = trimmed.Split('.');
            if (parts.Length != 4 || parts.Any(p => p.Length == 0 || p.Length > 3 || !p.All(char.IsDigit)))
                return false;

            return IPAddress.TryParse(trimmed, out var address) && address.AddressFamily == AddressFamily.InterNetwork;
        }

        /// <summary>
        /// Converts a CIDR prefix length into a dotted-decimal mask.
        /// </summary>
        /// <param name="prefix">Prefix length, between 0 and 32.</param>
        /// <returns>The dotted-decimal mask, e.g. <c>255.255.255.0</c> for <c>24</c>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">If <paramref name="prefix"/> is not between 0 and 32.</exception>
        public static string PrefixToMask(int prefix)
        {
            if (prefix < 0 || prefix > 32)
                throw new ArgumentOutOfRangeException(nameof(prefix), prefix, "Prefix must be between 0 and 32.");

            uint mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
            return $"{mask >> 24}.{(mask >> 16) & 255}.{(mask >> 8) & 255}.{mask & 255}";
        }

        /// <summary>
        /// Converts a dotted-decimal mask into its CIDR prefix length.
        /// </summary>
        /// <param name="mask">Dotted-decimal mask.</param>
        /// <param name="prefix">Resulting prefix length when the conversion succeeds.</param>
        /// <returns><see langword="true"/> if the mask is valid (contiguous one bits).</returns>
        public static bool TryMaskToPrefix(string? mask, out int prefix)
        {
            prefix = 0;
            if (!IsValidIPv4(mask)) return false;

            uint value = mask!.Trim().Split('.').Aggregate(0u, (acc, octet) => (acc << 8) | uint.Parse(octet));

            // A valid mask is a run of ones followed by zeros: ~value + 1 must be a power of two
            uint inverted = ~value;
            if ((inverted & (inverted + 1)) != 0) return false;

            while (value != 0)
            {
                prefix++;
                value <<= 1;
            }
            return true;
        }

        /// <summary>
        /// Whether <paramref name="mask"/> is a valid subnet mask (contiguous one bits).
        /// </summary>
        /// <param name="mask">Dotted-decimal mask.</param>
        /// <returns><see langword="true"/> if it is valid.</returns>
        public static bool IsValidMask(string? mask) => TryMaskToPrefix(mask, out _);

        /// <summary>
        /// Parses an address with mask written as <c>ip/prefix</c> or <c>ip mask</c>.
        /// </summary>
        /// <param name="text">Input text, e.g. <c>10.0.0.5/24</c> or <c>10.0.0.5 255.255.255.0</c>.</param>
        /// <param name="address">Resulting IPv4 address.</param>
        /// <param name="mask">Resulting dotted-decimal mask.</param>
        /// <returns><see langword="true"/> if the text is valid.</returns>
        public static bool TryParseAddressWithMask(string? text, out string address, out string mask)
        {
            address = string.Empty;
            mask = string.Empty;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string trimmed = text!.Trim();
            bool isCidr = trimmed.IndexOf('/') >= 0;

            string[] parts = isCidr
                ? trimmed.Split('/')
                : trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2 || !IsValidIPv4(parts[0])) return false;

            string candidateMask;
            if (isCidr)
            {
                if (!int.TryParse(parts[1].Trim(), out int prefix) || prefix < 1 || prefix > 32) return false;
                candidateMask = PrefixToMask(prefix);
            }
            else
            {
                if (!IsValidMask(parts[1])) return false;
                candidateMask = parts[1].Trim();
            }

            address = parts[0].Trim();
            mask = candidateMask;
            return true;
        }

        /// <summary>
        /// Normalizes an address with mask to the canonical <c>ip/prefix</c> form used for persistence.
        /// </summary>
        /// <param name="text">Input text in any format accepted by <see cref="TryParseAddressWithMask"/>.</param>
        /// <param name="normalized">Resulting canonical form.</param>
        /// <returns><see langword="true"/> if the text is valid.</returns>
        public static bool TryNormalizeAddressWithMask(string? text, out string normalized)
        {
            normalized = string.Empty;
            if (!TryParseAddressWithMask(text, out string address, out string mask)) return false;

            TryMaskToPrefix(mask, out int prefix);
            normalized = $"{address}/{prefix}";
            return true;
        }
    }
}
