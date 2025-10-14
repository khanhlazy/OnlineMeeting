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

        private TextBox txtRoom = new();
        private Button btnCreate = new() { Text = "Tạo phòng" };
        private Button btnJoin = new() { Text = "Tham gia" };
        private Label  lblInfo  = new() { AutoSize = true };

        public MainForm(ClientNet net, string username)
        {
            _net = net;
            _username = username;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Text = $"Xin chào {_username} - Lobby";
            Width = 420; Height = 200;

            var tl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(10) };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            tl.Controls.Add(btnCreate, 0, 0); tl.Controls.Add(new Label(), 1, 0);
            tl.Controls.Add(new Label { Text = "Mã phòng" }, 0, 1); tl.Controls.Add(txtRoom, 1, 1);
            tl.Controls.Add(btnJoin, 0, 2); tl.Controls.Add(lblInfo, 1, 2);
            Controls.Add(tl);

            // Đăng ký handler duy nhất
            _net.OnMessage += Net_OnMessage;

            btnCreate.Click += async (_, __) => await _net.SendAsync(MsgType.CreateRoom, Packet.Str(""));
            btnJoin .Click += async (_, __) => await _net.SendAsync(MsgType.JoinRoom , Packet.Str(txtRoom.Text));
        }

        // CHỈ CÒN MỘT Net_OnMessage — luôn đưa cập nhật về UI thread
        private void Net_OnMessage(MsgType t, byte[] p)
        {
            if (t != MsgType.Info) return;
            var s = Packet.Str(p);

            if (this.IsHandleCreated)
                this.BeginInvoke(new Action(() =>
                {
                    if (s.StartsWith("ROOM_CREATED|"))
                    {
                        var id = s.Split('|')[1];
                        lblInfo.Text = $"Phòng tạo: {id}. Sao chép mã để mời người khác.";
                        OpenMeeting(id, isHost: true);
                    }
                    else if (s.StartsWith("JOIN_OK|"))
                    {
                        var parts = s.Split('|');
                        var id = parts[1];
                        OpenMeeting(id, isHost: false);
                    }
                    else if (s == "ROOM_NOT_FOUND")
                    {
                        lblInfo.Text = "Không tìm thấy phòng.";
                    }
                }));
        }

        private void OpenMeeting(string roomId, bool isHost)
        {
            var frm = new MeetingForm(_net, _username, roomId, isHost);
            frm.Show();
            Hide();
            frm.FormClosed += (_, __) => { Show(); };
        }
    }
}
