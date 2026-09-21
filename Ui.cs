using System.Drawing.Drawing2D;
using System.Globalization;
using System.Reflection;

namespace DiskLit;

internal static class Humanize
{
    static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Number(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    public static string Bytes(long value)
    {
        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.CurrentCulture, $"{size:0.##} {Units[unit]}");
    }

    public static string Version
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            var raw = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "1.0.0";
            var plus = raw.IndexOf('+');
            return plus < 0 ? raw : raw[..plus];
        }
    }
}

internal static class Draw
{
    static readonly Dictionary<int, SolidBrush> brushes = [];
    static readonly Dictionary<(int Color, float Width), Pen> pens = [];
    static readonly Dictionary<(string Family, float Size), Font> fonts = [];

    public static SolidBrush Brush(Color color)
    {
        var key = color.ToArgb();
        if (!brushes.TryGetValue(key, out var brush)) brushes[key] = brush = new SolidBrush(color);
        return brush;
    }

    public static Pen Line(Color color, float width = 1f)
    {
        var key = (color.ToArgb(), width);
        if (!pens.TryGetValue(key, out var pen)) pens[key] = pen = new Pen(color, width);
        return pen;
    }

    public static Font Text(string family, float size)
    {
        var key = (family, size);
        if (!fonts.TryGetValue(key, out var font)) fonts[key] = font = new Font(family, size);
        return font;
    }

    public static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(1, radius * 2);
        if (bounds.Width <= diameter || bounds.Height <= diameter)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Padding = new Padding(16, 14, 16, 14);
    }

    public int Radius { get; set; } = 10;

    public Color Fill { get; set; } = Color.White;

    public Color Edge { get; set; } = Color.Gainsboro;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Draw.RoundedRectangle(bounds, Radius);
        using var fill = new SolidBrush(Fill);
        using var edge = new Pen(Edge);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(edge, path);
    }
}

internal sealed class SegmentedBar : Control
{
    IReadOnlyList<(Color Color, long Value)> segments = [];

    public SegmentedBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 8;
    }

    public Color Track { get; set; } = Color.Gainsboro;

    public long Capacity { get; private set; }

    public void SetSegments(IReadOnlyList<(Color Color, long Value)> values, long capacity)
    {
        segments = values;
        Capacity = capacity;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        var radius = Math.Max(2, Height / 2);
        using var path = Draw.RoundedRectangle(bounds, radius);
        using (var track = new SolidBrush(Track)) e.Graphics.FillPath(track, path);

        if (Capacity <= 0 || segments.Count == 0) return;

        var previous = e.Graphics.Clip;
        e.Graphics.SetClip(path);

        float x = 0;
        foreach (var (color, value) in segments)
        {
            if (value <= 0) continue;
            var width = (float)value / Capacity * Width;
            using var brush = new SolidBrush(color);
            e.Graphics.FillRectangle(brush, x, 0, width + 0.5f, Height);
            x += width;
        }

        e.Graphics.Clip = previous;
    }
}

internal sealed class Swatch : Control
{
    public Swatch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(10, 10);
    }

    public Color Color { get; set; } = Color.Gray;

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Color);
        e.Graphics.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
    }
}

internal abstract class Page : Panel
{
    bool busy;

    protected Page()
    {
        Dock = DockStyle.Fill;
        Visible = false;
        Padding = new Padding(22, 18, 22, 12);
    }

    public event EventHandler? BusyChanged;

    public bool Busy
    {
        get => busy;
        protected set
        {
            if (busy == value) return;
            busy = value;
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public abstract void ApplyLanguage();

    public abstract void ApplyTheme(Palette palette);

    public virtual void Activated() { }

    public virtual void Deactivated() { }

    public virtual void CancelWork() { }

    protected static ThemedButton FlatButton() => new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(16, 6, 16, 6),
        Margin = new Padding(0, 0, 8, 0)
    };

    protected static void Skin(ThemedButton button, Color back, Color fore, Palette palette) =>
        button.Recolor(back, fore, palette);

    protected static Label SectionLabel() => new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI Semibold", 8f),
        Margin = new Padding(0, 0, 0, 6)
    };

