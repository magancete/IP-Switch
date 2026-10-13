using IPSwitch.Models;
using System.IO;
using System.Web.Script.Serialization;

namespace IPSwitch.Services
{
    /// <summary>
    /// Import and export of profiles to JSON files, to copy them between computers or share them.
    /// </summary>
    /// <remarks>
    /// The format is the same as <c>profiles.json</c>: a JSON array of <see cref="IPProfile"/>.
    /// The <see cref="IPProfile.IsActive"/> state, which depends on the computer, is not exported.
    /// </remarks>
    public static class ProfileTransferService
    {
        private static readonly JavaScriptSerializer Serializer = new();

        /// <summary>Writes the profiles to a JSON file.</summary>
        /// <param name="path">Target file path (overwritten).</param>
        /// <param name="profiles">Profiles to export.</param>
        /// <exception cref="IOException">If the file cannot be written.</exception>
        public static void Export(string path, IEnumerable<IPProfile> profiles)
        {
            var copies = profiles.Select(p => p.Clone()).ToList();
            File.WriteAllText(path, Serializer.Serialize(copies));
        }

        /// <summary>Reads profiles from a JSON file and validates them.</summary>
        /// <param name="path">Source file path.</param>
        /// <returns>The profiles read (not marked as active).</returns>
        /// <exception cref="InvalidDataException">If the content is not a valid profile array.</exception>
        public static List<IPProfile> Read(string path)
        {
            List<IPProfile>? profiles;
            try
            {
                profiles = Serializer.Deserialize<List<IPProfile>>(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                throw new InvalidDataException(ex.Message, ex);
            }

            if (profiles == null || profiles.Any(p => p == null || string.IsNullOrWhiteSpace(p.Name)))
                throw new InvalidDataException("The file does not contain a valid profile list.");

            profiles.ForEach(p => p.IsActive = false);
            return profiles;
        }

        /// <summary>
        /// Adds imported profiles to a collection, renaming those that match an existing one
        /// (<c>Name (2)</c>, <c>Name (3)</c>…) and dropping repeated hotkeys.
        /// </summary>
        /// <param name="target">Target collection.</param>
        /// <param name="imported">Imported profiles.</param>
        /// <returns>Number of profiles added.</returns>
        public static int Merge(ICollection<IPProfile> target, IEnumerable<IPProfile> imported)
        {
            int added = 0;
            foreach (var profile in imported)
            {
                profile.Name = GetUniqueName(target, profile.Name);

                if (!string.IsNullOrEmpty(profile.Hotkey) &&
                    target.Any(p => string.Equals(p.Hotkey, profile.Hotkey, StringComparison.OrdinalIgnoreCase)))
                    profile.Hotkey = string.Empty;

                target.Add(profile);
                added++;
            }
            return added;
        }

        /// <summary>Returns a name that does not match (case-insensitively) any existing profile.</summary>
        /// <param name="profiles">Existing profiles.</param>
        /// <param name="baseName">Desired name.</param>
        /// <returns><paramref name="baseName"/> if it is free; otherwise <c>baseName (n)</c>.</returns>
        public static string GetUniqueName(IEnumerable<IPProfile> profiles, string baseName)
        {
            var names = new HashSet<string>(profiles.Select(p => p.Name.Trim()), StringComparer.OrdinalIgnoreCase);
            string name = baseName.Trim();
            if (!names.Contains(name)) return name;

            for (int i = 2; ; i++)
            {
                string candidate = $"{name} ({i})";
                if (!names.Contains(candidate)) return candidate;
            }
        }
    }
}
