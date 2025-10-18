using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Threading.Tasks;
using MeetingClient.Net;
using MeetingShared;
using MeetingClient.UI;
using NAudio.Wave;

// OpenCvSharp
using OpenCvSharp;
using OpenCvSharp.Extensions;

// Alias để hết mơ hồ Size
using SD = System.Drawing;
using DSize = System.Drawing.Size;
using CvSize = OpenCvSharp.Size;

namespace MeetingClient.Forms
{
    public class MeetingForm : Form
    {
        private readonly ClientNet _net;
        private readonly string _username;
        private readonly string _roomId;
        private readonly bool _isHost;

        // ========== UI thành phần ==========
        private readonly ToolStrip _toolbar = new()
        {
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.System
        };
        private readonly ToolStripLabel _lblRoom = new();
        private readonly ToolStripButton _btnCopyRoom = new() { Text = "Sao chép mã", DisplayStyle = ToolStripItemDisplayStyle.Text };
        private readonly ToolStripSeparator _sep1 = new();
        private readonly ToolStripButton _btnCam = new() { Text = "Camera: Tắt" };
        private readonly ToolStripButton _btnMic = new() { Text = "Mic: Tắt" };
        private readonly ToolStripSeparator _sep2 = new();
        private readonly ToolStripButton _btnLeave = new() { Text = "Rời phòng" };
        private readonly ToolStripComboBox _cmbCam = new() { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 200 };
        private readonly ToolStripComboBox _cmbMic = new() { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 220 };
        private readonly ToolStripDropDownButton _ddVideoSrc = new() { Text = "Nguồn video" };
        private readonly ToolStripDropDownButton _ddMicSrc = new() { Text = "Nguồn mic" };
        private readonly ToolStripMenuItem _miVideoReal = new("Camera thật") { Checked = true, CheckOnClick = true };
        private readonly ToolStripMenuItem _miVideoDemo = new("Demo (mẫu)") { CheckOnClick = true };
        private readonly ToolStripMenuItem _miMicReal = new("Micro thật") { Checked = true, CheckOnClick = true };
        private readonly ToolStripMenuItem _miMicDemo = new("Demo (tone)") { CheckOnClick = true };

        private readonly StatusStrip _status = new();
        private readonly ToolStripStatusLabel _lblNet = new() { Text = "Sẵn sàng" };

        // Cột trái: Preview + Users
        private readonly PictureBox _picLocal = new()
        {
            Height = 170,
            Dock = DockStyle.Top,
            BorderStyle = BorderStyle.FixedSingle,
            SizeMode = PictureBoxSizeMode.Zoom
        };
        private readonly Label _lbLocal = new() { Dock = DockStyle.Top, Height = 18, TextAlign = ContentAlignment.MiddleCenter };
        private readonly ListBox _lstUsers = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
        private readonly Button _btnKick = new() { Text = "Kick (Host)", Dock = DockStyle.Bottom, Height = 32, Enabled = false };

        // Trung tâm: Video grid (tự co giãn)
        private readonly TableLayoutPanel _videoGrid = new()
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 2,
            BackColor = Theme.Palette.Background,
            Padding = new Padding(8)
        };

        // Phải: Chat
        private readonly RichTextBox _rtbChat = new()
        {
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle
        };
        private readonly TextBox _txtChat = new() { PlaceholderText = "Nhập tin nhắn và Enter...", Dock = DockStyle.Fill, Height = 26 };
        private readonly Button _btnSend = new() { Text = "Gửi", Dock = DockStyle.Fill, Height = 28 };

        // Menu chuột phải trên danh sách user (tiện kick)
        private readonly ContextMenuStrip _userMenu = new();
        private readonly ToolStripMenuItem _miKick = new("Kick người này");

        // ========== Thiết bị ==========
        private OpenCvSharp.VideoCapture? _cvCap;
        private bool _camOn = false;
        private int _selectedCamIndex = -1;
        private long _lastVideoSentMs = 0; // throttle gửi video
        private System.Windows.Forms.Timer? _videoTimer;
        private int _demoFrameTick = 0;

