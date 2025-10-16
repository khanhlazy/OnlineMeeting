using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MeetingClient.UI
{
    public static class Theme
    {
        // Bảng màu chủ đạo cho giao diện tối (dark theme) với điểm nhấn tím/teal
        public static class Palette
        {
            public static readonly Color BackgroundDark = Color.FromArgb(22, 26, 48);
            public static readonly Color Background      = Color.FromArgb(30, 34, 64);
            public static readonly Color BackgroundLight = Color.FromArgb(38, 43, 80);
            public static readonly Color AccentPrimary   = Color.FromArgb(111, 76, 255); // purple
            public static readonly Color AccentSecondary = Color.FromArgb(0, 199, 190);  // teal
            public static readonly Color AccentDanger    = Color.FromArgb(240, 84, 84);
            public static readonly Color TextPrimary     = Color.White;
            public static readonly Color TextSecondary   = Color.FromArgb(200, 205, 230);
        }

        // Áp dụng theme chung cho Form và toàn bộ control con
        public static void Apply(Form form)
        {
            form.BackColor = Palette.Background;
            form.ForeColor = Palette.TextPrimary;
            form.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            StyleChildren(form);
        }

        // Đi qua từng control con để áp dụng màu sắc/kiểu dáng phù hợp
        private static void StyleChildren(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is StatusStrip ss)
                {
                    ss.BackColor = Palette.BackgroundDark;
                    ss.ForeColor = Palette.TextSecondary;
                }
                else if (c is ToolStrip ts)
                {
                    ts.BackColor = Palette.BackgroundDark;
                    ts.ForeColor = Palette.TextSecondary;
                }
                else if (c is Button b)
                {
                    StyleButton(b);
                }
                else if (c is TextBox tb)
                {
                    tb.BorderStyle = BorderStyle.FixedSingle;
                    tb.BackColor = Palette.BackgroundLight;
                    tb.ForeColor = Palette.TextPrimary;
                }
                else if (c is RichTextBox rtb)
                {
                    rtb.BorderStyle = BorderStyle.FixedSingle;
                    rtb.BackColor = Palette.BackgroundLight;
                    rtb.ForeColor = Palette.TextPrimary;
                }
                else if (c is ListBox lb)
                {
                    lb.BorderStyle = BorderStyle.FixedSingle;
                    lb.BackColor = Palette.BackgroundLight;
                    lb.ForeColor = Palette.TextPrimary;
                }
                else if (c is ComboBox cb)
                {
                    cb.FlatStyle = FlatStyle.Flat;
                    cb.BackColor = Palette.BackgroundLight;
                    cb.ForeColor = Palette.TextPrimary;
                }
                else if (c is Label lbl)
                {
                    lbl.ForeColor = c.Enabled ? Palette.TextSecondary : Color.Gray;
                }

                // Recurse
                if (c.HasChildren) StyleChildren(c);
            }
        }

        // Style cho nút hành động chính (primary)
        public static void StylePrimary(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            b.BackColor = Palette.AccentPrimary;
            b.ForeColor = Palette.TextPrimary;

            b.FlatAppearance.BorderSize = 0; // KHÔNG đặt BorderColor = Transparent để tránh crash
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(130, 95, 255);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(95, 65, 230);
        }

        // Style cho nút phụ (secondary)
        public static void StyleSecondary(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            b.BackColor = Palette.AccentSecondary;
            b.ForeColor = Palette.BackgroundDark;

            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 180, 175);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 160, 160);
        }

        // Style cho nút cảnh báo/nguy hiểm (danger)
        public static void StyleDanger(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            b.BackColor = Palette.AccentDanger;
            b.ForeColor = Palette.TextPrimary;

            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(220, 70, 70);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(200, 60, 60);
        }

        // Style mặc định cho các Button chưa gán kiểu cụ thể
        private static void StyleButton(Button b)
        {
            // Default button style (subtle) if not explicitly themed
            if (b.BackColor.IsEmpty || b.BackColor == SystemColors.Control)
            {
                b.BackColor = Palette.BackgroundLight;
                b.ForeColor = Palette.TextPrimary;
            }
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(70, 80, 120);
            b.FlatAppearance.BorderSize = 1;
        }

        // ==============================
        // Overload cho ToolStripButton
        // ==============================

        public static void StyleSecondary(ToolStripButton btn)
        {
            btn.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            btn.AutoSize = false;
            btn.Width = 90;
            btn.Height = 28;
            btn.Margin = new Padding(2);

            btn.BackColor = Palette.AccentSecondary;
            btn.ForeColor = Palette.BackgroundDark;

            btn.EnabledChanged += (_, __) =>
            {
                btn.ForeColor = btn.Enabled ? Palette.BackgroundDark : Color.Gray;
                btn.BackColor = btn.Enabled ? Palette.AccentSecondary : Color.FromArgb(80, 90, 100);
            };
        }

        public static void StyleDanger(ToolStripButton btn)
        {
            btn.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            btn.AutoSize = false;
            btn.Width = 90;
            btn.Height = 28;
            btn.Margin = new Padding(2);

            btn.BackColor = Palette.AccentDanger;
            btn.ForeColor = Palette.TextPrimary;

            btn.EnabledChanged += (_, __) =>
            {
                btn.ForeColor = btn.Enabled ? Palette.TextPrimary : Color.Silver;
                btn.BackColor = btn.Enabled ? Palette.AccentDanger : Color.FromArgb(120, 35, 45);
            };
        }
    }
}
