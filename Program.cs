using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace FileNameNormalizer
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main()
        {
            bool firstInstance;
            using (Mutex instance = new Mutex(true, "Local\\FileNameNormalizer-v1", out firstInstance))
            {
            if (!firstInstance)
            {
                MessageBox.Show("파일명 정리가 이미 실행 중입니다. 열려 있는 창을 사용해 주세요.", "파일명 정리");
                return;
            }
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                MessageBox.Show("작업 중 오류가 발생했습니다.\n\n" + e.Exception.Message,
                    "파일명 정리", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.Run(new MainForm());
            }
        }
    }
}
