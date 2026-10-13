using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace DistortionPlugins;

internal enum UiGlyph { Info, Collapse, Expand, Undo, Redo, Reset, Detect, Grid, Move, Freeze, Unfreeze, Link, Unlink, Mask, Backdrop, PenPressure, Save }

internal sealed class GlyphButton : Button
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal UiGlyph Glyph { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool IsSelected { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        var state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float size = Math.Min(ClientSize.Width - 8, ClientSize.Height - 8);
        graphics.TranslateTransform((ClientSize.Width - size) / 2, (ClientSize.Height - size) / 2);
        graphics.ScaleTransform(size / 24f, size / 24f);
        using var pen = new Pen(Enabled ? ForeColor : SystemColors.GrayText, 1.8f)
        { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (Glyph == UiGlyph.Info)
        {
            graphics.DrawEllipse(pen, 3, 3, 18, 18);
            graphics.DrawLine(pen, 12, 10, 12, 17);
            graphics.DrawLine(pen, 12, 7, 12, 7.2f);
        }
        else if (Glyph is UiGlyph.Collapse or UiGlyph.Expand)
        {
            float direction = Glyph == UiGlyph.Collapse ? -1 : 1;
            graphics.DrawLines(pen, new[] { new PointF(12 - direction * 3, 5), new PointF(12 + direction * 3, 12), new PointF(12 - direction * 3, 19) });
        }
        else if (Glyph is UiGlyph.Undo or UiGlyph.Redo or UiGlyph.Reset)
        {
            if (Glyph == UiGlyph.Redo) { graphics.TranslateTransform(24, 0); graphics.ScaleTransform(-1, 1); }
            graphics.DrawArc(pen, 5, 5, 14, 14, -150, 285);
            graphics.DrawLines(pen, new[] { new PointF(3, 6), new PointF(5, 12), new PointF(10, 8) });
            if (Glyph == UiGlyph.Reset) graphics.DrawRectangle(pen, 10, 10, 6, 6);
        }
        else if (Glyph == UiGlyph.Move)
        {
            graphics.DrawLine(pen, 3, 12, 21, 12);
            graphics.DrawLine(pen, 12, 3, 12, 21);
            graphics.DrawLines(pen, new[] { new PointF(7, 8), new PointF(3, 12), new PointF(7, 16) });
            graphics.DrawLines(pen, new[] { new PointF(17, 8), new PointF(21, 12), new PointF(17, 16) });
            graphics.DrawLines(pen, new[] { new PointF(8, 7), new PointF(12, 3), new PointF(16, 7) });
            graphics.DrawLines(pen, new[] { new PointF(8, 17), new PointF(12, 21), new PointF(16, 17) });
        }
        else if (Glyph is UiGlyph.Freeze or UiGlyph.Unfreeze)
        {
            DialogUi.DrawFreezeGlyph(graphics, pen, Glyph == UiGlyph.Unfreeze);
        }
        else if (Glyph == UiGlyph.Save)
        {
            graphics.DrawPolygon(pen, new[] { new PointF(4, 3), new PointF(17, 3), new PointF(21, 7), new PointF(21, 21), new PointF(4, 21) });
            graphics.DrawRectangle(pen, 8, 3, 8, 6);
            graphics.DrawRectangle(pen, 8, 14, 9, 7);
        }
        else if (Glyph == UiGlyph.Mask)
        {
            graphics.DrawEllipse(pen, 4, 4, 16, 16);
            using var fill = new SolidBrush(pen.Color);
            graphics.FillPie(fill, 4, 4, 16, 16, 90, 180);
        }
        else if (Glyph == UiGlyph.Backdrop)
        {
            graphics.DrawRectangle(pen, 3, 3, 12, 12);
            using var fill = new SolidBrush(BackColor);
            graphics.FillRectangle(fill, 9, 9, 12, 12);
            graphics.DrawRectangle(pen, 9, 9, 12, 12);
            graphics.DrawLine(pen, 9, 15, 21, 15);
            graphics.DrawLine(pen, 15, 9, 15, 21);
        }
        else if (Glyph == UiGlyph.PenPressure)
        {
            graphics.DrawPolygon(pen, new[] { new PointF(5, 17), new PointF(8, 10), new PointF(15, 3), new PointF(20, 8), new PointF(13, 15) });
            graphics.DrawLine(pen, 8, 10, 13, 15);
            graphics.DrawLine(pen, 5, 21, 19, 21);
            graphics.DrawLine(pen, 16, 17, 18, 15);
            graphics.DrawLine(pen, 20, 18, 22, 18);
        }
        else if (Glyph is UiGlyph.Link or UiGlyph.Unlink)
        {
            graphics.TranslateTransform(12, 12);
            graphics.RotateTransform(-35);
            graphics.DrawEllipse(pen, -10, -3.5f, 12, 7);
            graphics.DrawEllipse(pen, -2, -3.5f, 12, 7);
            if (Glyph == UiGlyph.Unlink)
            {
                using var erase = new Pen(BackColor, 4);
                graphics.DrawLine(erase, 0, -5, 0, 5);
                graphics.DrawLine(pen, -3, -6, -5, -9);
                graphics.DrawLine(pen, 3, 6, 5, 9);
            }
        }
        else
        {
            graphics.DrawRectangle(pen, 4, 4, 16, 16);
            if (Glyph == UiGlyph.Grid)
            {
                graphics.DrawLine(pen, 12, 4, 12, 20);
                graphics.DrawLine(pen, 4, 12, 20, 12);
            }
            else
            {
                graphics.DrawLine(pen, 8, 12, 16, 12);
                graphics.DrawLine(pen, 12, 8, 12, 16);
            }
        }
        graphics.Restore(state);
    }
}

internal static class DialogUi
{
    internal static Color FreezeColor => SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(80, 170, 255);
    internal static Color SelectedModeColor => SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(0, 85, 160);

    internal static void DrawFreezeGlyph(Graphics graphics, Pen pen, bool unfreeze)
    {
        graphics.DrawRectangle(pen, 6, 11, 12, 10);
        graphics.DrawArc(pen, unfreeze ? 12 : 8, 3, 8, 14, 180, 180);
        graphics.DrawLine(pen, 12, 15, 12, 18);
    }

    internal static GlyphButton Icon(Form owner, ToolTip tooltip, UiGlyph glyph, string name, string description)
    {
        float dpi = owner.DeviceDpi / 96f;
        var button = new GlyphButton
        {
            Glyph = glyph, Text = string.Empty, FlatStyle = FlatStyle.Flat,
            Size = new Size((int)(34 * dpi), (int)(34 * dpi)),
            Margin = new Padding(0, 0, (int)(4 * dpi), (int)(4 * dpi)),
            AccessibleName = name, AccessibleDescription = description
        };
        tooltip.SetToolTip(button, description);
        return button;
    }

    internal static void CompactHelp(Form owner, FlowLayoutPanel panel, ToolTip tooltip)
    {
        foreach (Label label in panel.Controls.OfType<Label>().Where(label =>
            Equals(label.Tag, "hint") || label.Text.Contains('\n') || label.Font.Italic).ToArray())
        {
            int index = panel.Controls.GetChildIndex(label);
            string description = label.Text;
            var info = Icon(owner, tooltip, UiGlyph.Info, "More information", description);
            info.Name = "controlInfo";
            info.Size = new Size((int)(24 * owner.DeviceDpi / 96f), (int)(24 * owner.DeviceDpi / 96f));
            info.Click += (_, _) => MessageBox.Show(owner, description, "Control information", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Control? header = index >= 2 && panel.Controls[index - 1] is TrackBar
                ? panel.Controls[index - 2] : null;
            if (header is Panel row && row.Controls.OfType<Label>().FirstOrDefault() is Label caption)
            {
                info.Location = new Point(caption.Right + 4, 0);
                info.Size = new Size(row.Height, row.Height);
                row.Controls.Add(info);
                info.BringToFront();
                tooltip.SetToolTip(panel.Controls[index - 1], description);
            }
            else if (index > 0 && panel.Controls[index - 1] is CheckBox checkbox)
            {
                var line = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
                panel.Controls.Add(line);
                panel.Controls.SetChildIndex(line, index - 1);
                line.Controls.Add(checkbox);
                line.Controls.Add(info);
                info.AccessibleName = checkbox.Text + " information";
                tooltip.SetToolTip(checkbox, description);
            }
            else
            {
                panel.Controls.Add(info);
                panel.Controls.SetChildIndex(info, index);
            }
            panel.Controls.Remove(label);
            label.Dispose();
        }
    }

    internal static void AddControlInfo(Form owner, Control control, ToolTip tooltip, string description)
    {
        var info = Icon(owner, tooltip, UiGlyph.Info, control.AccessibleName + " information", description);
        info.Name = "controlInfo";
        info.Click += (_, _) => MessageBox.Show(owner, description, control.AccessibleName ?? "Control information",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        if (control.Parent is FlowLayoutPanel parent)
        {
            int index = parent.Controls.GetChildIndex(control);
            if (control is CheckBox)
            {
                info.Size = new Size((int)(24 * owner.DeviceDpi / 96f), (int)(24 * owner.DeviceDpi / 96f));
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
                parent.Controls.Add(row);
                parent.Controls.SetChildIndex(row, index);
                row.Controls.Add(control);
                row.Controls.Add(info);
            }
            else if (index > 0 && parent.Controls[index - 1] is Panel header &&
                header.Controls.OfType<Label>().FirstOrDefault() is Label caption &&
                !header.Controls.OfType<GlyphButton>().Any())
            {
                info.Size = new Size(header.Height, header.Height);
                info.Location = new Point(caption.Right + 4, 0);
                header.Controls.Add(info);
                info.BringToFront();
            }
            else if (index > 0 && parent.Controls[index - 1] is Label label)
            {
                info.Size = new Size((int)(24 * owner.DeviceDpi / 96f), (int)(24 * owner.DeviceDpi / 96f));
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
                parent.Controls.Add(row);
                parent.Controls.SetChildIndex(row, index - 1);
                row.Controls.Add(label);
                row.Controls.Add(info);
            }
            else
            {
                parent.Controls.Add(info);
                parent.Controls.SetChildIndex(info, index);
            }
        }
        tooltip.SetToolTip(control, description);
    }

    internal static void ApplyTheme(Control parent, Control canvas, bool dark)
    {
        bool contrast = SystemInformation.HighContrast;
        Color background = contrast ? SystemColors.Control : dark ? Color.FromArgb(45, 45, 48) : Color.FromArgb(245, 245, 245);
        Color foreground = contrast ? SystemColors.ControlText : dark ? Color.FromArgb(235, 235, 235) : Color.FromArgb(30, 30, 30);
        foreach (Control control in parent.Controls)
        {
            if (control is Button { Tag: int }) continue; // Grid color swatches keep their actual colors.
            control.ForeColor = foreground;
            control.BackColor = control == canvas
                ? contrast ? SystemColors.Window : dark ? Color.FromArgb(48, 48, 48) : Color.FromArgb(215, 215, 215)
                : control is Button or ComboBox or NumericUpDown
                    ? contrast ? SystemColors.Control : dark ? Color.FromArgb(60, 60, 65) : Color.White
                    : background;
            if (control is Button button)
                button.FlatAppearance.BorderColor = contrast ? SystemColors.ControlText
                    : dark ? Color.FromArgb(105, 105, 115) : Color.FromArgb(155, 155, 165);
            if (control is GlyphButton { IsSelected: true } ||
                control is Label { Name: "pointModeStatus", Tag: true })
            {
                control.BackColor = SelectedModeColor;
                control.ForeColor = contrast ? SystemColors.HighlightText : Color.White;
                if (control is Button selected)
                    selected.FlatAppearance.BorderColor = contrast ? SystemColors.HighlightText : Color.White;
            }
            ApplyTheme(control, canvas, dark);
            control.Invalidate();
        }
    }

    internal static FlowLayoutPanel AddChrome(Form owner, FlowLayoutPanel settings, Control canvas, ToolTip tooltip, params Control[] actions)
    {
        int padding = (int)(6 * owner.DeviceDpi / 96f);
        var top = new FlowLayoutPanel { Name = "commonSettingsBar", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(padding) };
        var collapse = Icon(owner, tooltip, UiGlyph.Collapse, "Collapse settings", "Collapse the settings panel; editing and action buttons remain available.");
        collapse.Name = "collapseSettings";
        bool collapsed = false;
        collapse.Click += (_, _) =>
        {
            collapsed = !collapsed;
            settings.Visible = !collapsed;
            collapse.Glyph = collapsed ? UiGlyph.Expand : UiGlyph.Collapse;
            collapse.AccessibleName = collapsed ? "Expand settings" : "Collapse settings";
            collapse.AccessibleDescription = collapsed
                ? "Expand the settings panel; editing and action buttons remain available."
                : "Collapse the settings panel; editing and action buttons remain available.";
            tooltip.SetToolTip(collapse, collapse.AccessibleDescription);
            collapse.Invalidate();
        };
        top.Controls.Add(collapse);
        var footer = new FlowLayoutPanel
        {
            Name = "actionsBar", Dock = DockStyle.Bottom, AutoSize = true,
            WrapContents = true, Padding = new Padding(padding)
        };
        foreach (Control action in actions)
        {
            if (action is Button button)
            {
                button.AutoSize = false;
                button.Height = (int)(34 * owner.DeviceDpi / 96f);
                button.MinimumSize = new Size(button.MinimumSize.Width, button.Height);
            }
            footer.Controls.Add(action);
        }
        CompactHelp(owner, settings, tooltip);
        owner.Controls.Add(top);
        owner.Controls.Add(footer);
        return top;
    }
}
