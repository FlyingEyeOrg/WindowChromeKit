using System.Drawing;
using System.Windows.Forms;
using WindowChromeKit.WinForms;

namespace WindowChromeKit.WinForms.Sample;

/// <summary>
/// 自定义标题栏示例：标题栏里放一个真实菜单（<see cref="ChromeForm.TitleBarContent"/>）
/// 和一个按钮（<see cref="ChromeForm.TitleBarActions"/>），两者都用
/// <see cref="ChromeForm.SetHitTestRole"/> 标记为可交互内容；勾选"完全自绘"后
/// 连默认标题栏都不画，全部由 <see cref="ChromeForm.OnPaintTitleBar"/> 负责。
/// </summary>
public sealed class CustomTitleBarForm : ChromeForm
{
    private readonly MenuStrip _menu;
    private readonly Panel _contentHost;
    private readonly Button _actionButton;
    private readonly CheckBox _fullyCustomCheck;
    private readonly CheckBox _windows7IconCheck;

    public CustomTitleBarForm()
    {
        Text = "自定义标题栏（WinForms）";
        // 库内嵌的两个可选图标：Windows 10 / Windows 7 风格，直接赋给 Form.Icon
        Icon = WindowChromeIcons.Windows10;
        Width = 920;
        Height = 560;
        MinimumSize = new Size(460, 260);
        StartPosition = FormStartPosition.CenterScreen;

        // 标题栏操作：右对齐、紧挨三个窗口按钮
        _actionButton = new Button
        {
            Text = "标题栏按钮",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0xE8, 0xF0, 0xFE),
            ForeColor = Color.FromArgb(0x17, 0x4E, 0xA6),
        };
        _actionButton.FlatAppearance.BorderColor = Color.FromArgb(0x1A, 0x73, 0xE8);
        _actionButton.Click += (_, _) => MessageBox.Show(this, "标题栏按钮被点击了。", "WinForms 标题栏");
        ChromeForm.SetHitTestRole(_actionButton, ChromeHitTestRole.Client);
        TitleBarActions = _actionButton;

        var info = new Label
        {
            Dock = DockStyle.Top,
            Height = 150,
            Padding = new Padding(20, 16, 20, 0),
            Text =
                "标题栏完全自定义（WinForms）\r\n"
                + "・「文件 / 视图 / 帮助」是真实的 MenuStrip，放在 TitleBarContent 插槽里，\r\n"
                + "  用 SetHitTestRole(menu, Client) 标记后可交互，不会被当成窗口拖动区。\r\n"
                + "・右侧「标题栏按钮」放在 TitleBarActions 插槽，紧挨最小化/最大化/关闭。\r\n"
                + "・勾选下面的“完全自绘标题栏”，ShowDefaultTitleBar 关闭，\r\n"
                + "  底色、顶边线和文字全部由 OnPaintTitleBar 绘制，按钮状态机仍然可用。\r\n"
                + "・把鼠标移到窗口外侧边缘（阴影里那一圈）可缩放；拖标题栏空白处可移动。\r\n"
                + "・图标来自库内嵌资源：WindowChromeIcons.Windows10 / Windows7，勾选下面的开关可切换。",
        };

        _fullyCustomCheck = new CheckBox
        {
            Text = "完全自绘标题栏（ShowDefaultTitleBar = false）",
            AutoSize = true,
            Margin = new Padding(0, 0, 24, 0),
        };
        _fullyCustomCheck.CheckedChanged += (_, _) =>
        {
            ShowDefaultTitleBar = !_fullyCustomCheck.Checked;
            Invalidate();
        };

        // 库内嵌的两个图标可以随时切换（标题栏 / 任务栏 / Alt+Tab 一起变）
        _windows7IconCheck = new CheckBox
        {
            Text = "使用 Windows 7 风格图标（否则用 Windows 10 风格）",
            AutoSize = true,
        };
        _windows7IconCheck.CheckedChanged += (_, _) =>
            Icon = _windows7IconCheck.Checked ? WindowChromeIcons.Windows7 : WindowChromeIcons.Windows10;

