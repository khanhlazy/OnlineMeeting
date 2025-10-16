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
            RenderMode = ToolStripRenderMode.System,
            Padding = new Padding(5, 0, 5, 0)
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

        private readonly StatusStrip _status = new()
        {
            Padding = new Padding(5, 0, 5, 0)
        };
        private readonly ToolStripStatusLabel _lblNet = new() { Text = "Sẵn sàng" };

        // Cột trái: Preview + Users
        private readonly PictureBox _picLocal = new()
        {
            Dock = DockStyle.Top,
            BorderStyle = BorderStyle.FixedSingle,
            SizeMode = PictureBoxSizeMode.Zoom,
            MinimumSize = new DSize(240, 180)
        };
        private readonly Label _lbLocal = new() { Dock = DockStyle.Top, Height = 20, TextAlign = ContentAlignment.MiddleCenter };
        private readonly ListBox _lstUsers = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
        private readonly Button _btnKick = new() { Text = "Kick (Host)", Dock = DockStyle.Bottom, Height = 30, Enabled = false };

        // Trung tâm: Video grid + Chat
        private readonly TableLayoutPanel _videoGrid = new()
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 2,
            BackColor = Theme.Palette.Background,
            Padding = new Padding(10),
            Margin = new Padding(5)
        };

        private readonly RichTextBox _rtbChat = new()
        {
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle
        };
        private readonly TextBox _txtChat = new() { PlaceholderText = "Nhập tin nhắn và Enter...", Dock = DockStyle.Fill, Height = 30 };
        private readonly Button _btnSend = new() { Text = "Gửi", Dock = DockStyle.Fill, Height = 30 };

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

        // Multi-video: quản lý tile theo username
        private readonly Dictionary<string, PictureBox> _remoteTiles = new();

        public MeetingForm(ClientNet net, string username, string roomId, bool isHost)
        {
            _net = net; _username = username; _roomId = roomId; _isHost = isHost;

            // cấu hình cột 50/50
            _videoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            _videoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            _videoGrid.RowStyles.Clear();
        }

        // Màn hình phòng họp: quản lý video/mic, chat, danh sách người tham gia
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Text = $"Phòng {_roomId} - Người dùng: {_username}{(_isHost ? " (Host)" : "")}";
            MinimumSize = new DSize(800, 600);
            Width = 1280; Height = 800;

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
            Controls.Add(_toolbar);

            // ===== Status bar =====
            _status.Items.Add(_lblNet);
            _status.Dock = DockStyle.Bottom;
            Controls.Add(_status);

            // ===== Khối trái: preview + users ====
            var left = new Panel { Dock = DockStyle.Left, Width = 280, Padding = new Padding(10) };
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
                FixedPanel = FixedPanel.Panel2,
                Panel2MinSize = 150,
                SplitterWidth = 6,
                SplitterDistance = Height - 200
            };

            // Video Grid
            split.Panel1.Controls.Add(_videoGrid);

            // Chat (TableLayout with header)
            var chatPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            var chatHeader = new Label
            {
                Text = "Chat",
                Dock = DockStyle.Top,
                Height = 25,
                BackColor = Theme.Palette.BackgroundDark,
                ForeColor = Theme.Palette.TextSecondary, // Adjusted to use TextSecondary
                TextAlign = ContentAlignment.MiddleCenter
            };
            var chatLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0, 5, 0, 0)
            };
            chatLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 90));
            chatLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));

            chatLayout.Controls.Add(_rtbChat, 0, 0);
            var sendPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Padding = new Padding(0, 0, 5, 0)
            };
            sendPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75));
            sendPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            sendPanel.Controls.Add(_txtChat, 0, 0);
            sendPanel.Controls.Add(_btnSend, 1, 0);
            chatLayout.Controls.Add(sendPanel, 0, 1);

            chatPanel.Controls.Add(chatHeader);
            chatPanel.Controls.Add(chatLayout);
            split.Panel2.Controls.Add(chatPanel);
            Controls.Add(split);

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

            // Nạp danh sách thiết bị lần đầu
            RefreshCameras();
            RefreshMics();
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
                await _net.SendAsync(MsgType.Kick, Packet.Str(target));
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

                            // tạo sẵn tile video cho từng người
                            EnsureTile(name);
                        }
                        AdjustVideoGridLayout();
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
            var pb = EnsureTile(user);
            var old = pb.Image;
            pb.Image = (Image)img.Clone();
            old?.Dispose();
        }

        private PictureBox EnsureTile(string user)
        {
            if (_remoteTiles.TryGetValue(user, out var pb))
                return pb;

            // PictureBox hiển thị
            pb = new PictureBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                MinimumSize = new DSize(240, 180)
            };

            // Header tên
            var cap = new Label
            {
                Text = user,
                Dock = DockStyle.Top,
                Height = 25,
                ForeColor = Theme.Palette.TextSecondary,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(6, 3, 6, 3)
            };

            // Cell panel
            var cell = new Panel
            {
                BackColor = Color.FromArgb(20, 24, 44),
                Margin = new Padding(10),
                Padding = new Padding(0),
                Dock = DockStyle.Fill
            };
            var cellLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2
            };
            cellLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            cellLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            cellLayout.Controls.Add(cap, 0, 0);
            cellLayout.Controls.Add(pb, 0, 1);
            cell.Controls.Add(cellLayout);

            // Tính hàng/cột (2 cột)
            var index = _videoGrid.Controls.Count;
            var row = index / 2;
            var col = index % 2;

            if (row >= _videoGrid.RowCount)
            {
                _videoGrid.RowCount = row + 1;
                _videoGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            _videoGrid.Controls.Add(cell, col, row);

            _remoteTiles[user] = pb;
            return pb;
        }

        private void AdjustVideoGridLayout()
        {
            var participantCount = _remoteTiles.Count + 1; // Include local user
            var rows = (int)Math.Ceiling(participantCount / 2.0);
            _videoGrid.RowCount = rows;
            _videoGrid.RowStyles.Clear();
            for (int i = 0; i < rows; i++)
            {
                _videoGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
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
                _videoTimer.Tick += async (_, __) =>
                {
                    if (!_camOn || _cvCap == null) return;
                    using var mat = new Mat();
                    if (!_cvCap.Read(mat) || mat.Empty()) return;
                    using var bmp = BitmapConverter.ToBitmap(mat);
                    await SendFrameAsync((Bitmap)bmp.Clone());
                };
                _videoTimer.Start();
            }
            catch (Exception ex) { MessageBox.Show("Lỗi camera: " + ex.Message); }
        }

        private void StopCamera()
        {
            try { _cvCap?.Release(); _cvCap?.Dispose(); } catch { }
            _cvCap = null;
            _picLocal.Image?.Dispose();
            _picLocal.Image = null;
            if (_videoTimer != null)
            {
                try { _videoTimer.Stop(); _videoTimer.Tick -= VideoTimer_Tick; } catch { }
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
                    await _net.SendAsync(MsgType.Video, payload);
                }
            }
        }

        // DEMO video
        private void StartDemoVideo()
        {
            _videoTimer = new System.Windows.Forms.Timer { Interval = 100 }; // ~10 fps
            _videoTimer.Tick += VideoTimer_Tick;
            _demoFrameTick = 0;
            _videoTimer.Start();
        }

        private async void VideoTimer_Tick(object? sender, EventArgs e)
        {
            if (!_camOn) return;
            int w = 640, h = 360;
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
            using var f = new Font("Segoe UI", 16, FontStyle.Bold);
            g.DrawString($"DEMO {_username}", f, Brushes.White, 10, 10);
            await SendFrameAsync((Bitmap)bmp.Clone());
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
                    await _net.SendAsync(MsgType.Audio, pcm);
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
                    try { await _net.SendAsync(MsgType.Audio, buf.ToArray()); } catch { }
                    await Task.Delay(40, ct);
                }
            }, ct);
        }

        // ======= Kick dialog (không gọi ở đâu — giữ lại nếu muốn dùng) =======
        private string? ShowKickDialog()
        {
            var users = _remoteTiles.Keys
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