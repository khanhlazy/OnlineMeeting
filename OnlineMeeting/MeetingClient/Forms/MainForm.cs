using System;
using System.Windows.Forms;
using MeetingClient.Net;
using MeetingShared;
using MeetingClient.UI;

namespace MeetingClient.Forms
{
    public class MainForm : Form
    {
        private readonly ClientNet _net;
        private readonly string _username;

        // ====== UI ======
        private readonly ToolStrip _toolbar = new()
        {
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.System
        };
        private readonly ToolStripLabel _lblHello = new();
        private readonly ToolStripSeparator _sep1 = new();
        private readonly ToolStripButton _btnCreate = new() { Text = "Tạo phòng", DisplayStyle = ToolStripItemDisplayStyle.Text };
        private readonly ToolStripButton _btnJoin = new()   { Text = "Tham gia",  DisplayStyle = ToolStripItemDisplayStyle.Text };
        private readonly ToolStripButton _btnCopy = new()   { Text = "Sao chép mã", DisplayStyle = ToolStripItemDisplayStyle.Text };

        private readonly StatusStrip _status = new();
        private readonly ToolStripStatusLabel _lblStatus = new() { Text = "Sẵn sàng" };

        private readonly TextBox txtRoom = new() { PlaceholderText = "Nhập mã phòng (ví dụ R123456)..." };

        public MainForm(ClientNet net, string username)
        {
            _net = net;
            _username = username;
        }

        // Màn hình Lobby: tạo phòng, nhập mã tham gia phòng; theo dõi thông báo từ server
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Text = $"Xin chào {_username} - Lobby";
            Width = 640; Height = 360;
            StartPosition = FormStartPosition.CenterScreen;

            // ===== Toolbar =====
            _lblHello.Text = $"Người dùng: {_username}";
            _toolbar.Items.Add(_lblHello);
            _toolbar.Items.Add(_sep1);
            Controls.Add(_toolbar);

