using System;
using System.IO;
using System.Windows.Forms;

namespace SimpleMark
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string[] paths = ToFullPaths(args);
            using (SingleInstance instance = SingleInstance.TryAcquire())
            {
                // Another window is running: hand the files over, or run standalone if it does not answer.
                if (instance == null && SingleInstance.TrySend(paths)) return;

                var form = new MainForm(paths);
                if (instance != null)
                {
                    form.HandleCreated += delegate { instance.Start(form.OpenFromAnotherInstance); };
                }
                Application.Run(form);
            }
        }

        // Relative paths must be resolved here: the receiving instance has a different working directory.
        private static string[] ToFullPaths(string[] args)
        {
            var paths = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                try
                {
                    paths[i] = Path.GetFullPath(args[i]);
                }
                catch (Exception ex)
                {
                    // Keep the argument so opening it reports the error.
                    if (!DocumentTab.IsFileError(ex)) throw;
                    paths[i] = args[i];
                }
            }
            return paths;
        }
    }
}