        private WaveInEvent? _mic;
        private BufferedWaveProvider? _audioBuffer;
        private WaveOutEvent? _speaker;
        private bool _micOn = false;
        private int _selectedMicIndex = -1;
        private System.Threading.CancellationTokenSource? _micDemoCts;

        private enum VideoInputMode { Real, Demo }
        private enum MicInputMode { Real, Demo }
        private VideoInputMode _videoMode = VideoInputMode.Real;
        private MicInputMode _micMode = MicInputMode.Real;

        // Multi-video: quản lý tile theo username (giữ cả cell và PictureBox)
        private readonly Dictionary<string, (Panel cell, PictureBox pb)> _tiles = new();

        // ======= chống crash khi mất kết nối =======
        private bool _shownDisconnect = false;

        // Reflow cấu hình
        private const int MinTileWidth = 320; // bề rộng tối thiểu 1 tile (tuỳ chỉnh)
        private const int MaxCols = 4;        // tối đa số cột (3–4 tuỳ ý)

        public MeetingForm(ClientNet net, string username, string roomId, bool isHost)
        {
            _net = net; _username = username; _roomId = roomId; _isHost = isHost;

            // lưới video khởi tạo
            _videoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            _videoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            _videoGrid.RowCount = 1;
            _videoGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Reflow khi đổi kích thước
            _videoGrid.SizeChanged += (_, __) => ReflowTiles();
        }

        // ========== Helper xử lý lỗi mạng ==========
        private void HandleNetException(Exception ex)
        {
            if (_shownDisconnect) return;
            _shownDisconnect = true;

            _lblNet.Text = "Mất kết nối tới server.";
            try { _videoTimer?.Stop(); } catch { }
            _camOn = false; _btnCam.Text = "Camera: Tắt"; StopCamera();
            _micOn = false; _btnMic.Text = "Mic: Tắt"; StopMic();

            // Nếu muốn tự đóng form khi rớt mạng, bỏ comment dòng dưới:
            // BeginInvoke(new Action(() => Close()));
        }

        private async Task<bool> SafeSendAsync(MsgType type, byte[] payload)
        {
            try
            {
                await _net.SendAsync(type, payload);
                return true;
            }
            catch (Exception ex)
            {
                HandleNetException(ex);
                return false;
            }
        }

        // Màn hình phòng họp: quản lý video/mic, chat, danh sách người tham gia
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Text = $"Phòng {_roomId} - Người dùng: {_username}{(_isHost ? " (Host)" : "")}";
            Width = 1280; Height = 780;

            // ===== Toolbar =====
            _lblRoom.Text = $"Phòng: {_roomId}";
            _ddVideoSrc.DropDownItems.AddRange(new ToolStripItem[] { _miVideoReal, _miVideoDemo });
            _ddMicSrc.DropDownItems.AddRange(new ToolStripItem[] { _miMicReal, _miMicDemo });
            _toolbar.Items.AddRange(new ToolStripItem[]
            {
                _lblRoom, _btnCopyRoom, _sep1,
                new ToolStripLabel("Camera:"), _cmbCam, _ddVideoSrc, _btnCam,
                new ToolStripSeparator(),
                new ToolStripLabel("Mic:"), _cmbMic, _ddMicSrc, _btnMic,
                _sep2, _btnLeave
            });
            _toolbar.Dock = DockStyle.Top;
            Controls.Add(_toolbar);

            // ===== Status bar =====
            _status.Items.Add(_lblNet);
            _status.Dock = DockStyle.Bottom;
            Controls.Add(_status);

            // Apply theme and emphasize actions
            Theme.Apply(this);
            Theme.StyleSecondary(_btnCopyRoom);
            Theme.StyleSecondary(_btnCam);
            Theme.StyleSecondary(_btnMic);
            Theme.StyleDanger(_btnLeave);
            Theme.StyleDanger(_btnKick);

            // ===== Context menu danh sách user =====
            _userMenu.Items.Add(_miKick);
            _lstUsers.ContextMenuStrip = _userMenu;
            _miKick.Enabled = _isHost;
            _btnKick.Enabled = _isHost;