    protected static ThemedListView DetailList(bool virtualMode) => new() { VirtualMode = virtualMode };
}

internal sealed class ThemedListView : ListView
{
    const int LvmFirst = 0x1000;
    const int LvmSetExtendedListViewStyle = LvmFirst + 54;
    const int LvsExDoubleBuffer = 0x00010000;
    const int WmNcCalcSize = 0x0083;
    const int SbHorz = 0;

    public ThemedListView()
    {
        View = View.Details;
        FullRowSelect = true;
        GridLines = false;
        BorderStyle = BorderStyle.None;
        HeaderStyle = ColumnHeaderStyle.Nonclickable;
        Dock = DockStyle.Fill;
        OwnerDraw = true;
        DrawColumnHeader += PaintHeader;
        DrawItem += PaintRow;
        DrawSubItem += PaintCell;
    }

    public bool HideHorizontalScrollBar { get; set; }

    public Func<int, string?>? SectionAt { get; set; }

    int hotIndex = -1;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTest(e.Location).Item?.Index ?? -1;
        if (hit == hotIndex) return;
        var previous = hotIndex;
        hotIndex = hit;
        Redraw(previous);
        Redraw(hotIndex);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hotIndex < 0) return;
        var previous = hotIndex;
        hotIndex = -1;
        Redraw(previous);
    }

    void Redraw(int index)
    {
        var count = VirtualMode ? VirtualListSize : Items.Count;
        if (index < 0 || index >= count) return;
        try
        {
            RedrawItems(index, index, false);
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    void PaintRow(object? sender, DrawListViewItemEventArgs e)
    {
        var title = SectionAt?.Invoke(e.ItemIndex);
        if (title is null) return;

        var palette = Theme.Current;
        e.Graphics.FillRectangle(Draw.Brush(palette.Rail), e.Bounds);
        e.Graphics.DrawLine(Draw.Line(palette.Border), e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        e.Graphics.FillRectangle(Draw.Brush(palette.Accent), e.Bounds.Left + 4, e.Bounds.Top + 5, 3, e.Bounds.Height - 10);

        var text = new Rectangle(e.Bounds.Left + 14, e.Bounds.Top, e.Bounds.Width - 18, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, title, Draw.Text("Segoe UI Semibold", 9f), text, palette.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    void PaintCell(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (SectionAt?.Invoke(e.ItemIndex) is not null) return;

        var palette = Theme.Current;
        var selected = e.Item?.Selected == true;
        var hovered = e.ItemIndex == hotIndex;
        var background = selected ? palette.Selection : hovered ? palette.MenuHighlight : palette.Surface;

        e.Graphics.FillRectangle(Draw.Brush(background), e.Bounds);

        var bounds = e.Bounds;
        if (e.ColumnIndex == 0 && SmallImageList is not null && e.Item is { ImageIndex: >= 0 } item
            && item.ImageIndex < SmallImageList.Images.Count)
        {
            var size = SmallImageList.ImageSize;
            var top = bounds.Top + Math.Max(0, (bounds.Height - size.Height) / 2);
            e.Graphics.DrawImage(SmallImageList.Images[item.ImageIndex], bounds.Left + 3, top, size.Width, size.Height);
            bounds = new Rectangle(bounds.Left + size.Width + 7, bounds.Top, Math.Max(0, bounds.Width - size.Width - 10), bounds.Height);
        }
        else
        {
            bounds = new Rectangle(bounds.Left + 5, bounds.Top, Math.Max(0, bounds.Width - 9), bounds.Height);
        }

        var foreground = palette.Text;
        if (!selected && e.SubItem is { } sub && sub.ForeColor.A > 0 && sub.ForeColor != SystemColors.WindowText)
            foreground = sub.ForeColor;

        var alignment = e.ColumnIndex < Columns.Count ? Columns[e.ColumnIndex].TextAlign : HorizontalAlignment.Left;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
            | alignment switch
            {
                HorizontalAlignment.Right => TextFormatFlags.Right,
                HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
                _ => TextFormatFlags.Left
            };

        TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", e.SubItem?.Font ?? Font, bounds, foreground, flags);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SendMessage(Handle, LvmSetExtendedListViewStyle, LvsExDoubleBuffer, LvsExDoubleBuffer);
        Theme.ApplyNativeListStyle(this);
    }

    protected override void WndProc(ref Message m)
    {
        if (HideHorizontalScrollBar && m.Msg == WmNcCalcSize && IsHandleCreated)
            ShowScrollBar(Handle, SbHorz, false);
        base.WndProc(ref m);
    }

    static void PaintHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        var palette = Theme.Current;
        e.Graphics.FillRectangle(Draw.Brush(palette.Surface), e.Bounds);

        var separator = Draw.Line(palette.Border);
        e.Graphics.DrawLine(separator, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        if (e.ColumnIndex > 0)
            e.Graphics.DrawLine(separator, e.Bounds.Left, e.Bounds.Top + 5, e.Bounds.Left, e.Bounds.Bottom - 6);

        var alignment = e.Header?.TextAlign ?? HorizontalAlignment.Left;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
            | alignment switch
            {
                HorizontalAlignment.Right => TextFormatFlags.Right,
                HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
                _ => TextFormatFlags.Left
            };

        var bounds = Rectangle.Inflate(e.Bounds, -8, 0);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", e.Font, bounds, palette.MutedText, flags);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    static extern IntPtr SendMessage(IntPtr window, int message, int wparam, int lparam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    static extern bool ShowScrollBar(IntPtr window, int bar, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool show);
}

internal class ThemedButton : Button
{
    bool hot;
    bool pressed;

    public ThemedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
    }

    public Color Normal { get; private set; } = Color.Transparent;

    public Color Hover { get; private set; } = Color.Transparent;

    public Color Pressed { get; private set; } = Color.Transparent;

    public Color Foreground { get; private set; } = Color.Black;

    public Color Disabled { get; private set; } = Color.Gray;

    protected Color ContentColor => Enabled ? Foreground : Disabled;

    public void Recolor(Color back, Color fore, Palette palette)
    {
        Normal = back;
        Hover = ControlPaint.Light(back, 0.25f);
        Pressed = ControlPaint.Dark(back, 0.08f);
        Foreground = fore;
        Disabled = palette.MutedText;
        BackColor = back;
        ForeColor = fore;
        Invalidate();
    }

    public void RecolorRail(Palette palette, bool emphasised)
    {
        Normal = palette.Rail;
        Hover = palette.RailSelected;
        Pressed = palette.Subtle;
        Foreground = emphasised ? palette.Text : palette.MutedText;
        Disabled = palette.MutedText;
        BackColor = palette.Rail;
        ForeColor = Foreground;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hot = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hot = false;
        pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var back = !Enabled ? Normal : pressed ? Pressed : hot ? Hover : Normal;
        e.Graphics.Clear(back);
        PaintContent(e);

        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
            | TextAlign switch
            {
                ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft => TextFormatFlags.Left,
                ContentAlignment.MiddleRight or ContentAlignment.TopRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
                _ => TextFormatFlags.HorizontalCenter
            };

        var bounds = new Rectangle(Padding.Left, Padding.Top,
            Width - Padding.Horizontal, Height - Padding.Vertical);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, Enabled ? Foreground : Disabled, flags);
    }

    protected virtual void PaintContent(PaintEventArgs e)
    {
    }
}

internal sealed class GearButton : ThemedButton
{
    protected override void PaintContent(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var size = Math.Min(18f, Math.Min(Width, Height) - 8f);
        var center = new PointF(Width / 2f, Height / 2f);
        var outer = size / 2f;
        var inner = outer * 0.66f;

        using var pen = new Pen(ContentColor, Math.Max(1.6f, size / 10f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        for (var index = 0; index < 8; index++)
        {
            var angle = index * MathF.PI / 4f;
            var cosine = MathF.Cos(angle);
            var sine = MathF.Sin(angle);
            e.Graphics.DrawLine(pen,
                center.X + cosine * inner, center.Y + sine * inner,
                center.X + cosine * outer, center.Y + sine * outer);
        }

        var ring = new RectangleF(center.X - inner, center.Y - inner, inner * 2f, inner * 2f);
        var hole = new RectangleF(center.X - size * 0.14f, center.Y - size * 0.14f, size * 0.28f, size * 0.28f);
        e.Graphics.DrawEllipse(pen, ring);
        e.Graphics.DrawEllipse(pen, hole);
    }
}

internal sealed class DropDown : Control
{
    readonly List<object> items = [];
    int selectedIndex = -1;
    int updateDepth;
    bool hot;
    ToolStripDropDown? popup;
    ToolStripControlHost? host;
    ListBox? list;
    bool open;

    public DropDown()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Height = 30;
        TabStop = true;
    }

    public event EventHandler? SelectedIndexChanged;

    public int ItemHeight { get; set; } = 24;

    public int MaxVisibleItems { get; set; } = 14;

    public IReadOnlyList<object> Items => items;

    public object? SelectedItem => selectedIndex >= 0 && selectedIndex < items.Count ? items[selectedIndex] : null;

    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            var clamped = items.Count == 0 ? -1 : Math.Clamp(value, -1, items.Count - 1);
            if (clamped == selectedIndex) return;
            selectedIndex = clamped;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void BeginUpdate() => updateDepth++;

    public void EndUpdate()
    {
        if (updateDepth > 0) updateDepth--;
        if (updateDepth == 0) Invalidate();
    }

    public void Add(object item)
    {
        items.Add(item);
        if (updateDepth == 0) Invalidate();
    }

    public void Replace(int index, object item)
    {
        if (index < 0 || index >= items.Count) return;
        items[index] = item;
        if (updateDepth == 0) Invalidate();
    }

    public void Clear()
    {
        items.Clear();
        selectedIndex = -1;
        if (updateDepth == 0) Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hot = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hot = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled || items.Count == 0) return;
        Focus();
        Open();
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.Space or Keys.Enter;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Enabled || items.Count == 0) return;

        if (e.KeyCode is Keys.Space or Keys.Enter) Open();
        else if (e.KeyCode == Keys.Down) SelectedIndex = Math.Min(items.Count - 1, selectedIndex + 1);
        else if (e.KeyCode == Keys.Up) SelectedIndex = Math.Max(0, selectedIndex - 1);
    }

    void Open()
    {
        if (open || items.Count == 0) return;

        EnsurePopup();
        if (popup is null || list is null) return;

        var palette = Theme.Current;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var item in items) list.Items.Add(item);
        list.EndUpdate();

        list.Font = Font;
        list.ItemHeight = ItemHeight;
        list.BackColor = palette.Surface;
        list.ForeColor = palette.Text;
        list.Width = Math.Max(40, Width - 2);
        list.Height = Math.Max(ItemHeight, Math.Min(items.Count, MaxVisibleItems) * ItemHeight);
        list.SelectedIndex = selectedIndex;

        host!.Size = list.Size;
        popup.BackColor = palette.Border;
        popup.Size = new Size(list.Width + 2, list.Height + 2);

        open = true;
        popup.Show(this, new Point(0, Height));
        list.Focus();
    }

    void EnsurePopup()
    {
        if (popup is not null) return;

        list = new ListBox
        {
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            IntegralHeight = false
        };
        list.DrawItem += PaintPopupItem;
        list.Click += (_, _) => Commit();
        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) Commit();
            else if (e.KeyCode == Keys.Escape) popup?.Close();
        };

        host = new ToolStripControlHost(list)
        {
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AutoSize = false
        };

        popup = new ToolStripDropDown
        {
            Margin = Padding.Empty,
            Padding = new Padding(1),
            AutoSize = false,
            DropShadowEnabled = true
        };
        popup.Items.Add(host);
        popup.Closed += (_, _) =>
        {
            open = false;
            Invalidate();
        };
    }

    void Commit()
    {
        var chosen = list?.SelectedIndex ?? -1;
        popup?.Close();
        if (chosen >= 0) SelectedIndex = chosen;
    }

    static void PaintPopupItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ListBox list) return;
        var palette = Theme.Current;
        var highlighted = (e.State & DrawItemState.Selected) != 0;

        using var background = new SolidBrush(highlighted ? palette.Selection : palette.Surface);
        e.Graphics.FillRectangle(background, e.Bounds);
        if (e.Index < 0 || e.Index >= list.Items.Count) return;

        var bounds = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top, e.Bounds.Width - 12, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, list.Items[e.Index]?.ToString(), e.Font ?? list.Font, bounds, palette.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var palette = Theme.Current;
        e.Graphics.Clear(palette.Surface);

        var border = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var pen = new Pen(hot && Enabled ? palette.Accent : palette.Border))
            e.Graphics.DrawRectangle(pen, border);

        var arrowWidth = 22;
        var text = new Rectangle(9, 0, Math.Max(0, Width - arrowWidth - 12), Height);
        TextRenderer.DrawText(e.Graphics, SelectedItem?.ToString() ?? "", Font, text,
            Enabled ? palette.Text : palette.MutedText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var centerX = Width - arrowWidth / 2 - 4;
        var centerY = Height / 2;
        using var chevron = new Pen(Enabled ? palette.MutedText : palette.Border, 1.6f);
        Point[] points =
        [
            new(centerX - 4, centerY - 2),
            new(centerX, centerY + 2),
            new(centerX + 4, centerY - 2)
        ];
        e.Graphics.DrawLines(chevron, points);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            popup?.Close();
            popup?.Dispose();
            popup = null;
            host = null;
            list = null;
        }
        base.Dispose(disposing);
    }
}

