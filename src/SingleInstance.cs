using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleMark
{
    /// <summary>
    /// Lets a second launch hand its files to the running window. The first instance of a user
    /// session owns a named mutex and listens on a named pipe that only that user can open;
    /// later instances send their file paths (UTF-8, one per line) and exit.
    /// </summary>
    internal sealed class SingleInstance : IDisposable
    {
        private const int MaxMessageBytes = 64 * 1024;
        private const int ConnectTimeoutMs = 2000;
        private const int ReadTimeoutMs = 5000;
        private const int AsfwAny = -1;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly Mutex mutex;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly string sid;
        private Action<string[]> onPaths;

        private SingleInstance(Mutex mutex, string sid)
        {
            this.mutex = mutex;
            this.sid = sid;
        }

        [DllImport("user32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(int processId);

        /// <summary>
        /// Returns the primary-instance handle, or null when another instance already runs for
        /// this user (call <see cref="TrySend"/> then).
        /// </summary>
        public static SingleInstance TryAcquire()
        {
            string sid = UserSid();
            bool createdNew;
            // Only the existence of the mutex matters, so it is never owned.
            var mutex = new Mutex(false, @"Local\SimpleMark.Instance." + sid, out createdNew);
            if (createdNew) return new SingleInstance(mutex, sid);
            mutex.Dispose();
            return null;
        }

        /// <summary>Sends full paths to the primary instance; false if it could not be reached.</summary>
        public static bool TrySend(IEnumerable<string> fullPaths)
        {
            var message = new StringBuilder();
            foreach (string path in fullPaths)
            {
                // Line breaks are the separator and never valid in a path.
                if (path.IndexOfAny(new[] { '\r', '\n' }) < 0) message.Append(path).Append('\n');
            }
            byte[] bytes = Utf8NoBom.GetBytes(message.ToString());
            if (bytes.Length > MaxMessageBytes) return false;

            try
            {
                // Lets the primary bring its window to the foreground; only the launched process may grant it.
                AllowSetForegroundWindow(AsfwAny);
                using (var client = new NamedPipeClientStream(".", PipeName(UserSid()), PipeDirection.Out))
                {
                    client.Connect(ConnectTimeoutMs);
                    client.Write(bytes, 0, bytes.Length);
                    client.Flush();
                }
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Starts listening; <paramref name="handler"/> runs on a background thread for every
        /// message, possibly with no paths (a plain relaunch). Later calls are ignored.
        /// </summary>
        public void Start(Action<string[]> handler)
        {
            if (onPaths != null) return;
            onPaths = handler;
            Task.Run(new Func<Task>(ServeAsync));
        }

        public void Dispose()
        {
            stop.Cancel();
            mutex.Dispose();
        }

        private async Task ServeAsync()
        {
            CancellationToken token = stop.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using (NamedPipeServerStream server = CreateServer())
                    {
                        await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                        string[] paths = await ReadPathsAsync(server, token).ConfigureAwait(false);
                        if (paths != null) onPaths(paths);
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    // The read timeout closed the pipe; wait for the next client.
                }
                catch (IOException)
                {
                    // The client disconnected early, or the pipe could not be created right now.
                    // Pause briefly so a persistent failure cannot spin; this runs on a pool thread.
                    Thread.Sleep(100);
                }
                catch (UnauthorizedAccessException)
                {
                    // Another process owns the pipe name; single-instance handoff is unavailable.
                    return;
                }
            }
        }

        private NamedPipeServerStream CreateServer()
        {
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sid), PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));

            return new NamedPipeServerStream(PipeName(sid), PipeDirection.In, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, MaxMessageBytes, 0, security);
        }

        // Reads until the client closes its end; returns null for oversized or stalled messages.
        private static async Task<string[]> ReadPathsAsync(NamedPipeServerStream server, CancellationToken token)
        {
            var buffer = new byte[MaxMessageBytes + 1];
            int length = 0;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (timeout.Token.Register(server.Dispose))
            {
                timeout.CancelAfter(ReadTimeoutMs);
                while (length < buffer.Length)
                {
                    int read = await server.ReadAsync(buffer, length, buffer.Length - length).ConfigureAwait(false);
                    if (read == 0) break;
                    length += read;
                }
            }
            if (length > MaxMessageBytes) return null;

            var paths = new List<string>();
            foreach (string line in Utf8NoBom.GetString(buffer, 0, length).Split('\n'))
            {
                string path = line.TrimEnd('\r');
                if (path.Length > 0) paths.Add(path);
            }
            return paths.ToArray();
        }

        private static string UserSid()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                return identity.User.Value;
            }
        }

        private static string PipeName(string sid)
        {
            return "SimpleMark.Open." + sid;
        }
    }
}
