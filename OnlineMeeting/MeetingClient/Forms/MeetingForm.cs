using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Collections.Generic;
using MeetingClient.Net;
using MeetingShared;

using AForge.Video;
using AForge.Video.DirectShow;
using NAudio.Wave;

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

        private readonly StatusStrip _status = new();
        private readonly ToolStripStatusLabel _lblNet = new() { Text = "Sẵn sàng" };

        // Cột trái: Preview + Users
        private readonly PictureBox _picLocal = new()
        {
            Height = 170, Dock = DockStyle.Top, BorderStyle = BorderStyle.FixedSingle, SizeMode = PictureBoxSizeMode.Zoom
        };
        private readonly Label _lbLocal = new() { Dock = DockStyle.Top, Height = 18, TextAlign = ContentAlignment.MiddleCenter };
        private readonly ListBox _lstUsers = new() { Dock = DockStyle.Fill };
        private readonly Button _btnKick = new() { Text = "Kick (Host)", Dock = DockStyle.Bottom, Height = 32, Enabled = false };

        // Trung tâm: Video grid + Chat
        private readonly FlowLayoutPanel _videoGrid = new()
        {
            Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Padding = new Padding(6)
        };

        private readonly RichTextBox _rtbChat = new()
        {
            ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical, Dock = DockStyle.Fill
        };
        private readonly TextBox _txtChat = new() { PlaceholderText = "Nhập tin nhắn và Enter...", Dock = DockStyle.Fill, Height = 26 };
        private readonly Button _btnSend = new() { Text = "Gửi", Dock = DockStyle.Right, Width = 68 };

        // Menu chuột phải trên danh sách user (tiện kick)
        private readonly ContextMenuStrip _userMenu = new();
        private readonly ToolStripMenuItem _miKick = new("Kick người này");

        // ========== Thiết bị ==========
        private FilterInfoCollection? _cams;
        private VideoCaptureDevice? _camDev;
        private bool _camOn = false;

        private WaveInEvent? _mic;
        private BufferedWaveProvider? _audioBuffer;
        private WaveOutEvent? _speaker;
        private bool _micOn = false;

        // Multi-video: quản lý tile theo username
        private readonly Dictionary<string, PictureBox> _remoteTiles = new();

        public MeetingForm(ClientNet net, string username, string roomId, bool isHost)
        {
            _net = net; _username = username; _roomId = roomId; _isHost = isHost;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Text = $"Phòng {_roomId} - Người dùng: {_username}{(_isHost ? " (Host)" : "")}";
            Width = 1280; Height = 780;

            // ===== Toolbar =====
            _lblRoom.Text = $"Phòng: {_roomId}";
            _toolbar.Items.AddRange(new ToolStripItem[] { _lblRoom, _btnCopyRoom, _sep1, _btnCam, _btnMic, _sep2, _btnLeave });
            Controls.Add(_toolbar);

            // ===== Status bar =====
            _status.Items.Add(_lblNet);
            _status.Dock = DockStyle.Bottom;
            Controls.Add(_status);

            // ===== Khối trái: preview + users ====
            var left = new Panel { Dock = DockStyle.Left, Width = 260, Padding = new Padding(8) };
            _lbLocal.Text = $"({_username}) Video của bạn";
            _btnKick.Enabled = _isHost;

            left.Controls.Add(_btnKick);
            left.Controls.Add(_lstUsers);
            left.Controls.Add(_lbLocal);
            left.Controls.Add(_picLocal);
            Controls.Add(left);

            // ===== Khối giữa: videos + chat (SplitContainer) =====
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 480,
                FixedPanel = FixedPanel.Panel2
            };

            // Video Grid
            split.Panel1.Controls.Add(_videoGrid);

            // Chat (TableLayout)
            var chat = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Padding = new Padding(8)
            };
            chat.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            chat.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            chat.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            chat.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            chat.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 10)); // spacing

            chat.Controls.Add(_rtbChat, 0, 0);
            chat.SetColumnSpan(_rtbChat, 3);

            var sendRow = new Panel { Dock = DockStyle.Fill, Height = 28 };
            _txtChat.Parent = sendRow;
            _btnSend.Parent = sendRow;
            _txtChat.Width = sendRow.Width - _btnSend.Width - 12;
            _txtChat.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            _btnSend.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            _btnSend.Left = sendRow.Width - _btnSend.Width;
            sendRow.Resize += (_, __) =>
            {
                _txtChat.Width = sendRow.Width - _btnSend.Width - 12;
                _btnSend.Left = sendRow.Width - _btnSend.Width;
            };

            chat.Controls.Add(sendRow, 0, 1);
            chat.SetColumnSpan(sendRow, 3);

            split.Panel2.Controls.Add(chat);
            Controls.Add(split);

            // ===== Context menu danh sách user =====
            _userMenu.Items.Add(_miKick);
            _lstUsers.ContextMenuStrip = _userMenu;
            _miKick.Enabled = _isHost;

            // ===== Sự kiện =====
            _btnCopyRoom.Click += (_, __) => { try { Clipboard.SetText(_roomId); _lblNet.Text = "Đã sao chép mã phòng"; } catch { } };
            _btnLeave.Click += (_, __) => Close();

            _btnCam.Click += async (_, __) =>
            {
                _camOn = !_camOn;
                _btnCam.Text = _camOn ? "Camera: Bật" : "Camera: Tắt";
                await _net.SendAsync(MsgType.ToggleCam, Packet.Str(_camOn ? "ON" : "OFF"));
                if (_camOn) StartCamera(); else StopCamera();
            };

            _btnMic.Click += async (_, __) =>
            {
                _micOn = !_micOn;
                _btnMic.Text = _micOn ? "Mic: Bật" : "Mic: Tắt";
                await _net.SendAsync(MsgType.ToggleMic, Packet.Str(_micOn ? "ON" : "OFF"));
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
        }

        protected override async void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            try { await _net.SendAsync(MsgType.Leave, Packet.Str("")); } catch { }
            StopCamera(); StopMic();
        }

        // ================== Chat ==================
        private async Task SendChatAsync()
        {
            var raw = _txtChat.Text.Trim();
            if (raw.Length == 0) return;

            // Gửi text thô; server sẽ ghép "<username>: "
            await _net.SendAsync(MsgType.Chat, Packet.Str(raw));

            _rtbChat.AppendText($"(Bạn) {raw}{Environment.NewLine}");
            _rtbChat.SelectionStart = _rtbChat.TextLength;
            _rtbChat.ScrollToCaret();

            _txtChat.Clear();
        }

        // ================== Kick ==================
        private async Task KickSelectedAsync()
        {
            if (!_isHost) return;
            if (_lstUsers.SelectedItem is not string line) return;
            if (line.Contains("(Host)")) { MessageBox.Show("Không thể kick Host."); return; }

            var target = line.Split(' ')[0]; // lấy username trước khoảng trắng đầu
            var ok = MessageBox.Show($"Kick '{target}' khỏi phòng?", "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ok == DialogResult.Yes)
                await _net.SendAsync(MsgType.Kick, Packet.Str(target));
        }

        // ============ Nhận dữ liệu từ server (UI -> BeginInvoke) ============
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
                        _lstUsers.Items.Clear();
                        foreach (var item in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var parts = item.Split(':');
                            var name = parts[0];
                            var isHost = parts[1] == "1";
                            var cam = parts[2] == "1"; var mic = parts[3] == "1";
                            _lstUsers.Items.Add(name + (isHost ? " (Host)" : " ")
                                + $"  [Cam:{(cam ? "On" : "Off")}  Mic:{(mic ? "On" : "Off")}]");

                            // tạo sẵn tile video cho từng người
                            EnsureTile(name);
                        }
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
                    break;
                }

                case MsgType.Video:
                {
                    // Payload: "<username>|<jpeg bytes>"
                    if (!TrySplitUserPayload(p, out var user, out var bytes)) break;
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
            var pb = EnsureTile(user);
            var old = pb.Image;
            pb.Image = (Image)img.Clone();
            old?.Dispose();
        }

        private PictureBox EnsureTile(string user)
        {
            if (_remoteTiles.TryGetValue(user, out var pb))
                return pb;

            // ô video + caption tên
            pb = new PictureBox
            {
                Width = 420, Height = 300,
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            var cap = new Label
            {
                Text = user, Dock = DockStyle.Bottom, Height = 18,
                TextAlign = ContentAlignment.MiddleCenter
            };
            var cell = new Panel
            {
                Width = pb.Width + 6, Height = pb.Height + cap.Height + 6,
                Padding = new Padding(3)
            };
            pb.Dock = DockStyle.Top;
            cell.Controls.Add(cap);
            cell.Controls.Add(pb);
            _videoGrid.Controls.Add(cell);

            _remoteTiles[user] = pb;
            return pb;
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
                _cams = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                if (_cams == null || _cams.Count == 0)
                {
                    MessageBox.Show("Không tìm thấy camera");
                    _camOn = false; _btnCam.Text = "Camera: Tắt"; return;
                }
                _camDev = new VideoCaptureDevice(_cams[0].MonikerString);
                _camDev.NewFrame += async (s, e) =>
                {
                    if (!_camOn) return;

                    using var bmpFull = (Bitmap)e.Frame.Clone();
                    using var bmp = new Bitmap(bmpFull, new Size(640, 360)); // giảm tải cho LAN
                    _picLocal.Image?.Dispose();
                    _picLocal.Image = (Bitmap)bmp.Clone();

                    using var ms = new MemoryStream();
                    var enc = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
                              .First(x => x.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
                    var ep = new System.Drawing.Imaging.EncoderParameters(1);
                    ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                        System.Drawing.Imaging.Encoder.Quality, 60L);
                    bmp.Save(ms, enc, ep);

                    var jpeg = ms.ToArray();
                    if (jpeg.Length < 250_000) // ~250KB/gói
                    {
                        var header = Encoding.UTF8.GetBytes(_username + "|");
                        var payload = new byte[header.Length + jpeg.Length];
                        Buffer.BlockCopy(header, 0, payload, 0, header.Length);
                        Buffer.BlockCopy(jpeg, 0, payload, header.Length, jpeg.Length);
                        await _net.SendAsync(MsgType.Video, payload);
                    }
                };
                _camDev.Start();
            }
            catch (Exception ex) { MessageBox.Show("Lỗi camera: " + ex.Message); }
        }

        private void StopCamera()
        {
            try { _camDev?.SignalToStop(); _camDev?.WaitForStop(); } catch { }
            _camDev = null;
            _picLocal.Image = null;
        }

        // ============ Mic ============
        private void StartMic()
        {
            try
            {
                _mic = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(16000, 1),
                    BufferMilliseconds = 40,
                    NumberOfBuffers = 4
                };
                _mic.DataAvailable += async (s, e) =>
                {
                    if (!_micOn) return;
                    var pcm = e.Buffer.Take(e.BytesRecorded).ToArray();
                    await _net.SendAsync(MsgType.Audio, pcm); // trộn chung (đơn giản)
                };
                _mic.StartRecording();
            }
            catch (Exception ex) { MessageBox.Show("Lỗi mic: " + ex.Message); }
        }

        private void StopMic()
        {
            try { _mic?.StopRecording(); _mic?.Dispose(); } catch { }
            _mic = null;
        }
    }
}
