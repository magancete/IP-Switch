using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace IPSwitch.Services
{
    /// <summary>
    /// Guarantees that only one IP Switch process runs on the machine and lets later launches talk to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A machine-wide mutex (<c>Global\</c>, shared by every Windows session) marks the running instance.
    /// That instance also listens on a named pipe; any later launch does not act on its own: it sends a command
    /// to the running instance and exits.
    /// </para>
    /// <list type="table">
    /// <listheader><term>Command</term><description>Effect / reply</description></listheader>
    /// <item><term><c>show</c></term><description>Shows the window. Replies <c>0</c>.</description></item>
    /// <item><term><c>apply&lt;TAB&gt;Name</c></term><description>Applies the profile. Replies the exit code (0 / 1 / 2).</description></item>
    /// </list>
    /// <para>So two processes can never change the network configuration at the same time.</para>
    /// </remarks>
    public sealed class SingleInstance : IDisposable
    {
        /// <summary>Machine-wide name of the instance mutex.</summary>
        public const string MutexName = @"Global\IPSwitch.SingleInstance";

        /// <summary>Name of the command pipe.</summary>
        public const string PipeName = "IPSwitch.Commands";

        /// <summary>Command that asks the running instance to show its window.</summary>
        public const string ShowCommand = "show";

        /// <summary>Prefix of the command that asks the running instance to apply a profile.</summary>
        public const string ApplyCommand = "apply";

        private readonly Mutex _mutex;
        private readonly CancellationTokenSource _cancellation = new();

        private SingleInstance(Mutex mutex) => _mutex = mutex;

        /// <summary>
        /// Tries to become the running instance.
        /// </summary>
        /// <returns>The instance owner, or <see langword="null"/> if another process already owns it.</returns>
        public static SingleInstance? TryAcquire()
        {
            var mutex = new Mutex(true, MutexName, out bool createdNew);
            if (createdNew) return new SingleInstance(mutex);

            mutex.Dispose();
            return null;
        }

        /// <summary>
        /// Starts listening for commands from later launches on a background thread.
        /// </summary>
        /// <param name="handler">
        /// Handles a command and returns the reply. It is invoked on a background thread: it must dispatch to the UI
        /// thread itself if needed.
        /// </param>
        public void StartServer(Func<string, Task<string>> handler)
        {
            var token = _cancellation.Token;
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                        await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                        using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true);
                        using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };

                        string? command = await reader.ReadLineAsync().ConfigureAwait(false);
                        string reply = command == null ? string.Empty : await handler(command).ConfigureAwait(false);
                        await writer.WriteLineAsync(reply).ConfigureAwait(false);
                        server.WaitForPipeDrain();
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                    {
                        Debug.WriteLine($"[SingleInstance] Pipe error: {ex.Message}");
                    }
                }
            }, token);
        }

        /// <summary>Sends a command to the running instance and waits for its reply.</summary>
        /// <param name="command">Command line (see the class remarks).</param>
        /// <param name="timeout">Maximum time to connect and get the reply.</param>
        /// <returns>The reply, or <see langword="null"/> if the instance could not be reached in time.</returns>
        public static async Task<string?> SendAsync(string command, TimeSpan timeout)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                client.Connect((int)Math.Min(timeout.TotalMilliseconds, 5000));

                using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);

                await writer.WriteLineAsync(command).ConfigureAwait(false);
                var readTask = reader.ReadLineAsync();
                var finished = await Task.WhenAny(readTask, Task.Delay(timeout)).ConfigureAwait(false);
                return finished == readTask ? await readTask.ConfigureAwait(false) : null;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"[SingleInstance] Could not reach the running instance: {ex.Message}");
                return null;
            }
        }

        /// <summary>Builds the command that applies a profile.</summary>
        /// <param name="profileName">Profile name (tabs and line breaks are removed).</param>
        /// <returns>The command line.</returns>
        public static string BuildApplyCommand(string profileName) =>
            ApplyCommand + "\t" + profileName.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ").Trim();

        /// <summary>Extracts the profile name from an apply command.</summary>
        /// <param name="command">Received command line.</param>
        /// <param name="profileName">Profile name if it is an apply command.</param>
        /// <returns><see langword="true"/> if the command is a well-formed apply command.</returns>
        public static bool TryParseApplyCommand(string command, out string profileName)
        {
            profileName = string.Empty;
            string prefix = ApplyCommand + "\t";
            if (!command.StartsWith(prefix, StringComparison.Ordinal)) return false;

            profileName = command.Substring(prefix.Length).Trim();
            return profileName.Length > 0;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _cancellation.Cancel();
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Released from a different thread or already released: nothing to do
            }
            _mutex.Dispose();
            _cancellation.Dispose();
        }
    }
}
