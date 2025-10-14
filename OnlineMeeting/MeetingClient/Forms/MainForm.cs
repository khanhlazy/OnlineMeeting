using System;
using System.Windows.Forms;
using MeetingClient.Net;
using MeetingShared;

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

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Text = $"Xin chào {_username} - Lobby";
            Width = 520; Height = 240;
            StartPosition = FormStartPosition.CenterScreen;

            // ===== Toolbar =====
            _lblHello.Text = $"Người dùng: {_username}";
            _toolbar.Items.Add(_lblHello);
            _toolbar.Items.Add(_sep1);
            _toolbar.Items.Add(_btnCreate);
            _toolbar.Items.Add(_btnJoin);
            _toolbar.Items.Add(_btnCopy);
            Controls.Add(_toolbar);

            // ===== Content =====
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(12)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));

            panel.Controls.Add(new Label { Text = "Mã phòng", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
            panel.Controls.Add(txtRoom, 1, 0);

            Controls.Add(panel);

            // ===== Status bar =====
            _status.Items.Add(_lblStatus);
            _status.Dock = DockStyle.Bottom;
            Controls.Add(_status);

            // ===== Events =====
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
        }

        // ===== Actions =====
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
