using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
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

        private RichTextBox rtbChat = new(){ Dock=DockStyle.Fill, ReadOnly=true };
        private TextBox txtChat = new(){ Dock=DockStyle.Bottom };
        private Button btnSend = new(){ Text="Gửi", Dock=DockStyle.Bottom };
        private ListBox lstUsers = new(){ Dock=DockStyle.Right, Width=180 };
        private Button btnKick = new(){ Text="Kick", Dock=DockStyle.Right, Enabled=false };
        private Button btnCam = new(){ Text="Camera: Tắt", Dock=DockStyle.Top };
        private Button btnMic = new(){ Text="Mic: Tắt", Dock=DockStyle.Top };
        private PictureBox picLocal = new(){ Dock=DockStyle.Top, Height=180, BorderStyle=BorderStyle.FixedSingle, SizeMode=PictureBoxSizeMode.Zoom };
        private FlowLayoutPanel videoPanel = new(){ Dock=DockStyle.Fill, AutoScroll=true };

        private FilterInfoCollection? _cams;
        private VideoCaptureDevice? _camDev;
        private bool _camOn = false;

        private WaveInEvent? _mic;
        private BufferedWaveProvider? _audioBuffer;
        private WaveOutEvent? _speaker;
        private bool _micOn = false;

        public MeetingForm(ClientNet net, string username, string roomId, bool isHost)
        {
            _net = net; _username = username; _roomId = roomId; _isHost = isHost;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Text = $"Phòng {_roomId} - Người dùng: {_username}{(_isHost ? " (Host)" : "")}";
            Width = 1100; Height = 720;

            // ====== LEFT: Preview + Buttons + Kick + Users ======
            var left = new Panel { Dock = DockStyle.Left, Width = 260, Padding = new Padding(8) };

            // Preview local
            picLocal.Height = 160;
            picLocal.Dock = DockStyle.Top;
            left.Controls.Add(picLocal);

            // Hàng nút Cam/Mic
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 40,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            btnCam.Width = 110; btnMic.Width = 110;
            buttons.Controls.Add(btnCam);
            buttons.Controls.Add(btnMic);
            left.Controls.Add(buttons);

            // Nút Kick
            btnKick.Dock = DockStyle.Top;
            btnKick.Enabled = _isHost;
            left.Controls.Add(btnKick);

            // Danh sách người dùng
            lstUsers.Dock = DockStyle.Fill;
            left.Controls.Add(lstUsers);

            // ====== CENTER: Video panel + Chat ======
            var chatPanel = new Panel { Dock = DockStyle.Bottom, Height = 150, Padding = new Padding(8, 4, 8, 8) };
            txtChat.Dock = DockStyle.Bottom;
            btnSend.Dock = DockStyle.Bottom;
            chatPanel.Controls.Add(btnSend);
            chatPanel.Controls.Add(txtChat);

            rtbChat.Dock = DockStyle.Bottom;
            rtbChat.Height = 110;
            chatPanel.Controls.Add(rtbChat);

            var center = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            videoPanel.Dock = DockStyle.Fill;
            center.Controls.Add(videoPanel);
            center.Controls.Add(chatPanel);

            Controls.Add(center);
            Controls.Add(left);

            // ====== Events ======
            btnSend.Click += async (_, __) =>
            {
                if (string.IsNullOrWhiteSpace(txtChat.Text)) return;
                var msg = $"{_username}: {txtChat.Text}";
                await _net.SendAsync(MsgType.Chat, Packet.Str(msg));
                rtbChat.AppendText("(Bạn) " + txtChat.Text + Environment.NewLine);
                txtChat.Clear();
            };
            txtChat.KeyDown += (s, ev) => { if (ev.KeyCode == Keys.Enter) { btnSend.PerformClick(); ev.SuppressKeyPress = true; } };

            btnCam.Click += async (_, __) =>
            {
                _camOn = !_camOn;
                btnCam.Text = _camOn ? "Camera: Bật" : "Camera: Tắt";
                await _net.SendAsync(MsgType.ToggleCam, Packet.Str(_camOn ? "ON" : "OFF"));
                if (_camOn) StartCamera(); else StopCamera();
            };

            btnMic.Click += async (_, __) =>
            {
                _micOn = !_micOn;
                btnMic.Text = _micOn ? "Mic: Bật" : "Mic: Tắt";
                await _net.SendAsync(MsgType.ToggleMic, Packet.Str(_micOn ? "ON" : "OFF"));
                if (_micOn) StartMic(); else StopMic();
            };

            btnKick.Click += async (_, __) =>
            {
                if (lstUsers.SelectedItem is string u && !u.Contains("(Host)") && !u.StartsWith(_username + " "))
                    await _net.SendAsync(MsgType.Kick, Packet.Str(u.Split(' ')[0]));
            };

            _net.OnMessage += Net_OnMessage;
        }

        protected override async void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            await _net.SendAsync(MsgType.Leave, Packet.Str(""));
            StopCamera(); StopMic();
        }

        // ==== Mọi cập nhật UI đều qua BeginInvoke ====
        private void Net_OnMessage(MsgType t, byte[] p)
        {
            if (!this.IsHandleCreated) return;

            switch (t)
            {
                case MsgType.Chat:
                {
                    var s = Packet.Str(p);
                    this.BeginInvoke(new Action(() => rtbChat.AppendText(s + "\n")));
                    break;
                }
                case MsgType.Participants:
                {
                    var s = Packet.Str(p);
                    this.BeginInvoke(new Action(() =>
                    {
                        lstUsers.Items.Clear();
                        foreach (var item in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var parts = item.Split(':');
                            var name = parts[0];
                            var isHost = parts[1] == "1";
                            var cam = parts[2] == "1"; var mic = parts[3] == "1";
                            lstUsers.Items.Add(name + (isHost ? " (Host)" : " ")
                                + $" [Cam:{(cam ? "On" : "Off")} Mic:{(mic ? "On" : "Off")}]");
                        }
                    }));
                    break;
                }
                case MsgType.Info:
                {
                    var s = Packet.Str(p);
                    if (s == "KICKED")
                        this.BeginInvoke(new Action(() =>
                        {
                            MessageBox.Show("Bạn đã bị host đưa ra khỏi phòng.");
                            Close();
                        }));
                    break;
                }
                case MsgType.Video:
                {
                    try
                    {
                        using var ms = new MemoryStream(p);
                        var img = Image.FromStream(ms);
                        this.BeginInvoke(new Action(() => ShowRemoteFrame(img)));
                    }
                    catch { }
                    break;
                }
                case MsgType.Audio:
                {
                    if (_audioBuffer == null)
                    {
                        _audioBuffer = new BufferedWaveProvider(new WaveFormat(16000,1));
                        _speaker = new WaveOutEvent();
                        _speaker.Init(_audioBuffer);
                        _speaker.Play();
                    }
                    _audioBuffer.AddSamples(p, 0, p.Length);
                    break;
                }
            }
        }

        private void ShowRemoteFrame(Image img)
        {
            if (videoPanel.Controls.Count == 0)
            {
                videoPanel.Controls.Add(new PictureBox{
                    Width=320, Height=240, SizeMode=PictureBoxSizeMode.Zoom, BorderStyle=BorderStyle.FixedSingle, Image=img
                });
            }
            else
            {
                var pb = (PictureBox)videoPanel.Controls[0];
                var old = pb.Image; pb.Image = (Image)img.Clone(); old?.Dispose();
            }
        }

        private void StartCamera()
        {
            try
            {
                _cams = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                if (_cams.Count == 0) { MessageBox.Show("Không tìm thấy camera"); _camOn=false; btnCam.Text="Camera: Tắt"; return; }
                _camDev = new VideoCaptureDevice(_cams[0].MonikerString);
                _camDev.NewFrame += async (s, e) =>
                {
                    if (!_camOn) return;
                    using var bmp = (Bitmap)e.Frame.Clone();
                    picLocal.Image?.Dispose();
                    picLocal.Image = (Bitmap)bmp.Clone();
                    using var ms = new MemoryStream();
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                    var data = ms.ToArray();
                    if (data.Length < 200_000)
                        await _net.SendAsync(MsgType.Video, data);
                };
                _camDev.Start();
            }
            catch (Exception ex) { MessageBox.Show("Lỗi camera: "+ex.Message); }
        }

        private void StopCamera()
        {
            try{ _camDev?.SignalToStop(); _camDev?.WaitForStop(); } catch {}
            _camDev = null;
            picLocal.Image = null;
        }

        private void StartMic()
        {
            try
            {
                _mic = new WaveInEvent(){ WaveFormat = new WaveFormat(16000,1) };
                _mic.DataAvailable += async (s,e)=>{
                    if (!_micOn) return;
                    var pcm = new byte[e.BytesRecorded];
                    Buffer.BlockCopy(e.Buffer, 0, pcm, 0, e.BytesRecorded);
                    await _net.SendAsync(MsgType.Audio, pcm);
                };
                _mic.StartRecording();
            }
            catch (Exception ex) { MessageBox.Show("Lỗi mic: "+ex.Message); }
        }

        private void StopMic()
        {
            try{ _mic?.StopRecording(); _mic?.Dispose(); } catch {}
            _mic=null;
        }
    }
}