        // 标题栏菜单：放进内容插槽，标记为可交互
        _menu = new MenuStrip
        {
            Dock = DockStyle.None,
            AutoSize = false,
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(0),
            // 默认标题栏是 VS Code 深色（#323233），菜单用浅色文字 + 扁平渲染器；
            // 系统渲染器会画渐变底和 3D 边框，在标题栏底部露出一条白边。
            BackColor = Color.FromArgb(0x32, 0x32, 0x33),
            ForeColor = Color.FromArgb(0xCC, 0xCC, 0xCC),
            Renderer = new TitleBarMenuRenderer(
                Color.FromArgb(0x32, 0x32, 0x33),
                Color.FromArgb(0xCC, 0xCC, 0xCC),
                Color.FromArgb(0x50, 0x50, 0x50)),
        };
        // 每个顶级菜单都要有下拉项，否则点开是个空菜单
        var fileMenu = new ToolStripMenuItem("文件");
        fileMenu.DropDownItems.Add(new ToolStripMenuItem(
            "再开一个自定义标题栏窗口",
            null,
            (_, _) => new CustomTitleBarForm { Owner = this }.Show()));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("关闭", null, (_, _) => Close()));

        var viewMenu = new ToolStripMenuItem("视图");
        var customItem = new ToolStripMenuItem("完全自绘标题栏");
        customItem.Click += (_, _) => _fullyCustomCheck.Checked = !_fullyCustomCheck.Checked;
        var iconItem = new ToolStripMenuItem("使用 Windows 7 风格图标");
        iconItem.Click += (_, _) => _windows7IconCheck.Checked = !_windows7IconCheck.Checked;
        viewMenu.DropDownItems.Add(customItem);
        viewMenu.DropDownItems.Add(iconItem);

        var helpMenu = new ToolStripMenuItem("帮助");
        helpMenu.DropDownItems.Add(new ToolStripMenuItem(
            "关于",
            null,
            (_, _) => MessageBox.Show(
                this,
                "标题栏里的菜单就是普通的 MenuStrip，放在 TitleBarContent 插槽并用 SetHitTestRole 标记为 Client。",
                "关于")));

        _menu.Items.Add(fileMenu);
        _menu.Items.Add(viewMenu);
        _menu.Items.Add(helpMenu);
        ChromeForm.SetHitTestRole(_menu, ChromeHitTestRole.Client);
        // 插槽铺满标题栏；只有菜单自己标记为可交互，插槽里其余空白仍可拖动窗口
        // （与 WPF 的 TitleBarContent + HitTestRole=Client 语义一致）。
        _contentHost = new Panel { BackColor = Color.Transparent, Height = 39 };
        _contentHost.Controls.Add(_menu);
        _contentHost.SizeChanged += (_, _) => LayoutMenu();
        TitleBarContent = _contentHost;

        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(20, 4, 0, 0),
        };
        options.Controls.Add(_fullyCustomCheck);
        options.Controls.Add(_windows7IconCheck);

        // 用表格布局分行，避免绝对坐标被 Dock 的标签盖住
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 168));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.Controls.Add(info, 0, 0);
        layout.Controls.Add(options, 0, 1);

        Controls.Add(layout);
    }

    /// <summary>
    /// 菜单按内容宽度摆放，插槽留出一小段未标记的空白：库会给未标记的插槽控件
    /// 挂上"按下即拖动窗口"的处理，所以插槽里的空白同样能拖窗口。
    /// </summary>
    private void LayoutMenu()
    {
        var preferred = _menu.PreferredSize;
        _contentHost.Size = new Size(preferred.Width + 24, _contentHost.Height);
        _menu.SetBounds(0, 0, preferred.Width, _contentHost.ClientSize.Height);
        // 顶级菜单项撑满标题栏高度：命中区与悬停高亮覆盖整条，点标题栏里任意高度都能展开
        var itemHeight = Math.Max(1, CaptionHeight - 1);
        foreach (ToolStripItem item in _menu.Items)
        {
            item.AutoSize = false;
            item.Height = itemHeight;
            item.Width = item.GetPreferredSize(Size.Empty).Width;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        LayoutMenu();
    }

    private void InitializeComponent()
    {

    }

    /// <summary>
    /// 标题栏菜单用的扁平渲染器：底色跟随标题栏、去掉系统渲染器的渐变与 3D 边框，
    /// 悬停/打开时用标题栏按钮同款的悬停色。
    /// </summary>
    private sealed class TitleBarMenuRenderer : ToolStripRenderer
    {
        private readonly Color _background;
        private readonly Color _foreground;
        private readonly Color _hover;

        internal TitleBarMenuRenderer(Color background, Color foreground, Color hover)
        {
            _background = background;
            _foreground = foreground;
            _hover = hover;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(_background);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // 不要 3D 边框：它会在标题栏底部画出一条亮线
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && !e.Item.Pressed)
                return;
            using var brush = new SolidBrush(_hover);
            e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var y = e.Item.Height / 2;
            using var pen = new Pen(_hover);
            e.Graphics.DrawLine(pen, 4, y, Math.Max(4, e.Item.Width - 6), y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected || e.Item.Pressed ? Color.White : _foreground;
            base.OnRenderItemText(e);
        }
    }

    protected override void OnPaintTitleBar(TitleBarPaintEventArgs e)
    {
        if (ShowDefaultTitleBar)
            return;
        // 完全自绘：渐变底色 + 自己画的标题与顶边线（标题栏里的插槽控件仍由 WinForms 绘制）
        var caption = e.CaptionBounds;
        using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                   caption,
                   Color.FromArgb(0x1A, 0x73, 0xE8),
                   Color.FromArgb(0x17, 0x4E, 0xA6),
                   System.Drawing.Drawing2D.LinearGradientMode.Horizontal))
        {
            e.Graphics.FillRectangle(brush, caption);
        }
        using (var pen = new Pen(Color.FromArgb(0x0B, 0x57, 0xD0)))
        {
            e.Graphics.DrawLine(pen, caption.Left, caption.Bottom - 1, caption.Right, caption.Bottom - 1);
        }
        // 标题居中绘制，避开左右内容：内容插槽是铺满的面板，真正的占用看菜单自身右边界，
        // 右侧用 TitleBarActionsBounds（库公开的插槽矩形）避让。
        var contentRight = _menu.Visible ? _menu.Right : TitleBarContentBounds.Right;
        var left = Math.Max(12, contentRight + 8);
        var right = TitleBarActionsBounds.IsEmpty ? caption.Right - 12 : TitleBarActionsBounds.Left - 8;
        if (right > left)
        {
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                new Rectangle(left, caption.Top, right - left, caption.Height),
                Color.White,
                TextFormatFlags.VerticalCenter
                    | TextFormatFlags.HorizontalCenter
                    | TextFormatFlags.SingleLine
                    | TextFormatFlags.EndEllipsis);
        }
    }
}