internal sealed class NavButton : ThemedButton
{
    bool active;

    public NavButton()
    {
        AutoSize = false;
        Height = 42;
        Dock = DockStyle.Top;
        TextAlign = ContentAlignment.MiddleLeft;
        Padding = new Padding(18, 0, 8, 0);
        Margin = new Padding(0);
    }

    public bool Active
    {
        get => active;
        set
        {
            active = value;
            Restyle();
        }
    }

    public Palette Palette { get; set; } = Theme.Current;

    public void Restyle()
    {
        RecolorRail(Palette, active);
        if (!active) return;
        Recolor(Palette.RailSelected, Palette.Text, Palette);
    }

    public bool Busy { get; set; }

    public int BusyPhase { get; set; }

    protected override void PaintContent(PaintEventArgs e)
    {
        if (active)
        {
            using var marker = new SolidBrush(Palette.Accent);
            e.Graphics.FillRectangle(marker, 0, 9, 3, Height - 18);
        }

        if (!Busy) return;

        const int trackHeight = 3;
        var trackWidth = Width - 22;
        var top = Height - trackHeight - 5;
        using var track = new SolidBrush(Palette.Subtle);
        e.Graphics.FillRectangle(track, 14, top, trackWidth, trackHeight);

        var sweep = Math.Max(20, trackWidth / 3);
        var travel = trackWidth + sweep;
        var offset = BusyPhase % travel - sweep;
        var left = Math.Max(14, 14 + offset);
        var right = Math.Min(14 + trackWidth, 14 + offset + sweep);
        if (right <= left) return;

        using var bar = new SolidBrush(Palette.Accent);
        e.Graphics.FillRectangle(bar, left, top, right - left, trackHeight);
    }
}
