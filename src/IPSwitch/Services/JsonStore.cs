using System.Diagnostics;
using System.IO;
using System.Web.Script.Serialization;

namespace IPSwitch.Services
{
    /// <summary>
    /// Minimal JSON persistence based on <see cref="JavaScriptSerializer"/>, included in .NET Framework,
    /// to avoid extra dependencies (System.Text.Json would increase the executable size).
    /// </summary>
    /// <remarks>
    /// Serializes public properties with a getter and a setter; those marked with <see cref="ScriptIgnoreAttribute"/>
    /// are skipped. Writes are atomic: data goes to a temporary file that then replaces the target.
    /// </remarks>
    public static class JsonStore
    {
        private static readonly JavaScriptSerializer Serializer = new();

        /// <summary>Reads and deserializes a JSON file.</summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="path">File path.</param>
        /// <returns>The object read, or <see langword="null"/> if the file does not exist or is corrupt.</returns>
        public static T? Load<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                return Serializer.Deserialize<T>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JsonStore] Load failed for {path}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Serializes and saves an object to a JSON file, creating the folder if needed.</summary>
        /// <param name="path">File path.</param>
        /// <param name="value">Object to save.</param>
        /// <remarks>I/O errors are written to the debug output and not propagated.</remarks>
        public static void Save(string path, object value)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temp = path + ".tmp";
                File.WriteAllText(temp, Serializer.Serialize(value));

                if (File.Exists(path))
                    File.Replace(temp, path, null);
                else
                    File.Move(temp, path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JsonStore] Save failed for {path}: {ex.Message}");
            }
        }
    }
}
