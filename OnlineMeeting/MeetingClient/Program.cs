using System;
using System.Windows.Forms;

namespace MeetingClient
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Điểm vào ứng dụng Client: bật VisualStyles và mở màn hình Đăng nhập
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Forms.LoginForm());
        }
    }
}
