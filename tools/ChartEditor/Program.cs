using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace InvectorModKit.ChartEditor.Host
{
    internal static class Program
    {
        /// <summary>
        /// Starts the editor, with the song given on the command line (the game's "Level editor" button passes the
        /// selected song's audio). When an editor window is already open, it gets the song instead and this start ends.
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            string song = EditorInstance.SongArgument(args);
            using (var instance = EditorInstance.TryClaim(EditorInstance.DefaultName))
            {
                if (instance == null)
                {
                    AllowSetForegroundWindow(AnyProcess); // this start may have the focus: the open window takes it
                    EditorInstance.Send(EditorInstance.DefaultName, song ?? "", 5000);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(song, instance));
            }
        }

        private const int AnyProcess = -1; // ASFW_ANY

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);
    }
}
