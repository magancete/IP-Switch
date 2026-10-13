using System.Globalization;
using System.Windows;

namespace IPSwitch.Services
{
    /// <summary>
    /// UI localization with live language switching.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Texts live in <c>Resources/Strings.{language}.xaml</c> as <c>sys:String</c>. Switching the language replaces
    /// that dictionary inside <see cref="Application.Resources"/>, and everything bound with
    /// <c>{DynamicResource Key}</c> updates immediately.
    /// </para>
    /// <para>
    /// What does not support <c>DynamicResource</c> (<c>DataGridColumn</c> headers, texts built in code) must subscribe
    /// to <see cref="LanguageChanged"/>.
    /// </para>
    /// <para>Default language: Spanish if Windows is in Spanish, English otherwise.</para>
    /// </remarks>
    public static class Loc
    {
        /// <summary>Available languages (ISO 639-1 codes).</summary>
        public static readonly string[] SupportedLanguages = { "en", "es" };

        /// <summary>Currently applied language.</summary>
        public static string CurrentLanguage { get; private set; } = "en";

        /// <summary>Raised after the language changes.</summary>
        public static event EventHandler? LanguageChanged;

        /// <summary>Resolves the effective language from the user preference.</summary>
        /// <param name="language">Preferred code, or empty to use the system language.</param>
        /// <returns><c>es</c> if requested or if Windows is in Spanish; <c>en</c> otherwise.</returns>
        public static string Resolve(string? language)
        {
            if (!string.IsNullOrEmpty(language) && SupportedLanguages.Contains(language))
                return language!;

            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en";
        }

        /// <summary>Applies a language to the whole application.</summary>
        /// <param name="language">Language code, or empty to use the system language.</param>
        public static void Apply(string? language)
        {
            string lang = Resolve(language);
            var dictionaries = Application.Current.Resources.MergedDictionaries;

            var previous = dictionaries.FirstOrDefault(d =>
                d.Source?.OriginalString.Contains("/Strings.") == true);

            var next = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Resources/Strings.{lang}.xaml", UriKind.Absolute)
            };

            if (previous != null) dictionaries.Remove(previous);
            dictionaries.Add(next);

            CurrentLanguage = lang;
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(lang);
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Gets the localized text of a key.</summary>
        /// <param name="key">Resource key.</param>
        /// <returns>The text, or the key itself if it does not exist (or there is no WPF application, e.g. in tests).</returns>
        /// <remarks>Must be called on the UI thread.</remarks>
        public static string Get(string key) =>
            Application.Current?.TryFindResource(key) as string ?? key;

        /// <summary>Gets a localized text with <c>{0}</c>, <c>{1}</c>… placeholders and formats it.</summary>
        /// <param name="key">Resource key.</param>
        /// <param name="args">Placeholder values.</param>
        /// <returns>The formatted text.</returns>
        public static string Format(string key, params object[] args) =>
            string.Format(Get(key), args);
    }
}
