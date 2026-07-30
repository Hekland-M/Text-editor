using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace WritingApp
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string initialPath = args.Length > 0 ? args[0] : null;
            Application.Run(new WritingAppApplicationContext(initialPath));
        }
    }

    internal sealed class WritingAppApplicationContext : ApplicationContext
    {
        private int openWindowCount;

        public WritingAppApplicationContext(string initialPath)
        {
            if (!String.IsNullOrEmpty(initialPath))
            {
                OpenWindow(initialPath, null);
                return;
            }

            bool openedRecovery = false;
            foreach (RecoverySnapshot recovery in RecoveryManager.LoadAll())
            {
                string documentName = String.IsNullOrEmpty(recovery.OriginalPath)
                    ? "et dokument uten filnavn"
                    : Path.GetFileName(recovery.OriginalPath);
                DialogResult answer = MessageBox.Show(
                    "WritingApp fant en automatisk sikkerhetskopi av " +
                    documentName + ".\r\n\r\nVil du gjenopprette dokumentet?",
                    "Gjenopprett dokument",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (answer == DialogResult.Yes)
                {
                    OpenWindow(null, recovery);
                    openedRecovery = true;
                }
                else
                {
                    RecoveryManager.Delete(recovery.RecoveryPath);
                }
            }

            if (!openedRecovery)
                OpenWindow(null, null);
        }

        private void OpenWindow(string initialPath, RecoverySnapshot recovery)
        {
            var window = new MainForm(
                initialPath,
                recovery,
                delegate { OpenWindow(null, null); });
            openWindowCount++;
            window.FormClosed += delegate
            {
                openWindowCount--;
                if (openWindowCount == 0)
                    ExitThread();
            };
            window.Show();
        }
    }
}