            // ===================== BỐ CỤC 3 CỘT =====================
            // Tuỳ chỉnh nhanh hai thông số:
            int chatRightWidth = 320; // rộng cột chat (phải)
            int previewHeight = 210;  // cao khung preview (trái, trên)

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Theme.Palette.Background
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260)); // trái: preview + users
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));  // giữa: video grid
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, chatRightWidth)); // phải: chat
            Controls.Add(content);

            // ===== CỘT TRÁI =====
            var leftCol = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(8)
            };
            leftCol.RowStyles.Add(new RowStyle(SizeType.Absolute, previewHeight)); // Preview
            leftCol.RowStyles.Add(new RowStyle(SizeType.Percent, 100));            // Users
            leftCol.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));            // Nút kick

            // Group Preview
            var gbPreview = new GroupBox
            {
                Text = "Video của bạn",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };
            var previewWrap = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
            previewWrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            previewWrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _lbLocal.Text = $"({_username})";
            _lbLocal.Dock = DockStyle.Top;
            _picLocal.Dock = DockStyle.Fill;
            gbPreview.Controls.Add(previewWrap);
            previewWrap.Controls.Add(_lbLocal, 0, 0);
            previewWrap.Controls.Add(_picLocal, 0, 1);

            // Group Users
            var gbUsers = new GroupBox
            {
                Text = "Người tham gia",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };
            _lstUsers.Dock = DockStyle.Fill;
            gbUsers.Controls.Add(_lstUsers);

            _btnKick.Dock = DockStyle.Fill;

            leftCol.Controls.Add(gbPreview, 0, 0);
            leftCol.Controls.Add(gbUsers, 0, 1);
            leftCol.Controls.Add(_btnKick, 0, 2);

            content.Controls.Add(leftCol, 0, 0);

            // ===== CỘT GIỮA: VIDEO GRID =====
            var gbVideo = new GroupBox
            {
                Text = "Màn hình",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };
            _videoGrid.Dock = DockStyle.Fill;
            gbVideo.Controls.Add(_videoGrid);
            content.Controls.Add(gbVideo, 1, 0);

            // ===== CỘT PHẢI: CHAT =====
            var gbChat = new GroupBox
            {
                Text = "Chat",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };
            var chatLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            chatLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            chatLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            _rtbChat.Dock = DockStyle.Fill;
            chatLayout.Controls.Add(_rtbChat, 0, 0);

            var sendRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            sendRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sendRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            _txtChat.Dock = DockStyle.Fill;
            _btnSend.Dock = DockStyle.Fill;
            sendRow.Controls.Add(_txtChat, 0, 0);
            sendRow.Controls.Add(_btnSend, 1, 0);

            chatLayout.Controls.Add(sendRow, 0, 1);
            gbChat.Controls.Add(chatLayout);
            content.Controls.Add(gbChat, 2, 0);

            // ===== Sự kiện =====
            _btnCopyRoom.Click += (_, __) => { try { Clipboard.SetText(_roomId); _lblNet.Text = "Đã sao chép mã phòng"; } catch { } };
            _btnLeave.Click += (_, __) => Close();

            _cmbCam.DropDown += (_, __) => RefreshCameras();
            _cmbMic.DropDown += (_, __) => RefreshMics();
            _cmbCam.SelectedIndexChanged += (_, __) => { if (_camOn) { StopCamera(); StartCamera(); } };
            _cmbMic.SelectedIndexChanged += (_, __) => { if (_micOn) { StopMic(); StartMic(); } };

            _miVideoReal.Click += (_, __) => { _videoMode = VideoInputMode.Real; _miVideoReal.Checked = true; _miVideoDemo.Checked = false; if (_camOn) { StopCamera(); StartCamera(); } };
            _miVideoDemo.Click += (_, __) => { _videoMode = VideoInputMode.Demo; _miVideoReal.Checked = false; _miVideoDemo.Checked = true; if (_camOn) { StopCamera(); StartCamera(); } };
            _miMicReal.Click += (_, __) => { _micMode = MicInputMode.Real; _miMicReal.Checked = true; _miMicDemo.Checked = false; if (_micOn) { StopMic(); StartMic(); } };
            _miMicDemo.Click += (_, __) => { _micMode = MicInputMode.Demo; _miMicReal.Checked = false; _miMicDemo.Checked = true; if (_micOn) { StopMic(); StartMic(); } };

            _btnCam.Click += async (_, __) =>
            {
                _camOn = !_camOn;
                _btnCam.Text = _camOn ? "Camera: Bật" : "Camera: Tắt";
                if (!await SafeSendAsync(MsgType.ToggleCam, Packet.Str(_camOn ? "ON" : "OFF")))
                {
                    _camOn = false; _btnCam.Text = "Camera: Tắt"; return;
                }
                if (_camOn) StartCamera(); else StopCamera();
            };

            _btnMic.Click += async (_, __) =>
            {
                _micOn = !_micOn;
                _btnMic.Text = _micOn ? "Mic: Bật" : "Mic: Tắt";
                if (!await SafeSendAsync(MsgType.ToggleMic, Packet.Str(_micOn ? "ON" : "OFF")))
                {
                    _micOn = false; _btnMic.Text = "Mic: Tắt"; return;
                }
                if (_micOn) StartMic(); else StopMic();
            };

            _btnKick.Click += async (_, __) => await KickSelectedAsync();
            _miKick.Click += async (_, __) => await KickSelectedAsync();

            _btnSend.Click += async (_, __) => await SendChatAsync();
            _txtChat.KeyDown += async (_, ev) => { if (ev.KeyCode == Keys.Enter) { ev.SuppressKeyPress = true; await SendChatAsync(); } };

            // đăng ký sự kiện mạng
            _net.OnMessage += Net_OnMessage;

            // nhãn
            _lblNet.Text = "Đã vào phòng.";

            // Nạp danh sách thiết bị lần đầu
            RefreshCameras();
            RefreshMics();
        }

        protected override async void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            try { await SafeSendAsync(MsgType.Leave, Packet.Str("")); } catch { }
            StopCamera(); StopMic();
        }

        // ================== Chat ==================
        private async Task SendChatAsync()
        {
            var raw = _txtChat.Text.Trim();
            if (raw.Length == 0) return;

            if (!await SafeSendAsync(MsgType.Chat, Packet.Str(raw))) return;

            _rtbChat.AppendText($"(Bạn) {raw}{Environment.NewLine}");
            _rtbChat.SelectionStart = _rtbChat.TextLength;
            _rtbChat.ScrollToCaret();

            _txtChat.Clear();
        }

        // ======= Kick UX =======
        private async Task KickSelectedAsync()
        {
            if (!_isHost)
            {
                MessageBox.Show("Chỉ Host mới có quyền kick.");
                return;
            }

            if (_lstUsers.SelectedItem is not string line)
            {
                MessageBox.Show("Hãy chọn một người trong danh sách.");
                return;
            }

            if (line.Contains("(Host)"))
            {
                MessageBox.Show("Không thể kick Host.");
                return;
            }

            var target = line.Split(' ')[0];

            var ok = MessageBox.Show(
                $"Kick '{target}' khỏi phòng?",
                "Xác nhận",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (ok == DialogResult.Yes)
            {
                await SafeSendAsync(MsgType.Kick, Packet.Str(target));
            }
        }

        // ============ Nhận dữ liệu từ server ============
        private void Net_OnMessage(MsgType t, byte[] p)
        {
            if (!IsHandleCreated) return;

            switch (t)
            {
                case MsgType.Chat:
                {
                    var s = Packet.Str(p);
                    BeginInvoke(new Action(() =>
                    {
                        _rtbChat.AppendText(s + Environment.NewLine);
                        _rtbChat.SelectionStart = _rtbChat.TextLength;
                        _rtbChat.ScrollToCaret();
                    }));
                    break;
                }

                case MsgType.Participants:
                {
                    var s = Packet.Str(p);
                    BeginInvoke(new Action(() =>
                    {
                        var currentUsers = new HashSet<string>();
                        _lstUsers.Items.Clear();
                        foreach (var item in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var parts = item.Split(':');
                            var name = parts[0];
                            var isHost = parts[1] == "1";
                            var cam = parts[2] == "1";
                            var mic = parts[3] == "1";

                            _lstUsers.Items.Add(
                                name + (isHost ? " (Host)" : "") +
                                $"  [Cam:{(cam ? "On" : "Off")}  Mic:{(mic ? "On" : "Off")}]"
                            );
                            currentUsers.Add(name);

                            // tạo tile cho user khác mình
                            if (name != _username) EnsureTile(name);
                        }

                        // Remove tiles for users who left
                        var leftUsers = _tiles.Keys.Where(u => !currentUsers.Contains(u)).ToList();
                        foreach (var user in leftUsers)
                        {
                            var cell = _tiles[user].cell;
                            _videoGrid.Controls.Remove(cell);
                            _tiles.Remove(user);
                        }

                        ReflowTiles();
                    }));
                    break;
                }

                case MsgType.Info:
                {
                    var s = Packet.Str(p);
                    if (s == "KICKED")
                        BeginInvoke(new Action(() =>
                        {
                            MessageBox.Show("Bạn đã bị host đưa ra khỏi phòng.");
                            Close();
                        }));
                    else if (s == "DISCONNECTED")
                        BeginInvoke(new Action(() =>
                        {
                            _lblNet.Text = "Mất kết nối tới server.";
                            MessageBox.Show("Mất kết nối tới server.");
                            Close();
                        }));
                    break;
                }

                case MsgType.Video:
                {
                    // Payload: "<username>|<jpeg bytes>"
                    if (!TrySplitUserPayload(p, out var user, out var bytes) || user == _username) break; // Skip local video
                    try
                    {
                        using var ms = new MemoryStream(bytes);
                        var img = Image.FromStream(ms);
                        BeginInvoke(new Action(() => ShowRemoteFrame(user, img)));
                    }
                    catch { }
                    break;
                }

                case MsgType.Audio:
                {
                    // Audio trộn chung (đơn giản)
                    if (_audioBuffer == null)
                    {
                        _audioBuffer = new BufferedWaveProvider(new WaveFormat(16000, 1))
                        {
                            DiscardOnBufferOverflow = true,
                            BufferDuration = TimeSpan.FromSeconds(2)
                        };
                        _speaker = new WaveOutEvent();
                        _speaker.Init(_audioBuffer);
                        _speaker.Play();
                    }
                    _audioBuffer.AddSamples(p, 0, p.Length);
                    break;
                }
            }
        }

        // ============ Video grid (multi user) ============
        private void ShowRemoteFrame(string user, Image img)
        {
            if (user == _username) return; // Skip displaying local video in grid
            var pb = EnsureTile(user);     // tạo nếu chưa có (video có thể tới trước Participants)
            var old = pb.Image;
            pb.Image = (Image)img.Clone();
            old?.Dispose();
        }

        private PictureBox EnsureTile(string user)
        {
            if (_tiles.TryGetValue(user, out var t))
                return t.pb;

            // PictureBox hiển thị
            var pb = new PictureBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                MinimumSize = new DSize(240, 160)
            };

            // Header tên
            var cap = new Label
            {
                Text = user,
                Dock = DockStyle.Top,
                Height = 22,
                ForeColor = Color.White, // Fallback
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(6, 3, 6, 3)
            };

            // Cell panel
            var cell = new Panel
            {
                BackColor = Color.FromArgb(20, 24, 44),
                Margin = new Padding(8),
                Padding = new Padding(0),
                Dock = DockStyle.Fill
            };
            var cellLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2
            };
            cellLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            cellLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            cellLayout.Controls.Add(cap, 0, 0);
            cellLayout.Controls.Add(pb, 0, 1);
            cell.Controls.Add(cellLayout);

            _tiles[user] = (cell, pb);
            ReflowTiles(); // thêm xong là sắp xếp lại
            return pb;
        }

        // Sắp xếp lưới linh hoạt (hỗ trợ 5,6,10... người)
        private void ReflowTiles()
        {
            _videoGrid.SuspendLayout();

            _videoGrid.Controls.Clear();

            var cells = _tiles
                .OrderBy(kv => kv.Key, StringComparer.Ordinal) // hoặc thay bằng thứ tự join
                .Select(kv => kv.Value.cell)
                .ToList();

            int count = cells.Count;
            if (count == 0)
            {
                _videoGrid.RowCount = 1;
                _videoGrid.ColumnCount = 1;
                _videoGrid.RowStyles.Clear();
                _videoGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                _videoGrid.ColumnStyles.Clear();
                _videoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                _videoGrid.ResumeLayout();
                return;
            }

            // Tính số cột theo bề rộng hiện tại
            int clientW = Math.Max(1, _videoGrid.ClientSize.Width);
            int approxTileW = MinTileWidth + 16; // 16 ~ margin 8 hai bên
            int colsByWidth = Math.Max(1, clientW / approxTileW);
            int cols = Math.Min(MaxCols, Math.Max(1, colsByWidth));
            cols = Math.Min(cols, count);
            int rows = (int)Math.Ceiling((double)count / cols);

            _videoGrid.ColumnCount = cols;
            _videoGrid.RowCount = rows;

            _videoGrid.ColumnStyles.Clear();
            for (int c = 0; c < cols; c++)
                _videoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cols));

            _videoGrid.RowStyles.Clear();
            for (int r = 0; r < rows; r++)
                _videoGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));

            for (int i = 0; i < count; i++)
            {
                int row = i / cols;
                int col = i % cols;
                _videoGrid.Controls.Add(cells[i], col, row);
            }

            _videoGrid.ResumeLayout();
        }

        // ============ Encode/Decode "<user>|bytes" ============
        private static bool TrySplitUserPayload(byte[] payload, out string user, out byte[] rest)
        {
            user = string.Empty; rest = Array.Empty<byte>();
            int sep = Array.IndexOf(payload, (byte)'|');
            if (sep <= 0) return false;
            user = Encoding.UTF8.GetString(payload, 0, sep);
            var len = payload.Length - (sep + 1);
            rest = new byte[len];
            Buffer.BlockCopy(payload, sep + 1, rest, 0, len);
            return true;
        }

        // ============ Camera ============
        private void StartCamera()
        {
            try
            {
                if (_videoMode == VideoInputMode.Demo)
                {
                    StartDemoVideo();
                    return;
                }

                var camIdx = _selectedCamIndex >= 0 ? _selectedCamIndex : (_cmbCam.SelectedIndex >= 0 ? _cmbCam.SelectedIndex : 0);
                _cvCap = new OpenCvSharp.VideoCapture(camIdx);
                if (!_cvCap.IsOpened())
                {
                    MessageBox.Show("Không mở được camera (OpenCvSharp).");
                    _camOn = false; _btnCam.Text = "Camera: Tắt"; return;
                }
                _videoTimer = new System.Windows.Forms.Timer { Interval = 100 };
                _videoTimer.Tick += CameraTimer_Tick; // handler riêng để có thể gỡ ra
                _videoTimer.Start();
            }
            catch (Exception ex) { MessageBox.Show("Lỗi camera: " + ex.Message); }
        }

        private async void CameraTimer_Tick(object? sender, EventArgs e)
        {
            if (!_camOn || _cvCap == null) return;
            using var mat = new Mat();
            if (!_cvCap.Read(mat) || mat.Empty()) return;
            using var bmp = BitmapConverter.ToBitmap(mat);
            await SendFrameAsync((Bitmap)bmp.Clone());
        }

        private void StopCamera()
        {
            try { _cvCap?.Release(); _cvCap?.Dispose(); } catch { }
            _cvCap = null;
            _picLocal.Image?.Dispose();
            _picLocal.Image = null;
            if (_videoTimer != null)
            {
                try { _videoTimer.Tick -= CameraTimer_Tick; _videoTimer.Stop(); } catch { }
                _videoTimer.Dispose();
                _videoTimer = null;
            }
        }

        // Nén JPEG và gửi khung hình (giới hạn ~10fps, chất lượng ~60)
        private async Task SendFrameAsync(Bitmap bmpFull)
        {
            using (bmpFull)
            using (var bmp = new SD.Bitmap(bmpFull, new SD.Size(640, 360)))
            {
                // preview
                if (_picLocal.InvokeRequired)
                {
                    _picLocal.BeginInvoke(new Action(() =>
                    {
                        _picLocal.Image?.Dispose();
                        _picLocal.Image = (Bitmap)bmp.Clone();
                    }));
                }
                else
                {
                    _picLocal.Image?.Dispose();
                    _picLocal.Image = (Bitmap)bmp.Clone();
                }

                var now = Environment.TickCount64;
                if (now - _lastVideoSentMs < 100) return; // ~10 fps
                _lastVideoSentMs = now;

                using var ms = new MemoryStream();
                var enc = SD.Imaging.ImageCodecInfo.GetImageEncoders()
                          .First(x => x.FormatID == SD.Imaging.ImageFormat.Jpeg.Guid);
                using var ep = new SD.Imaging.EncoderParameters(1);
                ep.Param[0] = new SD.Imaging.EncoderParameter(SD.Imaging.Encoder.Quality, 60L);
                bmp.Save(ms, enc, ep);

                var jpeg = ms.ToArray();
                if (jpeg.Length < 250_000)
                {
                    var header = Encoding.UTF8.GetBytes(_username + "|");
                    var payload = new byte[header.Length + jpeg.Length];
                    Buffer.BlockCopy(header, 0, payload, 0, header.Length);
                    Buffer.BlockCopy(jpeg, 0, payload, header.Length, jpeg.Length);
                    await SafeSendAsync(MsgType.Video, payload);
                }
            }
        }

        // DEMO video
        private void StartDemoVideo()
        {
            _videoTimer = new System.Windows.Forms.Timer { Interval = 100 }; // ~10 fps
            _videoTimer.Tick += VideoTimer_DemoTick;
            _demoFrameTick = 0;
            _videoTimer.Start();
        }

        private async void VideoTimer_DemoTick(object? sender, EventArgs e)
        {
            if (!_camOn) return;
            try
            {
                int w = 240, h = 180; // kích thước demo
                using var bmp = new Bitmap(w, h);
                using var g = Graphics.FromImage(bmp);
                g.Clear(Color.FromArgb(20, 20, 20));
                int t = _demoFrameTick++;
                var rnd = new Random(t);
                for (int i = 0; i < 5; i++)
                {
                    var rect = new Rectangle((t * 7 + i * 60) % w, 40 + i * 40, 120, 30);
                    using var br = new SolidBrush(Color.FromArgb(rnd.Next(60, 200), rnd.Next(60, 200), rnd.Next(60, 200)));
                    g.FillRectangle(br, rect);
                }
                using var f = new Font("Segoe UI", 12, FontStyle.Bold);
                g.DrawString($"DEMO {_username}", f, Brushes.White, 10, 10);
                await SendFrameAsync((Bitmap)bmp.Clone());
            }
            catch (Exception ex)
            {
                HandleNetException(ex);
            }
        }

        // ============ Mic ============
        private void StartMic()
        {
            try
            {
                if (_micMode == MicInputMode.Demo)
                {
                    StartDemoMic();
                    return;
                }

                var devCount = WaveIn.DeviceCount;
                int micIdx = (_cmbMic.SelectedIndex >= 0 && _cmbMic.SelectedIndex < devCount) ? _cmbMic.SelectedIndex : 0;
                _selectedMicIndex = micIdx;

                _mic = new WaveInEvent
                {
                    DeviceNumber = micIdx,
                    WaveFormat = new WaveFormat(16000, 1),
                    BufferMilliseconds = 40,
                    NumberOfBuffers = 4
                };
                _mic.DataAvailable += async (s, e) =>
                {
                    if (!_micOn) return;
                    // Noise gate đơn giản
                    int max = 0;
                    for (int i = 0; i < e.BytesRecorded; i += 2)
                    {
                        short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                        int abs = sample < 0 ? -sample : sample;
                        if (abs > max) max = abs;
                    }
                    if (max < 500) return;

                    var pcm = e.Buffer.Take(e.BytesRecorded).ToArray();
                    await SafeSendAsync(MsgType.Audio, pcm);
                };
                _mic.StartRecording();
            }
            catch (Exception ex) { MessageBox.Show("Lỗi mic: " + ex.Message); }
        }

        private void StopMic()
        {
            try { _mic?.StopRecording(); _mic?.Dispose(); } catch { }
            _mic = null;
            try { _speaker?.Stop(); _speaker?.Dispose(); } catch { }
            _speaker = null;
            if (_micDemoCts != null)
            {
                try { _micDemoCts.Cancel(); } catch { }
                _micDemoCts = null;
            }
        }

        private void StartDemoMic()
        {
            _micDemoCts = new System.Threading.CancellationTokenSource();
            var ct = _micDemoCts.Token;
            var sampleRate = 16000;
            var freq = 440.0; // A4
            var twoPi = Math.PI * 2.0;
            int samplesPerChunk = sampleRate / 25; // ~40ms
            double phase = 0;

            _ = Task.Run(async () =>
            {
                var buf = new byte[samplesPerChunk * 2];
                while (!ct.IsCancellationRequested)
                {
                    if (!_micOn) { await Task.Delay(20, ct); continue; }
                    for (int i = 0; i < samplesPerChunk; i++)
                    {
                        short s = (short)(Math.Sin(phase) * short.MaxValue / 6);
                        buf[2 * i] = (byte)(s & 0xff);
                        buf[2 * i + 1] = (byte)((s >> 8) & 0xff);
                        phase += twoPi * freq / sampleRate;
                        if (phase > twoPi) phase -= twoPi;
                    }
                    try { await SafeSendAsync(MsgType.Audio, buf.ToArray()); } catch { }
                    await Task.Delay(40, ct);
                }
            }, ct);
        }

        // ======= Kick dialog (tuỳ chọn) =======
        private string? ShowKickDialog()
        {
            var users = _tiles.Keys
                .Concat(_lstUsers.Items.Cast<string>().Select(x => x.Split(' ')[0]))
                .Distinct()
                .Where(u => u != _username)
                .ToList();
            if (users.Count == 0) { MessageBox.Show("Không có user nào để kick."); return null; }

            var frm = new Form { Text = "Chọn user để kick", Width = 300, Height = 360, StartPosition = FormStartPosition.CenterParent };
            var lb = new ListBox { Dock = DockStyle.Fill };
            foreach (var u in users) lb.Items.Add(u);
            var ok = new Button { Text = "Kick", Dock = DockStyle.Bottom, Height = 32 };
            ok.Click += (_, __) => frm.DialogResult = DialogResult.OK;
            frm.Controls.Add(lb);
            frm.Controls.Add(ok);
            return frm.ShowDialog(this) == DialogResult.OK && lb.SelectedItem is string s ? s : null;
        }

        // Nạp danh sách camera (OpenCvSharp không liệt kê tên, chỉ index)
        private void RefreshCameras()
        {
            _cmbCam.Items.Clear();
            for (int i = 0; i < 4; i++) _cmbCam.Items.Add($"Camera #{i}");
            if (_cmbCam.Items.Count > 0)
            {
                if (_selectedCamIndex >= 0 && _selectedCamIndex < _cmbCam.Items.Count)
                    _cmbCam.SelectedIndex = _selectedCamIndex;
                else _cmbCam.SelectedIndex = 0;
            }
        }

        // Nạp danh sách micro khả dụng từ NAudio
        private void RefreshMics()
        {
            try
            {
                _cmbMic.Items.Clear();
                for (int i = 0; i < WaveIn.DeviceCount; i++)
                {
                    var caps = WaveIn.GetCapabilities(i);
                    _cmbMic.Items.Add(string.IsNullOrWhiteSpace(caps.ProductName) ? $"Mic #{i}" : caps.ProductName);
                }
                if (_cmbMic.Items.Count > 0)
                {
                    if (_selectedMicIndex >= 0 && _selectedMicIndex < _cmbMic.Items.Count)
                        _cmbMic.SelectedIndex = _selectedMicIndex;
                    else _cmbMic.SelectedIndex = 0;
                }
            }
            catch { }
        }
    }
}
