using System;
using System.Windows.Forms;

namespace LitchiAutoUpdate
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length == 0)
            {
                MessageBox.Show("Please launch the updater with an update API URL.");
                return;
            }

            Application.Run(new MainForm(args[0]));
        }
    }
}
