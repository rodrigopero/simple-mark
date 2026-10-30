using System;
using System.IO;
using System.Text;

namespace SimpleMark
{
    internal static class SafeFile
    {
        public static byte[] ReadBounded(string path, long maxBytes)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > maxBytes)
                {
                    throw new IOException("The file exceeds the " + (maxBytes / (1024 * 1024)) + " MB limit.");
                }

                var buffer = new byte[stream.Length];
                int read = 0;
                while (read < buffer.Length)
                {
                    int n = stream.Read(buffer, read, buffer.Length - read);
                    if (n == 0) break;
                    read += n;
                }

                if (read == buffer.Length) return buffer;
                var trimmed = new byte[read];
                Buffer.BlockCopy(buffer, 0, trimmed, 0, read);
                return trimmed;
            }
        }

        // Writes to a sibling temp file first so a failure never leaves a truncated document.
        public static void WriteAtomic(string path, string text, Encoding encoding)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            string tempPath = Path.Combine(directory, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                File.WriteAllText(tempPath, text, encoding);
                if (File.Exists(fullPath)) File.Replace(tempPath, fullPath, null);
                else File.Move(tempPath, fullPath);
            }
            catch
            {
                try { File.Delete(tempPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                throw;
            }
        }
    }
}