            // ===== Content =====
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(16)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); // title
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); // subtitle
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); // join row
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // create button area

            var title = new Label
            {
                Text = "Online Meeting",
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Font = new System.Drawing.Font("Segoe UI", 16, System.Drawing.FontStyle.Bold)
            };
            var subtitle = new Label
            {
                Text = "Nhập mã để tham gia hoặc tạo phòng mới",
                Dock = DockStyle.Fill,
                ForeColor = System.Drawing.Color.DimGray,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };

            var joinRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4
            };
            joinRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); // label
            joinRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // textbox
            joinRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); // join
            joinRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); // copy

            var lblRoom = new Label { Text = "Mã phòng", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            txtRoom.Dock = DockStyle.Fill;

            _btnJoin.DisplayStyle = ToolStripItemDisplayStyle.Text;
            _btnCopy.DisplayStyle = ToolStripItemDisplayStyle.Text;

            var btnJoin = new Button { Text = "Tham gia", Dock = DockStyle.Fill };
            var btnCopy = new Button { Text = "Sao chép mã", Dock = DockStyle.Fill };

            btnJoin.Click += async (_, __) => await JoinRoomAsync();
            btnCopy.Click += (_, __) => { try { if (!string.IsNullOrWhiteSpace(txtRoom.Text)) Clipboard.SetText(txtRoom.Text.Trim()); _lblStatus.Text = "Đã sao chép mã phòng."; } catch { } };

            joinRow.Controls.Add(lblRoom, 0, 0);
            joinRow.Controls.Add(txtRoom, 1, 0);
            joinRow.Controls.Add(btnJoin, 2, 0);
            joinRow.Controls.Add(btnCopy, 3, 0);

            var createPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 0) };
            var btnCreateBig = new Button { Text = "Tạo phòng mới", Dock = DockStyle.Top, Height = 44 };
            btnCreateBig.Click += async (_, __) => await CreateRoomAsync();
            createPanel.Controls.Add(btnCreateBig);

            root.Controls.Add(title, 0, 0);
            root.Controls.Add(subtitle, 0, 1);
            root.Controls.Add(joinRow, 0, 2);
            root.Controls.Add(createPanel, 0, 3);

            Controls.Add(root);

            // ===== Status bar =====
            _status.Items.Add(_lblStatus);
            _status.Dock = DockStyle.Bottom;
            Controls.Add(_status);

            // Theme
            Theme.Apply(this);
            Theme.StylePrimary(btnCreateBig);
            Theme.StyleSecondary(btnJoin);
            Theme.StyleSecondary(btnCopy);

            // ===== Events ===== (gửi yêu cầu tạo/tham gia phòng; lắng nghe Info từ server)
            // Đăng ký handler — chỉ nhận Info cần cho lobby
            _net.OnMessage += Net_OnMessage;

            _btnCreate.Click += async (_, __) => await CreateRoomAsync();
            _btnJoin  .Click += async (_, __) => await JoinRoomAsync();
            _btnCopy  .Click += (_, __)      => { try { if (!string.IsNullOrWhiteSpace(txtRoom.Text)) Clipboard.SetText(txtRoom.Text.Trim()); _lblStatus.Text = "Đã sao chép mã phòng."; } catch { } };

            txtRoom.KeyDown += async (_, ev) =>
            {
                if (ev.KeyCode == Keys.Enter)
                {
                    ev.SuppressKeyPress = true;
                    await JoinRoomAsync();
                }
            };

            // UX: enable/disable join/copy by input
            txtRoom.TextChanged += (_, __) =>
            {
                var has = !string.IsNullOrWhiteSpace(txtRoom.Text);
                btnJoin.Enabled = has;
                btnCopy.Enabled = has;
            };
        }

        // ===== Actions =====
        // Gửi yêu cầu tạo phòng (MsgType.CreateRoom). Server trả "ROOM_CREATED|<id>"
        private async System.Threading.Tasks.Task CreateRoomAsync()
        {
            ToggleBusy(true, "Đang tạo phòng...");
            try
            {
                await _net.SendAsync(MsgType.CreateRoom, Packet.Str(""));
            }
            catch (Exception ex)
            {
                SafeUi(() =>
                {
                    _lblStatus.Text = "Lỗi: " + ex.Message;
                    ToggleBusy(false);
                });
            }
        }

        // Gửi yêu cầu tham gia phòng (MsgType.JoinRoom) với mã phòng
        private async System.Threading.Tasks.Task JoinRoomAsync()
        {
            var code = txtRoom.Text.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                _lblStatus.Text = "Vui lòng nhập mã phòng.";
                return;
            }
            ToggleBusy(true, "Đang tham gia phòng...");
            try
            {
                await _net.SendAsync(MsgType.JoinRoom, Packet.Str(code));
            }
            catch (Exception ex)
            {
                SafeUi(() =>
                {
                    _lblStatus.Text = "Lỗi: " + ex.Message;
                    ToggleBusy(false);
                });
            }
        }

        // ===== Network events (chỉ xử lý Info cần thiết) =====
        // Xử lý phản hồi: ROOM_CREATED, JOIN_OK, ROOM_NOT_FOUND, NEED_LOGIN
        private void Net_OnMessage(MsgType t, byte[] p)
        {
            if (t != MsgType.Info) return;
            var s = Packet.Str(p);

            SafeUi(() =>
            {
                if (s.StartsWith("ROOM_CREATED|"))
                {
                    var id = s.Split('|')[1];
                    txtRoom.Text = id;
                    _lblStatus.Text = $"Phòng tạo: {id}. Bạn có thể sao chép mã để mời người khác.";
                    ToggleBusy(false);
                    OpenMeeting(id, isHost: true);
                }
                else if (s.StartsWith("JOIN_OK|"))
                {
                    var parts = s.Split('|');
                    var id = parts[1];
                    _lblStatus.Text = $"Tham gia phòng {id} thành công.";
                    ToggleBusy(false);
                    OpenMeeting(id, isHost: false);
                }
                else if (s == "ROOM_NOT_FOUND")
                {
                    _lblStatus.Text = "Không tìm thấy phòng.";
                    ToggleBusy(false);
                }
                else if (s == "NEED_LOGIN")
                {
                    _lblStatus.Text = "Cần đăng nhập trước.";
                    ToggleBusy(false);
                }
            });
        }

        private void OpenMeeting(string roomId, bool isHost)
        {
            var frm = new MeetingForm(_net, _username, roomId, isHost);
            frm.Show();
            Hide();

            // Khi phòng đóng, quay về lobby
            frm.FormClosed += (_, __) =>
            {
                Show();
                Activate();
            };
        }

        // ===== Helpers =====
        private void ToggleBusy(bool busy, string? status = null)
        {
            _btnCreate.Enabled = _btnJoin.Enabled = !busy;
            if (status != null) _lblStatus.Text = status;
        }

        // Cập nhật UI an toàn từ mọi thread
        private void SafeUi(Action ui)
        {
            try
            {
                if (IsDisposed) return;
                if (!IsHandleCreated) { var _ = Handle; }
                if (InvokeRequired) BeginInvoke(ui);
                else ui();
            }
            catch { /* ignore UI race */ }
        }
    }
}
