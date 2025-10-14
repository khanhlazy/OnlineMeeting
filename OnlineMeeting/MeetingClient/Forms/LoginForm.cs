using System;
using System.Windows.Forms;
using MeetingClient.Net;
using MeetingShared;

namespace MeetingClient.Forms
{
    public class LoginForm : Form
    {
        private readonly ClientNet _net = new();

        public LoginForm()
        {
            // Có thể để true trong dev để bắt lỗi cross-thread, nhưng ở đây ta đã dùng BeginInvoke
            CheckForIllegalCrossThreadCalls = false;
        }

        private TextBox txtHost = new() { Text = "127.0.0.1" };
        private NumericUpDown numPort = new() { Minimum = 1, Maximum = 65535, Value = 5555 };
        private TextBox txtUser = new();
        private TextBox txtPass = new() { UseSystemPasswordChar = true };
        private Button btnLogin = new() { Text = "Đăng nhập" };
        private Button btnReg = new() { Text = "Đăng ký" };
        private Label lblStatus = new() { AutoSize = true };

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Text = "Đăng nhập - OnlineMeeting";
            Width = 420; Height = 260;

            var tl = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6,
                Padding = new Padding(10)
            };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            tl.Controls.Add(new Label { Text = "Server" }, 0, 0); tl.Controls.Add(txtHost, 1, 0);
            tl.Controls.Add(new Label { Text = "Port" }, 0, 1); tl.Controls.Add(numPort, 1, 1);
            tl.Controls.Add(new Label { Text = "Tài khoản" }, 0, 2); tl.Controls.Add(txtUser, 1, 2);
            tl.Controls.Add(new Label { Text = "Mật khẩu" }, 0, 3); tl.Controls.Add(txtPass, 1, 3);
            tl.Controls.Add(btnLogin, 0, 4); tl.Controls.Add(btnReg, 1, 4);
            tl.Controls.Add(lblStatus, 0, 5);
            Controls.Add(tl);

            btnLogin.Click += async (_, __) => await DoLogin(false);
            btnReg.Click += async (_, __) => await DoLogin(true);
        }

        private async Task EnsureConnected()
        {
            if (!_net.Tcp.Connected)
            {
                await _net.ConnectAsync(txtHost.Text, (int)numPort.Value);
                _net.OnMessage += Net_OnMessage; // đăng ký 1 lần
            }
        }

        private async Task DoLogin(bool register)
        {
            try
            {
                await EnsureConnected();
                var msg = $"{txtUser.Text}|{txtPass.Text}";
                await _net.SendAsync(register ? MsgType.Register : MsgType.Login, Packet.Str(msg));
            }
            catch (Exception ex)
            {
                if (IsHandleCreated)
                    BeginInvoke(new Action(() => lblStatus.Text = "Lỗi kết nối: " + ex.Message));
            }
        }

        private void Net_OnMessage(MsgType t, byte[] p)
        {
            if (t != MsgType.Info) return;
            var s = Packet.Str(p);

            if (IsHandleCreated)
                BeginInvoke(new Action(() =>
                {
                    if (s == "REGISTER_OK") lblStatus.Text = "Đăng ký thành công, hãy đăng nhập.";
                    else if (s == "REGISTER_FAIL") lblStatus.Text = "Tài khoản đã tồn tại.";
                    else if (s == "LOGIN_OK")
                    {
                        var main = new MainForm(_net, txtUser.Text);
                        Hide();
                        main.FormClosed += (_, __) => Close();
                        main.Show();
                    }
                    else if (s == "LOGIN_FAIL") lblStatus.Text = "Sai tài khoản/mật khẩu.";
                    else lblStatus.Text = s;
                }));
        }
    }
}
