using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using MeetingClient.Net;
using MeetingShared;

namespace MeetingClient.Forms
{
    public class LoginForm : Form
    {
        // ===== Network =====
        private readonly ClientNet _net = new();
        private bool _subscribed = false; // chỉ gắn OnMessage 1 lần

        // ===== UI =====
        private readonly TextBox txtHost = new() { Text = "127.0.0.1", Dock = DockStyle.Fill, PlaceholderText = "VD: 192.168.1.10" };
        private readonly NumericUpDown numPort = new() { Minimum = 1, Maximum = 65535, Value = 5555, Dock = DockStyle.Left, Width = 120 };
        private readonly TextBox txtUser = new() { Dock = DockStyle.Fill, PlaceholderText = "Tài khoản" };
        private readonly TextBox txtPass = new() { UseSystemPasswordChar = true, Dock = DockStyle.Fill, PlaceholderText = "Mật khẩu" };
        private readonly CheckBox chkShow = new() { Text = "Hiện mật khẩu", AutoSize = true, Dock = DockStyle.Left };
        private readonly Button btnLogin = new() { Text = "Đăng nhập", Dock = DockStyle.Fill };
        private readonly Button btnReg   = new() { Text = "Đăng ký",   Dock = DockStyle.Fill };
        private readonly Label lblStatus = new() { AutoSize = true, Dock = DockStyle.Fill };
        private readonly ProgressBar prg  = new() { Style = ProgressBarStyle.Marquee, Dock = DockStyle.Fill, Visible = false, MarqueeAnimationSpeed = 40 };
        private readonly ToolTip tip = new() { IsBalloon = true };

        public LoginForm()
        {
            // Đã dùng SafeUi/BeginInvoke khi chạm UI
            CheckForIllegalCrossThreadCalls = false;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Text = "Đăng nhập - OnlineMeeting";
            Width = 480; Height = 320;
            StartPosition = FormStartPosition.CenterScreen;

            // ===== Layout =====
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 8,
                Padding = new Padding(12),
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));

            root.Controls.Add(MkLabel("Server"), 0, 0);
            root.Controls.Add(txtHost,         1, 0);

            root.Controls.Add(MkLabel("Port"), 0, 1);
            var portPanel = new Panel { Dock = DockStyle.Fill, Height = 28 };
            portPanel.Controls.Add(numPort);
            root.Controls.Add(portPanel, 1, 1);

            root.Controls.Add(MkLabel("Tài khoản"), 0, 2);
            root.Controls.Add(txtUser,            1, 2);

            root.Controls.Add(MkLabel("Mật khẩu"), 0, 3);
            var passRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            passRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            passRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            passRow.Controls.Add(txtPass, 0, 0);
            passRow.Controls.Add(chkShow, 1, 0);
            root.Controls.Add(passRow, 1, 3);

            root.Controls.Add(btnLogin, 0, 4);
            root.Controls.Add(btnReg,   1, 4);

            var statusPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            lblStatus.AutoSize = true;
            lblStatus.ForeColor = System.Drawing.Color.DimGray;
            statusPanel.Controls.Add(lblStatus, 0, 0);
            statusPanel.Controls.Add(prg,      1, 0);
            root.Controls.Add(statusPanel, 0, 5);
            root.SetColumnSpan(statusPanel, 2);

            var hint = new Label
            {
                Text = "Gợi ý: nhập IP LAN của máy chạy Server (VD: 192.168.1.5). Port mặc định 5555.",
                AutoSize = true, Dock = DockStyle.Fill, ForeColor = System.Drawing.Color.Gray
            };
            root.Controls.Add(hint, 0, 6);
            root.SetColumnSpan(hint, 2);

            Controls.Add(root);

            // Tooltips
            tip.SetToolTip(txtHost, "Địa chỉ máy chủ trong LAN (IP hoặc hostname).");
            tip.SetToolTip(numPort, "Cổng TCP của server (mặc định 5555).");
            tip.SetToolTip(txtUser, "Tên đăng nhập (không chứa khoảng trắng).");
            tip.SetToolTip(txtPass, "Mật khẩu của bạn.");

            // UX
            AcceptButton = btnLogin;
            txtUser.Focus();
            chkShow.CheckedChanged += (_, __) => txtPass.UseSystemPasswordChar = !chkShow.Checked;

