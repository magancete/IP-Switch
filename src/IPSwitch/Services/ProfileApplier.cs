using IPSwitch.Models;

namespace IPSwitch.Services
{
    /// <summary>Result of applying a profile with <see cref="ProfileApplier"/>.</summary>
    public enum ApplyOutcome
    {
        /// <summary>The profile was applied and, when required, connectivity was verified.</summary>
        Applied,

        /// <summary>The profile was applied without connectivity, but the user chose to keep it.</summary>
        AppliedWithoutConnectivity,

        /// <summary>There was no connectivity and the previous configuration was restored.</summary>
        RolledBack,

        /// <summary>netsh returned an error; the configuration may be partially applied.</summary>
        Failed,

        /// <summary>The profile is not valid and was not applied.</summary>
        Invalid,

        /// <summary>Another profile was already being applied.</summary>
        Busy
    }

    /// <summary>
    /// Orchestrates applying a profile: snapshot, apply, connectivity check and rollback if the user does not confirm.
    /// </summary>
    /// <remarks>
    /// Flow (when <see cref="AppSettings.VerifyConnectivity"/> is enabled):
    /// <list type="number">
    /// <item><description>The adapter's current configuration is captured.</description></item>
    /// <item><description>The profile is applied.</description></item>
    /// <item><description>The gateway (or the configured host) is pinged for a few seconds.</description></item>
    /// <item><description>If it does not answer, the user is asked whether to keep the changes (with a countdown); otherwise they are rolled back.</description></item>
    /// </list>
    /// Only one apply operation can run at a time.
    /// </remarks>
    public static class ProfileApplier
    {
        /// <summary>Maximum time to wait for connectivity after applying.</summary>
        public static readonly TimeSpan ConnectivityTimeout = TimeSpan.FromSeconds(15);

        private static readonly SemaphoreSlim Gate = new(1, 1);

        /// <summary>Whether an apply operation is in progress.</summary>
        public static bool IsBusy => Gate.CurrentCount == 0;

        /// <summary>Applies a profile following the flow described in the class remarks.</summary>
        /// <param name="profile">Profile to apply.</param>
        /// <param name="settings">Verification settings.</param>
        /// <param name="confirmKeep">
        /// Asks the user whether to keep the changes; receives the countdown in seconds and returns
        /// <see langword="true"/> to keep them. If <see langword="null"/>, connectivity is not verified.
        /// </param>
        /// <returns>The outcome and, on error, the netsh message.</returns>
        public static async Task<(ApplyOutcome Outcome, string Message)> ApplyAsync(
            IPProfile profile, AppSettings settings, Func<int, Task<bool>>? confirmKeep)
        {
            if (!profile.IsValid)
                return (ApplyOutcome.Invalid, profile.ValidationMessage ?? string.Empty);

            if (!await Gate.WaitAsync(0))
                return (ApplyOutcome.Busy, string.Empty);

            try
            {
                bool verify = settings.VerifyConnectivity && confirmKeep != null;
                var snapshot = verify ? await Task.Run(() => NetworkService.CaptureCurrentConfig(profile.Interface)) : null;

                var result = await ProfileService.ApplyAsync(profile);
                if (!result.Success)
                    return (ApplyOutcome.Failed, result.Message);

                if (!verify || snapshot == null)
                    return (ApplyOutcome.Applied, string.Empty);

                if (await NetworkService.WaitForConnectivityAsync(profile, settings.VerifyHost, ConnectivityTimeout))
                    return (ApplyOutcome.Applied, string.Empty);

                if (await confirmKeep!(Math.Max(5, settings.RevertTimeoutSeconds)))
                    return (ApplyOutcome.AppliedWithoutConnectivity, string.Empty);

                var revert = await NetworkService.ApplyProfile(snapshot);
                await ProfileService.DetectActiveProfilesAsync();
                return revert.Success
                    ? (ApplyOutcome.RolledBack, string.Empty)
                    : (ApplyOutcome.Failed, revert.Message);
            }
            finally
            {
                Gate.Release();
            }
        }
    }
}
