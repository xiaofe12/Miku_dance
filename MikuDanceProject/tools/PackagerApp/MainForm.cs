using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MikuDancePackager;

internal sealed class MainForm : Form
{
    private const string DefaultPackageButtonText = "\u4E00\u952E\u6253\u5305";
    private const string DefaultPackagingStatusTitle = "\u5C31\u7EEA";
    private const string DefaultPackagingStatusDetail = "\u8BF7\u786E\u8BA4\u53C2\u6570\u540E\u5F00\u59CB\u6253\u5305\u3002";
    private const string LegacyDefaultMotionStartFrameText = "435";
    private const int ProgressLogTrimThreshold = 48000;
    private const int ProgressLogTrimTarget = 32000;

    private static readonly string UiStatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MikuDancePackager",
        "ui-state.json");

    private static readonly JsonSerializerOptions UiStateSerializerOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly Color WindowTopColor = Color.FromArgb(249, 244, 252);
    private static readonly Color WindowBottomColor = Color.FromArgb(238, 245, 251);
    private static readonly Color CardFillColor = Color.FromArgb(218, 255, 255, 255);
    private static readonly Color CardFillStrongColor = Color.FromArgb(235, 255, 255, 255);
    private static readonly Color CardBorderColor = Color.FromArgb(188, 228, 220, 238);
    private static readonly Color TitleColor = Color.FromArgb(52, 58, 80);
    private static readonly Color BodyColor = Color.FromArgb(102, 111, 136);
    private static readonly Color HintColor = Color.FromArgb(126, 136, 158);
    private static readonly Color LavenderAccent = Color.FromArgb(181, 194, 255);
    private static readonly Color MintAccent = Color.FromArgb(179, 225, 209);
    private static readonly Color PeachAccent = Color.FromArgb(255, 216, 191);
    private static readonly Color RoseAccent = Color.FromArgb(242, 201, 215);
    private static readonly Color PrimaryButtonColor = Color.FromArgb(113, 150, 246);
    private static readonly Color PrimaryButtonHoverColor = Color.FromArgb(99, 137, 236);
    private static readonly Color PrimaryButtonPressedColor = Color.FromArgb(88, 124, 220);
    private static readonly Color PrimaryButtonBorderColor = Color.FromArgb(97, 132, 221);

    private readonly TextBox _projectRootTextBox = CreateInputTextBox(readOnly: true);
    private readonly TextBox _bundlePathTextBox = CreateInputTextBox(readOnly: true);
    private readonly TextBox _motionStartFrameTextBox = CreateInputTextBox(readOnly: false, placeholderText: "0");
    private readonly TextBox _motionEndFrameTextBox = CreateInputTextBox(readOnly: false, placeholderText: "\u7559\u7A7A\u8868\u793A\u76F4\u5230\u52A8\u4F5C\u672B\u5C3E");
    private readonly TextBox _versionTextBox = CreateInputTextBox(readOnly: false);
    private readonly TextBox _outputDirectoryTextBox = CreateInputTextBox(readOnly: true);
    private readonly TextBox _iconPathTextBox = CreateInputTextBox(readOnly: true);
    private readonly TextBox _packagePreviewTextBox = CreateInputTextBox(readOnly: true);
    private readonly Label _packagingStatusTitleLabel = CreateStatusTitleLabel();
    private readonly Label _packagingStatusDetailLabel = CreateStatusDetailLabel();
    private readonly Label _packagingProgressPercentLabel = CreateStatusPercentLabel();
    private readonly ProgressBar _packagingProgressBar = CreatePackagingProgressBar();
    private readonly RichTextBox _packagingLogViewer = CreateProgressLogViewer();

    private readonly RichTextBox _manifestEditor = CreateEditor();
    private readonly RichTextBox _readmeEditor = CreateEditor();
    private readonly TabControl _editorTabs;

    private readonly RoundedButton _packageButton = new();
    private readonly RoundedButton _reloadContextButton = new();
    private readonly RoundedButton _refreshBundleButton = new();
    private readonly RoundedButton _reloadTemplatesButton = new();
    private readonly RoundedButton _openOutputButton = new();
    private readonly RoundedButton _manifestTabButton = new();
    private readonly RoundedButton _readmeTabButton = new();

    private readonly ToolTip _toolTip = new()
    {
        AutoPopDelay = 12000,
        InitialDelay = 300,
        ReshowDelay = 150,
        ShowAlways = true,
    };

    private bool _isSynchronizingManifestVersion;
    private int _currentPackagingProgressPercent;

    public MainForm()
    {
        _editorTabs = CreateEditorTabs();

        Text = "Miku Dance Packager";
        Width = 1440;
        Height = 900;
        MinimumSize = new Size(1180, 780);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);
        ForeColor = TitleColor;
        BackColor = WindowTopColor;
        FormBorderStyle = FormBorderStyle.Sizable;
        DoubleBuffered = true;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint,
            true);

        ConfigureButtons();
        WireEvents();
        FormClosing += (_, _) => SaveUiStateSafely();

        Controls.Add(CreateRootLayout());

        _motionStartFrameTextBox.Text = PackagingService.GetDefaultMotionStartFrame().ToString();
        RefreshPackagePreview();
        RefreshToolTips();
        LoadInitialState();
        RefreshEditorTabButtons();
        ResetPackagingProgressPresentation(clearLog: true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var backgroundBrush = new LinearGradientBrush(ClientRectangle, WindowTopColor, WindowBottomColor, 58f);
        e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);

        DrawGlow(e.Graphics, new Rectangle(-110, 30, 360, 260), Color.FromArgb(72, LavenderAccent));
        DrawGlow(e.Graphics, new Rectangle(Width - 340, 90, 290, 220), Color.FromArgb(68, PeachAccent));
        DrawGlow(e.Graphics, new Rectangle(Width / 2 - 180, Height - 250, 360, 220), Color.FromArgb(54, MintAccent));
    }

    private Control CreateRootLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(16, 14, 16, 14),
            Margin = new Padding(0),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        root.Controls.Add(CreateMainBody(), 0, 0);
        return root;
    }

    private Control CreateMainBody()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 516f));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        body.Controls.Add(CreateLeftColumn(), 0, 0);
        body.Controls.Add(CreateRightColumn(), 1, 0);
        return body;
    }

    private Control CreateLeftColumn()
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 6, 0),
        };
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        host.Controls.Add(CreateSettingsCard(), 0, 0);
        host.Controls.Add(CreateActionsCard(), 0, 1);
        host.Controls.Add(new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        }, 0, 2);

        return host;
    }

    private Control CreateRightColumn()
    {
        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(12, 0, 0, 0),
            Padding = new Padding(0),
        };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 240f));
        right.Controls.Add(CreateEditorCard(), 0, 0);
        right.Controls.Add(CreateProgressCard(), 0, 1);
        return right;
    }

    private Control CreateSettingsCard()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 8,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };

        content.Controls.Add(CreateFieldBlock("\u9879\u76EE\u76EE\u5F55", "\u81EA\u52A8\u5B9A\u4F4D\u5305\u542B MikuDanceProject.csproj \u7684\u76EE\u5F55\u3002", _projectRootTextBox, "\u9009\u62E9", SelectProjectRoot), 0, 0);
        content.Controls.Add(CreateFieldBlock("\u6700\u65B0 AB", "\u4F18\u5148\u4F7F\u7528\u6700\u8FD1\u4E00\u6B21 Unity \u8F93\u51FA\u7684 bundle\uff0c\u4E5F\u53EF\u624B\u52A8\u9009\u62E9\u3002", _bundlePathTextBox, "\u9009\u62E9", SelectBundle), 0, 1);
        content.Controls.Add(CreateFieldBlock(
            "\u52A8\u753B\u8D77\u59CB\u5E27",
            "\u9ED8\u8BA4 0\u3002\u6253\u5305\u65F6\u4F1A\u88C1\u526A\u52A8\u4F5C\u548C\u97F3\u9891\uFF0C\u5E76\u5199\u5165\u540C\u6B65\u5143\u6570\u636E\u3002",
            _motionStartFrameTextBox,
            "\u9ED8\u8BA4",
            ResetMotionStartFrame), 0, 2);
        content.Controls.Add(CreateFieldBlock(
            "\u52A8\u753B\u7ED3\u675F\u5E27",
            "\u7559\u7A7A\u8868\u793A\u76F4\u5230\u52A8\u4F5C\u672B\u5C3E\uFF1B\u586B\u5199\u540E\u5FC5\u987B\u5927\u4E8E\u8D77\u59CB\u5E27\u3002",
            _motionEndFrameTextBox,
            "\u6E05\u7A7A",
            ClearMotionEndFrame), 0, 3);
        content.Controls.Add(CreateFieldBlock("\u5F53\u524D\u7248\u672C", "\u4FEE\u6539\u540E\u4F1A\u540C\u6B65\u66F4\u65B0\u53F3\u4FA7 manifest.json \u7684 version_number\u3002", _versionTextBox, "\u8BFB\u53D6", LoadVersionFromProject), 0, 4);
        content.Controls.Add(CreateFieldBlock("\u8F93\u51FA\u76EE\u5F55", "\u6700\u7EC8\u6587\u4EF6\u5939\u5C06\u5BFC\u51FA\u5230\u8FD9\u91CC\u3002", _outputDirectoryTextBox, "\u9009\u62E9", SelectOutputDirectory), 0, 5);
        content.Controls.Add(CreateFieldBlock("\u56FE\u6807", "\u5BFC\u51FA\u65F6\u4F7F\u7528\u7684 icon.png\u3002", _iconPathTextBox, "\u9009\u62E9", SelectIcon), 0, 6);
        content.Controls.Add(CreateFieldBlock("\u6587\u4EF6\u5939\u9884\u89C8", "\u6839\u636E\u5F53\u524D\u5305\u540D\u548C\u7248\u672C\u53F7\u5B9E\u65F6\u751F\u6210\u3002", _packagePreviewTextBox, "\u6253\u5F00", OpenOutputDirectory), 0, 7);

        return CreateCard(
            "\u9879\u76EE\u4E0E\u8F93\u51FA",
            "\u5E38\u7528\u8DEF\u5F84\u548C\u7248\u672C\u96C6\u4E2D\u5728\u540C\u4E00\u5F20\u5361\u7247\u91CC\u3002",
            LavenderAccent,
            content,
            fillContent: false,
            bodyPadding: new Padding(14, 14, 14, 14),
            marginBottom: 10);
    }

    private Control CreateActionsCard()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        ConfigureActionButton(_reloadContextButton, "\u2699", "\u91CD\u65B0\u68C0\u6D4B", LavenderAccent);
        ConfigureActionButton(_refreshBundleButton, "\u21BB", "\u5237\u65B0 AB", MintAccent);
        ConfigureActionButton(_reloadTemplatesButton, "\u25A4", "\u91CD\u8F7D\u6A21\u677F", PeachAccent);
        ConfigureActionButton(_openOutputButton, "\u2197", "\u6253\u5F00\u8F93\u51FA\u76EE\u5F55", RoseAccent);

        grid.Controls.Add(_reloadContextButton, 0, 0);
        grid.Controls.Add(_refreshBundleButton, 1, 0);
        grid.Controls.Add(_reloadTemplatesButton, 0, 1);
        grid.Controls.Add(_openOutputButton, 1, 1);

        return CreateCard(
            "\u5FEB\u6377\u64CD\u4F5C",
            "\u4FDD\u7559\u5FC5\u8981\u64CD\u4F5C\uFF0C\u51CF\u5C11\u6765\u56DE\u5207\u6362\u3002",
            MintAccent,
            grid,
            fillContent: false,
            bodyPadding: new Padding(14, 12, 14, 14),
            marginBottom: 0);
    }

    private Control CreateEditorCard()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        content.Controls.Add(CreateEditorHeader(), 0, 0);
        content.Controls.Add(_editorTabs, 0, 1);

        return CreateCard(
            string.Empty,
            string.Empty,
            PeachAccent,
            content,
            fillContent: true,
            showHeader: false,
            bodyPadding: new Padding(10, 10, 10, 10),
            marginBottom: 0);
    }

    private Control CreateProgressCard()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var statusRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(0),
        };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var textHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        textHost.Controls.Add(_packagingStatusTitleLabel, 0, 0);
        textHost.Controls.Add(_packagingStatusDetailLabel, 0, 1);

        statusRow.Controls.Add(textHost, 0, 0);
        statusRow.Controls.Add(_packagingProgressPercentLabel, 1, 0);

        var logHost = new GlassPanel
        {
            Dock = DockStyle.Fill,
            FillColor = CardFillStrongColor,
            BorderColor = Color.FromArgb(184, 230, 223, 239),
            CornerRadius = 22,
            DrawShadow = false,
            AccentHeight = 0,
            Margin = new Padding(0, 12, 0, 0),
            Padding = new Padding(12),
        };
        logHost.Controls.Add(_packagingLogViewer);

        content.Controls.Add(statusRow, 0, 0);
        content.Controls.Add(_packagingProgressBar, 0, 1);
        content.Controls.Add(logHost, 0, 2);

        return CreateCard(
            "\u6253\u5305\u8FDB\u5EA6",
            "\u663E\u793A\u5F53\u524D\u9636\u6BB5\u548C\u6700\u8FD1\u65E5\u5FD7\u3002",
            RoseAccent,
            content,
            fillContent: true,
            marginBottom: 0);
    }

    private Control CreateEditorHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(4, 2, 4, 2),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var tabsHost = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Left,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };

        tabsHost.Controls.Add(_manifestTabButton);
        tabsHost.Controls.Add(_readmeTabButton);

        _packageButton.Margin = new Padding(0);
        _packageButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _packageButton.UpdatePreferredSize();

        header.Controls.Add(tabsHost, 0, 0);
        header.Controls.Add(_packageButton, 1, 0);
        return header;
    }

    private Control CreateCard(
        string title,
        string description,
        Color accentColor,
        Control content,
        bool fillContent,
        bool showHeader = true,
        Padding? bodyPadding = null,
        int marginBottom = 16)
    {
        var card = new GlassPanel
        {
            Dock = fillContent ? DockStyle.Fill : DockStyle.Top,
            AutoSize = !fillContent,
            AutoSizeMode = !fillContent ? AutoSizeMode.GrowAndShrink : AutoSizeMode.GrowOnly,
            FillColor = CardFillColor,
            BorderColor = CardBorderColor,
            CornerRadius = 28,
            AccentColor = accentColor,
            AccentHeight = 5,
            Margin = new Padding(0, 0, 0, marginBottom),
            Padding = bodyPadding ?? new Padding(18, 18, 18, 18),
        };

        var layout = new TableLayoutPanel
        {
            Dock = fillContent ? DockStyle.Fill : DockStyle.Top,
            ColumnCount = 1,
            RowCount = showHeader ? 3 : 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
            AutoSize = !fillContent,
            AutoSizeMode = !fillContent ? AutoSizeMode.GrowAndShrink : AutoSizeMode.GrowOnly,
        };

        if (showHeader)
        {
            var titleLabel = new Label
            {
                AutoSize = true,
                Text = title,
                ForeColor = TitleColor,
                Font = new Font("Segoe UI Semibold", 11.2f, FontStyle.Bold),
                Margin = new Padding(0),
            };

            var descriptionLabel = new Label
            {
                AutoSize = true,
                Text = description,
                ForeColor = BodyColor,
                Font = new Font("Segoe UI", 8.9f),
                Margin = new Padding(0, 5, 0, 14),
            };

            content.Dock = fillContent ? DockStyle.Fill : DockStyle.Top;

            layout.Controls.Add(titleLabel, 0, 0);
            layout.Controls.Add(descriptionLabel, 0, 1);
            layout.Controls.Add(content, 0, 2);
        }
        else
        {
            content.Dock = DockStyle.Fill;
            layout.Controls.Add(content, 0, 0);
        }

        card.Controls.Add(layout);
        return card;
    }

    private Control CreateFieldBlock(string title, string description, TextBox textBox, string buttonText, Action onClick)
    {
        var block = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(0),
        };

        var titleLabel = new Label
        {
            AutoSize = true,
            Text = title,
            ForeColor = TitleColor,
            Font = new Font("Segoe UI Semibold", 9.6f, FontStyle.Bold),
            Margin = new Padding(0),
        };

        var descriptionLabel = new Label
        {
            AutoSize = true,
            Text = description,
            ForeColor = BodyColor,
            Font = new Font("Segoe UI", 8.7f),
            Margin = new Padding(0, 3, 0, 5),
        };

        var shell = new GlassPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            FillColor = CardFillStrongColor,
            BorderColor = Color.FromArgb(190, 230, 222, 240),
            CornerRadius = 18,
            DrawShadow = false,
            AccentHeight = 0,
            Margin = new Padding(0),
            Padding = new Padding(12, 6, 6, 6),
        };

        var inputLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var button = CreateInlineButton(buttonText, onClick);
        inputLayout.Controls.Add(textBox, 0, 0);
        inputLayout.Controls.Add(button, 1, 0);
        shell.Controls.Add(inputLayout);

        block.Controls.Add(titleLabel, 0, 0);
        block.Controls.Add(descriptionLabel, 0, 1);
        block.Controls.Add(shell, 0, 2);
        return block;
    }

    private static TextBox CreateInputTextBox(bool readOnly, string? placeholderText = null)
    {
        return new TextBox
        {
            BorderStyle = BorderStyle.None,
            ReadOnly = readOnly,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 5, 8, 0),
            BackColor = Color.White,
            ForeColor = TitleColor,
            Font = new Font("Segoe UI", 9.5f),
            ShortcutsEnabled = !readOnly,
            PlaceholderText = placeholderText ?? string.Empty,
        };
    }

    private static RichTextBox CreateEditor()
    {
        return new RichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(250, 251, 255),
            ForeColor = TitleColor,
            Font = new Font("Consolas", 10f),
            Margin = new Padding(0),
            WordWrap = false,
            DetectUrls = false,
            AcceptsTab = true,
            HideSelection = false,
        };
    }

    private static Label CreateStatusTitleLabel()
    {
        return new Label
        {
            AutoSize = true,
            Text = DefaultPackagingStatusTitle,
            ForeColor = TitleColor,
            Font = new Font("Segoe UI Semibold", 10.2f, FontStyle.Bold),
            Margin = new Padding(0),
        };
    }

    private static Label CreateStatusDetailLabel()
    {
        return new Label
        {
            AutoSize = true,
            MaximumSize = new Size(0, 0),
            Text = DefaultPackagingStatusDetail,
            ForeColor = BodyColor,
            Font = new Font("Segoe UI", 8.8f),
            Margin = new Padding(0, 4, 0, 0),
        };
    }

    private static Label CreateStatusPercentLabel()
    {
        return new Label
        {
            AutoSize = true,
            Text = "0%",
            ForeColor = TitleColor,
            Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
            Anchor = AnchorStyles.Right,
            Margin = new Padding(16, 0, 0, 0),
        };
    }

    private static ProgressBar CreatePackagingProgressBar()
    {
        return new ProgressBar
        {
            Dock = DockStyle.Fill,
            Height = 18,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Continuous,
            Margin = new Padding(0),
        };
    }

    private static RichTextBox CreateProgressLogViewer()
    {
        return new RichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(250, 251, 255),
            ForeColor = BodyColor,
            Font = new Font("Consolas", 9.2f),
            Margin = new Padding(0),
            ReadOnly = true,
            WordWrap = true,
            DetectUrls = false,
            HideSelection = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
        };
    }

    private TabControl CreateEditorTabs()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Appearance = TabAppearance.FlatButtons,
            ItemSize = new Size(1, 1),
            SizeMode = TabSizeMode.Fixed,
            Multiline = true,
            Font = new Font("Segoe UI Semibold", 9.6f, FontStyle.Bold),
            Padding = new Point(0, 0),
            Margin = new Padding(0),
        };

        tabs.TabPages.Add(CreateEditorPage("manifest.json", _manifestEditor));
        tabs.TabPages.Add(CreateEditorPage("README.md", _readmeEditor));
        return tabs;
    }

    private static TabPage CreateEditorPage(string title, RichTextBox editor)
    {
        var page = new TabPage(title)
        {
            BackColor = Color.FromArgb(248, 249, 253),
            Padding = new Padding(0),
        };

        var host = new GlassPanel
        {
            Dock = DockStyle.Fill,
            FillColor = CardFillStrongColor,
            BorderColor = Color.FromArgb(184, 230, 223, 239),
            CornerRadius = 22,
            DrawShadow = false,
            AccentHeight = 0,
            Margin = new Padding(12),
            Padding = new Padding(12),
        };
        host.Controls.Add(editor);
        page.Controls.Add(host);
        return page;
    }

    private void ConfigureButtons()
    {
        ConfigurePrimaryButton(_packageButton, DefaultPackageButtonText, "\u25B6");
        ConfigureEditorTabButton(_manifestTabButton, "manifest.json");
        ConfigureEditorTabButton(_readmeTabButton, "README.md");
    }

    private static void ConfigurePrimaryButton(RoundedButton button, string text, string glyph)
    {
        button.Text = text;
        button.LeadingGlyph = glyph;
        button.GlyphFont = new Font("Segoe UI Symbol", 10.8f, FontStyle.Regular);
        button.IconTextSpacing = 10;
        button.Padding = new Padding(18, 0, 18, 0);
        button.ContentTextFormatFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        button.SizeToContent = true;
        button.MinimumSize = new Size(144, 42);
        button.CornerRadius = 20;
        button.FillColor = PrimaryButtonColor;
        button.HoverColor = PrimaryButtonHoverColor;
        button.PressedColor = PrimaryButtonPressedColor;
        button.BorderColor = PrimaryButtonBorderColor;
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);
        button.UpdatePreferredSize();
    }

    private static void ConfigureActionButton(RoundedButton button, string glyph, string text, Color accentColor)
    {
        var fill = Blend(Color.White, accentColor, 0.34f);
        var hover = Blend(Color.White, accentColor, 0.44f);
        var pressed = Blend(Color.White, accentColor, 0.56f);

        button.LeadingGlyph = glyph;
        button.Text = text;
        button.GlyphFont = new Font("Segoe UI Symbol", 10.2f, FontStyle.Regular);
        button.IconTextSpacing = 8;
        button.Padding = new Padding(14, 0, 14, 0);
        button.ContentTextFormatFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        button.SizeToContent = true;
        button.MinimumSize = new Size(118, 40);
        button.Dock = DockStyle.None;
        button.Margin = new Padding(0, 0, 8, 8);
        button.CornerRadius = 16;
        button.FillColor = fill;
        button.HoverColor = hover;
        button.PressedColor = pressed;
        button.BorderColor = Blend(accentColor, Color.White, 0.25f);
        button.ForeColor = TitleColor;
        button.Font = new Font("Segoe UI Semibold", 9.4f, FontStyle.Bold);
        button.UpdatePreferredSize();
    }

    private static void ConfigureEditorTabButton(RoundedButton button, string text)
    {
        button.LeadingGlyph = null;
        button.Text = text;
        button.Padding = new Padding(16, 0, 16, 0);
        button.ContentTextFormatFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        button.SizeToContent = true;
        button.MinimumSize = new Size(108, 34);
        button.Margin = new Padding(0, 0, 10, 0);
        button.CornerRadius = 17;
        button.BorderColor = Color.FromArgb(190, 216, 226, 243);
        button.ForeColor = TitleColor;
        button.Font = new Font("Segoe UI Semibold", 9.2f, FontStyle.Bold);
        button.TabStop = false;
        button.UpdatePreferredSize();
    }

    private void RefreshEditorTabButtons()
    {
        ApplyEditorTabButtonState(_manifestTabButton, _editorTabs.SelectedIndex == 0, LavenderAccent);
        ApplyEditorTabButtonState(_readmeTabButton, _editorTabs.SelectedIndex == 1, MintAccent);
    }

    private static void ApplyEditorTabButtonState(RoundedButton button, bool isSelected, Color accentColor)
    {
        var activeFill = Blend(Color.White, accentColor, 0.40f);
        var activeHover = Blend(Color.White, accentColor, 0.50f);
        var activePressed = Blend(Color.White, accentColor, 0.60f);
        var inactiveFill = Color.FromArgb(238, 252, 252, 255);
        var inactiveHover = Blend(Color.White, accentColor, 0.24f);
        var inactivePressed = Blend(Color.White, accentColor, 0.34f);

        button.FillColor = isSelected ? activeFill : inactiveFill;
        button.HoverColor = isSelected ? activeHover : inactiveHover;
        button.PressedColor = isSelected ? activePressed : inactivePressed;
        button.BorderColor = isSelected
            ? Blend(accentColor, Color.White, 0.20f)
            : Color.FromArgb(190, 216, 226, 243);
        button.ForeColor = isSelected ? TitleColor : BodyColor;
        button.Invalidate();
    }

    private void SelectEditorTab(int index)
    {
        if (index < 0 || index >= _editorTabs.TabCount || _editorTabs.SelectedIndex == index)
        {
            return;
        }

        _editorTabs.SelectedIndex = index;
    }

    private static RoundedButton CreateInlineButton(string text, Action onClick)
    {
        var button = new RoundedButton
        {
            Text = text,
            SizeToContent = true,
            MinimumSize = new Size(72, 28),
            Margin = new Padding(0),
            Padding = new Padding(14, 0, 14, 0),
            CornerRadius = 14,
            FillColor = Color.FromArgb(238, 242, 246, 255),
            HoverColor = Color.FromArgb(224, 232, 240, 255),
            PressedColor = Color.FromArgb(210, 223, 236, 252),
            BorderColor = Color.FromArgb(178, 215, 221, 244),
            ForeColor = TitleColor,
            Font = new Font("Segoe UI", 8.7f, FontStyle.Bold),
            Anchor = AnchorStyles.Right,
            ContentTextFormatFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine,
        };
        button.UpdatePreferredSize();
        button.Click += (_, _) => onClick();
        return button;
    }

    private void WireEvents()
    {
        _packageButton.Click += async (_, _) => await PackageAsync();
        _reloadContextButton.Click += (_, _) => ReloadContext();
        _refreshBundleButton.Click += (_, _) => RefreshLatestBundle();
        _reloadTemplatesButton.Click += (_, _) => ReloadEditorTemplates();
        _openOutputButton.Click += (_, _) => OpenOutputDirectory();
        _manifestTabButton.Click += (_, _) => SelectEditorTab(0);
        _readmeTabButton.Click += (_, _) => SelectEditorTab(1);

        _manifestEditor.TextChanged += (_, _) =>
        {
            RefreshPackagePreview();
            SaveUiStateSafely();
        };
        _readmeEditor.TextChanged += (_, _) => SaveUiStateSafely();
        _editorTabs.SelectedIndexChanged += (_, _) =>
        {
            RefreshEditorTabButtons();
            SaveUiStateSafely();
        };

        _projectRootTextBox.TextChanged += (_, _) =>
        {
            RefreshPackagePreview();
            RefreshToolTips();
            SaveUiStateSafely();
        };
        _bundlePathTextBox.TextChanged += (_, _) =>
        {
            RefreshToolTips();
            SaveUiStateSafely();
        };
        _motionStartFrameTextBox.TextChanged += (_, _) =>
        {
            RefreshToolTips();
            SaveUiStateSafely();
        };
        _motionEndFrameTextBox.TextChanged += (_, _) =>
        {
            RefreshToolTips();
            SaveUiStateSafely();
        };
        _versionTextBox.TextChanged += (_, _) =>
        {
            SyncManifestVersionFromVersionTextBox();
            RefreshPackagePreview();
            RefreshToolTips();
            SaveUiStateSafely();
        };
        _outputDirectoryTextBox.TextChanged += (_, _) =>
        {
            RefreshPackagePreview();
            RefreshToolTips();
            SaveUiStateSafely();
        };
        _iconPathTextBox.TextChanged += (_, _) =>
        {
            RefreshToolTips();
            SaveUiStateSafely();
        };
    }

    private void LoadInitialState()
    {
        var savedState = TryLoadUiState();
        if (savedState != null && IsValidProjectRoot(savedState.ProjectRoot))
        {
            try
            {
                LoadProjectContext(savedState.ProjectRoot!);
                ApplyUiState(savedState);
                return;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
            }
        }

        LoadDefaults();
    }

    private void LoadDefaults()
    {
        var projectRoot = PackagingService.TryDetectProjectRoot();
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return;
        }

        LoadProjectContext(projectRoot);
    }

    private void SelectProjectRoot()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "\u9009\u62E9\u5305\u542B MikuDanceProject.csproj \u7684\u76EE\u5F55",
            SelectedPath = string.IsNullOrWhiteSpace(_projectRootTextBox.Text) ? AppContext.BaseDirectory : _projectRootTextBox.Text,
            ShowNewFolderButton = false,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        LoadProjectContext(dialog.SelectedPath);
    }

    private void LoadProjectContext(string projectRoot)
    {
        var layout = PackagingService.CreateLayout(projectRoot);
        _projectRootTextBox.Text = layout.ProjectRoot;
        _versionTextBox.Text = PackagingService.ReadCurrentVersion(layout);
        _outputDirectoryTextBox.Text = layout.DistDirectory;
        _iconPathTextBox.Text = PackagingService.GetDefaultIconPath(layout);
        LoadEditorTemplates(layout);

        var latestBundle = PackagingService.TryDetectLatestBundle(layout);
        _bundlePathTextBox.Text = latestBundle ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_motionStartFrameTextBox.Text))
        {
            _motionStartFrameTextBox.Text = PackagingService.GetDefaultMotionStartFrame().ToString();
        }

        RefreshPackagePreview();
    }

    private void ReloadContext()
    {
        if (string.IsNullOrWhiteSpace(_projectRootTextBox.Text))
        {
            return;
        }

        LoadProjectContext(_projectRootTextBox.Text.Trim());
    }

    private void ReloadEditorTemplates()
    {
        if (string.IsNullOrWhiteSpace(_projectRootTextBox.Text))
        {
            return;
        }

        var layout = PackagingService.CreateLayout(_projectRootTextBox.Text.Trim());
        LoadEditorTemplates(layout);
    }

    private void RefreshLatestBundle()
    {
        if (string.IsNullOrWhiteSpace(_projectRootTextBox.Text))
        {
            return;
        }

        var layout = PackagingService.CreateLayout(_projectRootTextBox.Text.Trim());
        var latestBundle = PackagingService.TryDetectLatestBundle(layout);
        _bundlePathTextBox.Text = latestBundle ?? string.Empty;
    }

    private void SelectBundle()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "AssetBundle (*.bundle)|*.bundle|All files (*.*)|*.*",
            CheckFileExists = true,
            Title = "\u9009\u62E9\u8981\u6253\u5305\u7684 AB \u6587\u4EF6",
            InitialDirectory = GetExistingDirectory(_bundlePathTextBox.Text, _projectRootTextBox.Text),
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _bundlePathTextBox.Text = dialog.FileName;
    }

    private void ResetMotionStartFrame()
    {
        _motionStartFrameTextBox.Text = PackagingService.GetDefaultMotionStartFrame().ToString();
    }

    private void ClearMotionEndFrame()
    {
        _motionEndFrameTextBox.Clear();
    }

    private void LoadVersionFromProject()
    {
        if (string.IsNullOrWhiteSpace(_projectRootTextBox.Text))
        {
            return;
        }

        var layout = PackagingService.CreateLayout(_projectRootTextBox.Text.Trim());
        _versionTextBox.Text = PackagingService.ReadCurrentVersion(layout);
    }

    private void SelectOutputDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "\u9009\u62E9\u6587\u4EF6\u5939\u8F93\u51FA\u76EE\u5F55",
            SelectedPath = GetExistingDirectory(_outputDirectoryTextBox.Text, _projectRootTextBox.Text),
            ShowNewFolderButton = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _outputDirectoryTextBox.Text = dialog.SelectedPath;
    }

    private void SelectIcon()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "PNG Image (*.png)|*.png|All files (*.*)|*.*",
            CheckFileExists = true,
            Title = "\u9009\u62E9\u5BFC\u51FA\u65F6\u4F7F\u7528\u7684 icon.png",
            InitialDirectory = GetExistingDirectory(_iconPathTextBox.Text, _projectRootTextBox.Text),
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _iconPathTextBox.Text = dialog.FileName;
    }

    private void OpenOutputDirectory()
    {
        TryOpenDirectory(_outputDirectoryTextBox.Text.Trim());
    }

    private void TryOpenDirectory(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(directoryPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = directoryPath,
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            AppendLog($"\u6253\u5F00\u8F93\u51FA\u76EE\u5F55\u5931\u8D25: {exception.Message}");
        }
    }

    private void RefreshPackagePreview()
    {
        if (string.IsNullOrWhiteSpace(_projectRootTextBox.Text)
            || string.IsNullOrWhiteSpace(_outputDirectoryTextBox.Text)
            || string.IsNullOrWhiteSpace(_versionTextBox.Text))
        {
            _packagePreviewTextBox.Text = string.Empty;
            return;
        }

        try
        {
            var layout = PackagingService.CreateLayout(_projectRootTextBox.Text.Trim());
            var packageName = PackagingService.ResolvePackageNameFromManifestText(_manifestEditor.Text, layout.PackageName);
            _packagePreviewTextBox.Text = Path.Combine(_outputDirectoryTextBox.Text.Trim(), $"Thanks-{packageName}-{_versionTextBox.Text.Trim()}");
        }
        catch
        {
            _packagePreviewTextBox.Text = Path.Combine(_outputDirectoryTextBox.Text.Trim(), $"Thanks-MikuShowcase-{_versionTextBox.Text.Trim()}");
        }
    }

    private async Task PackageAsync()
    {
        if (!TryBuildPackagingOptions(out var options, out var validationError))
        {
            MessageBox.Show(this, validationError, "Miku Dance Packager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SaveUiStateSafely();
        ResetPackagingProgressPresentation(clearLog: true);
        AppendLog("\u5F00\u59CB\u6253\u5305\u3002");
        ApplyPackagingProgress(new PackagingProgressUpdate(
            0,
            "\u51C6\u5907\u5F00\u59CB",
            "\u6B63\u5728\u68C0\u67E5\u53C2\u6570\u5E76\u521D\u59CB\u5316\u6253\u5305\u4EFB\u52A1\u3002"));
        ToggleUi(enabled: false);

        try
        {
            var packagePath = await Task.Run(() => PackagingService.Package(
                options,
                AppendLogThreadSafe,
                ReportPackagingProgressThreadSafe));
            SaveUiStateSafely();
            MessageBox.Show(this, $"\u6253\u5305\u5B8C\u6210\uFF1A\n{packagePath}", "Miku Dance Packager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            TryOpenDirectory(packagePath);
        }
        catch (Exception exception)
        {
            AppendLog(exception.ToString());
            ApplyPackagingProgress(new PackagingProgressUpdate(
                _currentPackagingProgressPercent,
                "\u6253\u5305\u5931\u8D25",
                "\u5904\u7406\u8FC7\u7A0B\u4E2D\u53D1\u751F\u9519\u8BEF\uFF0C\u8BF7\u67E5\u770B\u4E0B\u65B9\u65E5\u5FD7\u3002"));
            MessageBox.Show(this, exception.ToString(), "\u6253\u5305\u5931\u8D25", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            ToggleUi(enabled: true);
            _packageButton.Text = DefaultPackageButtonText;
            RefreshPackagePreview();
        }
    }

    private bool TryBuildPackagingOptions(out PackagingOptions options, out string validationError)
    {
        options = default!;

        if (string.IsNullOrWhiteSpace(_projectRootTextBox.Text))
        {
            validationError = "\u8BF7\u5148\u9009\u62E9\u5305\u542B MikuDanceProject.csproj \u7684\u9879\u76EE\u76EE\u5F55\u3002";
            return false;
        }

        if (!int.TryParse(_motionStartFrameTextBox.Text.Trim(), out var motionStartFrame) || motionStartFrame < 0)
        {
            validationError = "\u52A8\u753B\u8D77\u59CB\u5E27\u5FC5\u987B\u662F\u5927\u4E8E\u7B49\u4E8E 0 \u7684\u6574\u6570\u3002";
            return false;
        }

        int? motionEndFrame = null;
        var motionEndFrameText = _motionEndFrameTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(motionEndFrameText))
        {
            if (!int.TryParse(motionEndFrameText, out var parsedMotionEndFrame) || parsedMotionEndFrame < 0)
            {
                validationError = "\u52A8\u753B\u7ED3\u675F\u5E27\u5FC5\u987B\u662F\u5927\u4E8E\u7B49\u4E8E 0 \u7684\u6574\u6570\uFF0C\u6216\u7559\u7A7A\u3002";
                return false;
            }

            if (parsedMotionEndFrame <= motionStartFrame)
            {
                validationError = "\u52A8\u753B\u7ED3\u675F\u5E27\u5FC5\u987B\u5927\u4E8E\u52A8\u753B\u8D77\u59CB\u5E27\u3002";
                return false;
            }

            motionEndFrame = parsedMotionEndFrame;
        }

        options = new PackagingOptions(
            _projectRootTextBox.Text.Trim(),
            _bundlePathTextBox.Text.Trim(),
            _versionTextBox.Text.Trim(),
            _outputDirectoryTextBox.Text.Trim(),
            _iconPathTextBox.Text.Trim(),
            _manifestEditor.Text,
            _readmeEditor.Text,
            motionStartFrame,
            motionEndFrame);

        validationError = string.Empty;
        return true;
    }

    private void ToggleUi(bool enabled)
    {
        _packageButton.Enabled = enabled;
        _reloadContextButton.Enabled = enabled;
        _refreshBundleButton.Enabled = enabled;
        _reloadTemplatesButton.Enabled = enabled;
        _openOutputButton.Enabled = enabled;
        _motionStartFrameTextBox.Enabled = enabled;
        _motionEndFrameTextBox.Enabled = enabled;
        _versionTextBox.Enabled = enabled;
        UseWaitCursor = !enabled;
    }

    private void ResetPackagingProgressPresentation(bool clearLog)
    {
        if (clearLog)
        {
            _packagingLogViewer.Clear();
        }

        ApplyPackagingProgress(new PackagingProgressUpdate(
            0,
            DefaultPackagingStatusTitle,
            DefaultPackagingStatusDetail));
        _packageButton.Text = DefaultPackageButtonText;
    }

    private void ReportPackagingProgressThreadSafe(PackagingProgressUpdate update)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<PackagingProgressUpdate>(ApplyPackagingProgress), update);
            return;
        }

        ApplyPackagingProgress(update);
    }

    private void ApplyPackagingProgress(PackagingProgressUpdate update)
    {
        var clampedPercent = Math.Clamp(update.Percent, 0, 100);
        _currentPackagingProgressPercent = clampedPercent;

        _packagingStatusTitleLabel.Text = string.IsNullOrWhiteSpace(update.Title)
            ? DefaultPackagingStatusTitle
            : update.Title.Trim();
        _packagingStatusDetailLabel.Text = string.IsNullOrWhiteSpace(update.Detail)
            ? DefaultPackagingStatusDetail
            : update.Detail.Trim();

        if (update.IsIndeterminate)
        {
            if (_packagingProgressBar.Style != ProgressBarStyle.Marquee)
            {
                _packagingProgressBar.Style = ProgressBarStyle.Marquee;
                _packagingProgressBar.MarqueeAnimationSpeed = 18;
            }

            _packagingProgressPercentLabel.Text = "\u5904\u7406\u4E2D";
        }
        else
        {
            if (_packagingProgressBar.Style != ProgressBarStyle.Continuous)
            {
                _packagingProgressBar.Style = ProgressBarStyle.Continuous;
                _packagingProgressBar.MarqueeAnimationSpeed = 0;
            }

            _packagingProgressBar.Value = clampedPercent;
            _packagingProgressPercentLabel.Text = $"{clampedPercent}%";
        }

        if (_packageButton.Enabled)
        {
            _packageButton.Text = DefaultPackageButtonText;
        }
        else
        {
            _packageButton.Text = update.IsIndeterminate
                ? "\u6253\u5305\u4E2D..."
                : $"\u6253\u5305\u4E2D {clampedPercent}%";
        }
    }

    private void AppendLogThreadSafe(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendLog), message);
            return;
        }

        AppendLog(message);
    }

    private void AppendLog(string message)
    {
        Debug.WriteLine(message);

        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var normalizedMessage = message.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var rawLine in normalizedMessage.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            _packagingLogViewer.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        }

        TrimPackagingLogViewer();
        _packagingLogViewer.SelectionStart = _packagingLogViewer.TextLength;
        _packagingLogViewer.ScrollToCaret();
    }

    private void TrimPackagingLogViewer()
    {
        if (_packagingLogViewer.TextLength <= ProgressLogTrimThreshold)
        {
            return;
        }

        var trimIndex = _packagingLogViewer.Text.IndexOf(
            Environment.NewLine,
            Math.Max(0, _packagingLogViewer.TextLength - ProgressLogTrimTarget),
            StringComparison.Ordinal);
        if (trimIndex < 0)
        {
            _packagingLogViewer.Clear();
            return;
        }

        var removeLength = Math.Min(_packagingLogViewer.TextLength, trimIndex + Environment.NewLine.Length);
        _packagingLogViewer.Select(0, removeLength);
        _packagingLogViewer.SelectedText = string.Empty;
    }

    private void LoadEditorTemplates(ProjectLayout layout)
    {
        _manifestEditor.Text = PackagingService.ReadManifestTemplate(layout);
        _readmeEditor.Text = PackagingService.ReadReadmeTemplate(layout);
        SyncManifestVersionFromVersionTextBox();
    }

    private void SyncManifestVersionFromVersionTextBox()
    {
        if (_isSynchronizingManifestVersion)
        {
            return;
        }

        var version = _versionTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(_manifestEditor.Text))
        {
            return;
        }

        try
        {
            var updatedManifestText = PackagingService.UpdateManifestVersionText(_manifestEditor.Text, version);
            if (string.Equals(updatedManifestText, _manifestEditor.Text, StringComparison.Ordinal))
            {
                return;
            }

            _isSynchronizingManifestVersion = true;
            var selectionStart = _manifestEditor.SelectionStart;
            var selectionLength = _manifestEditor.SelectionLength;

            _manifestEditor.Text = updatedManifestText;

            var safeSelectionStart = Math.Min(selectionStart, _manifestEditor.TextLength);
            var safeSelectionLength = Math.Min(selectionLength, _manifestEditor.TextLength - safeSelectionStart);
            _manifestEditor.Select(safeSelectionStart, safeSelectionLength);
        }
        finally
        {
            _isSynchronizingManifestVersion = false;
        }
    }

    private void ApplyUiState(PackagerUiState state)
    {
        _projectRootTextBox.Text = state.ProjectRoot ?? _projectRootTextBox.Text;
        _bundlePathTextBox.Text = state.BundlePath ?? _bundlePathTextBox.Text;
        _motionStartFrameTextBox.Text = NormalizeMotionStartFrameText(state.MotionStartFrame);
        _motionEndFrameTextBox.Text = state.MotionEndFrame ?? _motionEndFrameTextBox.Text;
        _versionTextBox.Text = state.Version ?? _versionTextBox.Text;
        _outputDirectoryTextBox.Text = state.OutputDirectory ?? _outputDirectoryTextBox.Text;
        _iconPathTextBox.Text = state.IconPath ?? _iconPathTextBox.Text;

        if (!string.IsNullOrWhiteSpace(state.ManifestJson))
        {
            _manifestEditor.Text = state.ManifestJson;
        }

        if (!string.IsNullOrWhiteSpace(state.ReadmeMarkdown))
        {
            _readmeEditor.Text = state.ReadmeMarkdown;
        }

        if (state.SelectedEditorTabIndex >= 0 && state.SelectedEditorTabIndex < _editorTabs.TabCount)
        {
            _editorTabs.SelectedIndex = state.SelectedEditorTabIndex;
        }

        RefreshPackagePreview();
        RefreshToolTips();
        RefreshEditorTabButtons();
    }

    private string NormalizeMotionStartFrameText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return PackagingService.GetDefaultMotionStartFrame().ToString();
        }

        var trimmedValue = value.Trim();
        return string.Equals(trimmedValue, LegacyDefaultMotionStartFrameText, StringComparison.Ordinal)
            ? PackagingService.GetDefaultMotionStartFrame().ToString()
            : trimmedValue;
    }

    private void SaveUiStateSafely()
    {
        try
        {
            var directory = Path.GetDirectoryName(UiStatePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(CreateUiStateSnapshot(), UiStateSerializerOptions);
            File.WriteAllText(UiStatePath, json);
        }
        catch
        {
        }
    }

    private PackagerUiState CreateUiStateSnapshot()
    {
        return new PackagerUiState
        {
            ProjectRoot = _projectRootTextBox.Text,
            BundlePath = _bundlePathTextBox.Text,
            MotionStartFrame = _motionStartFrameTextBox.Text,
            MotionEndFrame = _motionEndFrameTextBox.Text,
            Version = _versionTextBox.Text,
            OutputDirectory = _outputDirectoryTextBox.Text,
            IconPath = _iconPathTextBox.Text,
            ManifestJson = _manifestEditor.Text,
            ReadmeMarkdown = _readmeEditor.Text,
            SelectedEditorTabIndex = _editorTabs.SelectedIndex,
        };
    }

    private static PackagerUiState? TryLoadUiState()
    {
        try
        {
            if (!File.Exists(UiStatePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<PackagerUiState>(File.ReadAllText(UiStatePath));
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            return null;
        }
    }

    private void RefreshToolTips()
    {
        _toolTip.SetToolTip(_projectRootTextBox, _projectRootTextBox.Text);
        _toolTip.SetToolTip(_bundlePathTextBox, _bundlePathTextBox.Text);
        _toolTip.SetToolTip(_motionStartFrameTextBox, _motionStartFrameTextBox.Text);
        _toolTip.SetToolTip(_motionEndFrameTextBox, _motionEndFrameTextBox.Text);
        _toolTip.SetToolTip(_versionTextBox, _versionTextBox.Text);
        _toolTip.SetToolTip(_outputDirectoryTextBox, _outputDirectoryTextBox.Text);
        _toolTip.SetToolTip(_iconPathTextBox, _iconPathTextBox.Text);
        _toolTip.SetToolTip(_packagePreviewTextBox, _packagePreviewTextBox.Text);
    }

    private void DrawEditorTab(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || sender is not TabControl tabs)
        {
            return;
        }

        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = Rectangle.Inflate(e.Bounds, -6, -4);
        var selected = e.Index == tabs.SelectedIndex;

        using var path = CreateRoundRectanglePath(bounds, 14);
        using var fillBrush = new SolidBrush(selected
            ? Color.FromArgb(252, 255, 255, 255)
            : Color.FromArgb(154, 245, 241, 248));
        using var borderPen = new Pen(selected
            ? Color.FromArgb(188, 232, 223, 241)
            : Color.FromArgb(146, 226, 218, 238));

        graphics.FillPath(fillBrush, path);
        graphics.DrawPath(borderPen, path);

        TextRenderer.DrawText(
            graphics,
            tabs.TabPages[e.Index].Text,
            tabs.Font,
            bounds,
            selected ? TitleColor : BodyColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    internal static GraphicsPath CreateRoundRectanglePath(Rectangle bounds, int radius)
    {
        var safeRadius = Math.Max(1, radius);
        var diameter = safeRadius * 2;
        var rect = Rectangle.Inflate(bounds, -1, -1);
        var path = new GraphicsPath();

        if (rect.Width <= 0 || rect.Height <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static string GetExistingDirectory(string preferredPath, string fallbackPath)
    {
        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            var candidate = File.Exists(preferredPath)
                ? Path.GetDirectoryName(preferredPath)
                : preferredPath;

            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Directory.Exists(fallbackPath) ? fallbackPath : AppContext.BaseDirectory;
    }

    private static Color Blend(Color baseColor, Color overlayColor, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        var inverse = 1f - amount;
        return Color.FromArgb(
            255,
            (int)(baseColor.R * inverse + overlayColor.R * amount),
            (int)(baseColor.G * inverse + overlayColor.G * amount),
            (int)(baseColor.B * inverse + overlayColor.B * amount));
    }

    private static bool IsValidProjectRoot(string? projectRoot)
    {
        return !string.IsNullOrWhiteSpace(projectRoot)
               && File.Exists(Path.Combine(projectRoot, "MikuDanceProject.csproj"));
    }

    private static void DrawGlow(Graphics graphics, Rectangle bounds, Color color)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(bounds);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = color,
            SurroundColors = new[] { Color.Transparent },
        };
        graphics.FillEllipse(brush, bounds);
    }

}

internal sealed class PackagerUiState
{
    public string? ProjectRoot { get; set; }
    public string? BundlePath { get; set; }
    public string? MotionStartFrame { get; set; }
    public string? MotionEndFrame { get; set; }
    public string? Version { get; set; }
    public string? OutputDirectory { get; set; }
    public string? IconPath { get; set; }
    public string? ManifestJson { get; set; }
    public string? ReadmeMarkdown { get; set; }
    public int SelectedEditorTabIndex { get; set; }
}

internal sealed class GlassPanel : Panel
{
    public int CornerRadius { get; set; } = 24;
    public int AccentHeight { get; set; } = 4;
    public bool DrawShadow { get; set; } = true;
    public Color FillColor { get; set; } = Color.FromArgb(220, 255, 255, 255);
    public Color BorderColor { get; set; } = Color.FromArgb(180, 229, 221, 238);
    public Color AccentColor { get; set; } = LavenderAccent;

    private static readonly Color LavenderAccent = Color.FromArgb(181, 194, 255);

    public GlassPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint
            | ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        using var path = MainForm.CreateRoundRectanglePath(new Rectangle(0, 0, Width, Height), CornerRadius);
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        using var fillPath = MainForm.CreateRoundRectanglePath(rect, CornerRadius);

        if (DrawShadow)
        {
            for (var index = 5; index >= 1; index--)
            {
                var shadowRect = Rectangle.Inflate(rect, -index, -index);
                using var shadowPath = MainForm.CreateRoundRectanglePath(shadowRect, Math.Max(6, CornerRadius - index));
                using var shadowPen = new Pen(Color.FromArgb(10 + index * 4, 165, 153, 186), 1f);
                e.Graphics.DrawPath(shadowPen, shadowPath);
            }
        }

        using var fillBrush = new SolidBrush(FillColor);
        using var borderPen = new Pen(BorderColor);
        e.Graphics.FillPath(fillBrush, fillPath);
        e.Graphics.DrawPath(borderPen, fillPath);

        if (AccentHeight > 0)
        {
            using var clipRegion = new Region(fillPath);
            var oldClip = e.Graphics.Clip;
            e.Graphics.Clip = clipRegion;
            using var accentBrush = new LinearGradientBrush(
                new Rectangle(0, 0, Width, Math.Max(AccentHeight, 1)),
                AccentColor,
                Color.FromArgb(Math.Max(0, AccentColor.A - 30), AccentColor),
                0f);
            e.Graphics.FillRectangle(accentBrush, new Rectangle(0, 0, Width, AccentHeight + 1));
            e.Graphics.Clip = oldClip;
        }
    }
}

internal sealed class RoundedButton : Button
{
    private bool _hovered;
    private bool _pressed;
    private string? _leadingGlyph;
    private Font? _glyphFont;
    private int _iconTextSpacing = 8;
    private bool _sizeToContent;
    private TextFormatFlags _contentTextFormatFlags =
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;

    public int CornerRadius { get; set; } = 16;
    public Color FillColor { get; set; } = Color.White;
    public Color HoverColor { get; set; } = Color.Gainsboro;
    public Color PressedColor { get; set; } = Color.Silver;
    public Color BorderColor { get; set; } = Color.Transparent;
    public string? LeadingGlyph
    {
        get => _leadingGlyph;
        set
        {
            if (string.Equals(_leadingGlyph, value, StringComparison.Ordinal))
            {
                return;
            }

            _leadingGlyph = value;
            UpdatePreferredSize();
            Invalidate();
        }
    }

    public Font? GlyphFont
    {
        get => _glyphFont;
        set
        {
            if (ReferenceEquals(_glyphFont, value))
            {
                return;
            }

            _glyphFont = value;
            UpdatePreferredSize();
            Invalidate();
        }
    }

    public int IconTextSpacing
    {
        get => _iconTextSpacing;
        set
        {
            var normalizedValue = Math.Max(0, value);
            if (_iconTextSpacing == normalizedValue)
            {
                return;
            }

            _iconTextSpacing = normalizedValue;
            UpdatePreferredSize();
            Invalidate();
        }
    }

    public bool SizeToContent
    {
        get => _sizeToContent;
        set
        {
            if (_sizeToContent == value)
            {
                return;
            }

            _sizeToContent = value;
            UpdatePreferredSize();
        }
    }

    public TextFormatFlags ContentTextFormatFlags
    {
        get => _contentTextFormatFlags;
        set
        {
            if (_contentTextFormatFlags == value)
            {
                return;
            }

            _contentTextFormatFlags = value;
            UpdatePreferredSize();
            Invalidate();
        }
    }

    public RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseDownBackColor = Color.Transparent;
        FlatAppearance.MouseOverBackColor = Color.Transparent;
        BackColor = Color.Transparent;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        AutoSize = false;
        Padding = new Padding(14, 0, 14, 0);
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint
            | ControlStyles.SupportsTransparentBackColor,
            true);
    }

    public void UpdatePreferredSize()
    {
        if (!SizeToContent || IsDisposed)
        {
            return;
        }

        var preferredSize = GetPreferredSize(Size.Empty);
        if (Size != preferredSize)
        {
            Size = preferredSize;
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var textSize = MeasureContent(Text, Font);
        var glyphSize = MeasureContent(LeadingGlyph, GlyphFont ?? Font);
        var spacing = textSize.Width > 0 && glyphSize.Width > 0 ? IconTextSpacing : 0;
        var preferredWidth = Padding.Left + Padding.Right + glyphSize.Width + spacing + textSize.Width;
        var preferredHeight = Padding.Top + Padding.Bottom + Math.Max(textSize.Height, glyphSize.Height);

        if (preferredWidth <= 0)
        {
            preferredWidth = DefaultSize.Width;
        }

        if (preferredHeight <= 0)
        {
            preferredHeight = DefaultSize.Height;
        }

        return new Size(
            Math.Max(MinimumSize.Width, preferredWidth),
            Math.Max(MinimumSize.Height, preferredHeight));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        using var path = MainForm.CreateRoundRectanglePath(new Rectangle(0, 0, Width, Height), CornerRadius);
        Region = new Region(path);
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        UpdatePreferredSize();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        UpdatePreferredSize();
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdatePreferredSize();
        Invalidate();
    }

    protected override void OnPaddingChanged(EventArgs e)
    {
        base.OnPaddingChanged(e);
        UpdatePreferredSize();
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        _pressed = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        _pressed = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var fill = !Enabled
            ? Color.FromArgb(150, FillColor)
            : _pressed
                ? PressedColor
                : _hovered
                    ? HoverColor
                    : FillColor;

        using var path = MainForm.CreateRoundRectanglePath(rect, CornerRadius);
        using var fillBrush = new SolidBrush(fill);
        using var borderPen = new Pen(BorderColor);

        e.Graphics.FillPath(fillBrush, path);
        e.Graphics.DrawPath(borderPen, path);

        var textColor = Enabled ? ForeColor : Color.FromArgb(168, ForeColor);
        var contentRect = Rectangle.FromLTRB(
            rect.Left + Padding.Left,
            rect.Top + Padding.Top,
            rect.Right - Padding.Right,
            rect.Bottom - Padding.Bottom);

        if (contentRect.Width <= 0 || contentRect.Height <= 0)
        {
            return;
        }

        var textSize = MeasureContent(Text, Font);
        var glyphFont = GlyphFont ?? Font;
        var glyphSize = MeasureContent(LeadingGlyph, glyphFont);
        var spacing = textSize.Width > 0 && glyphSize.Width > 0 ? IconTextSpacing : 0;
        var contentWidth = glyphSize.Width + spacing + textSize.Width;

        var contentX = contentRect.Left;
        var horizontalFlags = ContentTextFormatFlags & (TextFormatFlags.HorizontalCenter | TextFormatFlags.Right);
        if (horizontalFlags == TextFormatFlags.Right)
        {
            contentX = contentRect.Right - contentWidth;
        }
        else if (horizontalFlags == TextFormatFlags.HorizontalCenter)
        {
            contentX = contentRect.Left + ((contentRect.Width - contentWidth) / 2);
        }

        contentX = Math.Max(contentRect.Left, contentX);

        if (glyphSize.Width > 0 && !string.IsNullOrEmpty(LeadingGlyph))
        {
            var glyphRect = new Rectangle(
                contentX,
                contentRect.Top + ((contentRect.Height - glyphSize.Height) / 2),
                Math.Max(1, glyphSize.Width),
                Math.Max(1, glyphSize.Height));
            TextRenderer.DrawText(
                e.Graphics,
                LeadingGlyph,
                glyphFont,
                glyphRect,
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            contentX = glyphRect.Right + spacing;
        }

        if (textSize.Width > 0 && !string.IsNullOrEmpty(Text))
        {
            var textRect = new Rectangle(
                contentX,
                contentRect.Top,
                Math.Max(0, contentRect.Right - contentX),
                contentRect.Height);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textRect,
                textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }

    private static Size MeasureContent(string? value, Font font)
    {
        if (string.IsNullOrEmpty(value))
        {
            return Size.Empty;
        }

        return TextRenderer.MeasureText(
            value,
            font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
}