            // Events
            btnLogin.Click += async (_, __) => await DoAuthAsync(false);
            btnReg.Click   += async (_, __) => await DoAuthAsync(true);
            txtPass.KeyDown += async (_, ev) =>
            {
                if (ev.KeyCode == Keys.Enter) { ev.SuppressKeyPress = true; await DoAuthAsync(false); }
            };
        }

        // ===== Helpers =====
        private static Label MkLabel(string text) => new()
        {
            Text = text, AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Dock = DockStyle.Fill
        };

        private void SetBusy(bool busy, string? status = null)
        {
            btnLogin.Enabled = btnReg.Enabled = !busy;
            prg.Visible = busy;
            if (status != null) lblStatus.Text = status;
        }

        private string? ValidateInputs()
        {
            var host = txtHost.Text.Trim();
            var user = txtUser.Text.Trim();
            var pass = txtPass.Text;

            if (string.IsNullOrWhiteSpace(host)) return "Vui lòng nhập địa chỉ server.";
            if (user.Length == 0) return "Vui lòng nhập tài khoản.";
            if (user.Contains(" ")) return "Tài khoản không được chứa khoảng trắng.";
            if (string.IsNullOrEmpty(pass)) return "Vui lòng nhập mật khẩu.";
            if (numPort.Value < 1 || numPort.Value > 65535) return "Port không hợp lệ.";
            return null;
        }

        private async Task EnsureConnectedAsync()
        {
            if (!_net.Tcp.Connected)
            {
                await _net.ConnectAsync(txtHost.Text.Trim(), (int)numPort.Value);
                try { _net.Tcp.NoDelay = true; } catch { /* ignore */ }
            }
            if (!_subscribed)
            {
                _net.OnMessage += Net_OnMessage;
                _subscribed = true;
            }
        }

        private async Task DoAuthAsync(bool isRegister)
        {
            var err = ValidateInputs();
            if (err != null)
            {
                lblStatus.Text = err;
                lblStatus.ForeColor = System.Drawing.Color.Firebrick;
                return;
            }
            lblStatus.ForeColor = System.Drawing.Color.DimGray;

            try
            {
                SetBusy(true, isRegister ? "Đang đăng ký..." : "Đang đăng nhập...");
                await EnsureConnectedAsync();

                var msg = $"{txtUser.Text.Trim()}|{txtPass.Text}";
                await _net.SendAsync(isRegister ? MsgType.Register : MsgType.Login, Packet.Str(msg));
            }
            catch (Exception ex)
            {
                SafeUi(() =>
                {
                    lblStatus.Text = "Lỗi kết nối: " + ex.Message;
                    lblStatus.ForeColor = System.Drawing.Color.Firebrick;
                    SetBusy(false);
                });
            }
        }

        private void Net_OnMessage(MsgType t, byte[] p)
        {
            if (t != MsgType.Info) return;
            var s = Packet.Str(p);

            SafeUi(() =>
            {
                switch (s)
                {
                    case "REGISTER_OK":
                        lblStatus.Text = "Đăng ký thành công, hãy đăng nhập.";
                        lblStatus.ForeColor = System.Drawing.Color.ForestGreen;
                        SetBusy(false);
                        break;

                    case "REGISTER_FAIL":
                        lblStatus.Text = "Tài khoản đã tồn tại.";
                        lblStatus.ForeColor = System.Drawing.Color.Firebrick;
                        SetBusy(false);
                        break;

                    case "LOGIN_OK":
                        lblStatus.Text = "Đăng nhập thành công.";
                        lblStatus.ForeColor = System.Drawing.Color.ForestGreen;
                        OpenMain();
                        break;

                    case "LOGIN_FAIL":
                        lblStatus.Text = "Sai tài khoản/mật khẩu.";
                        lblStatus.ForeColor = System.Drawing.Color.Firebrick;
                        SetBusy(false);
                        break;

                    case "NEED_LOGIN":
                        lblStatus.Text = "Cần đăng nhập trước.";
                        lblStatus.ForeColor = System.Drawing.Color.Firebrick;
                        SetBusy(false);
                        break;

                    default:
                        lblStatus.Text = s;
                        lblStatus.ForeColor = System.Drawing.Color.DimGray;
                        SetBusy(false);
                        break;
                }
            });
        }

        private void OpenMain()
        {
            var main = new MainForm(_net, txtUser.Text.Trim());
            Hide();
            main.FormClosed += (_, __) => Close();
            main.Show();
        }

        // Cập nhật UI an toàn từ bất kỳ thread nào
        private void SafeUi(Action action)
        {
            try
            {
                if (IsDisposed) return;
                if (!IsHandleCreated)
                {
                    // tạo handle nếu chưa có để có thể Invoke
                    var _ = Handle;
                }

                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch { /* ignore UI race */ }
        }
    }
}
