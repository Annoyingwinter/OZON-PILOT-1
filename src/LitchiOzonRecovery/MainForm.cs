using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LitchiOzonRecovery
{
    public sealed class MainForm : Form
    {
        private const string DefaultOzonClientId = "";
        private const string DefaultOzonApiKey = "";
        private static readonly Color ShellBack = Color.FromArgb(248, 246, 242);
        private static readonly Color CardBack = Color.FromArgb(255, 253, 249);
        private static readonly Color SoftCardBack = Color.FromArgb(252, 248, 242);
        private static readonly Color LineWarm = Color.FromArgb(231, 222, 212);
        private static readonly Color TextStrong = Color.FromArgb(31, 25, 21);
        private static readonly Color TextMuted = Color.FromArgb(111, 99, 89);
        private static readonly Color PilotGreen = Color.FromArgb(255, 106, 0);
        private static readonly Color PilotGreenDark = Color.FromArgb(184, 70, 0);
        private static readonly Color PilotGreenSoft = Color.FromArgb(255, 239, 224);
        private static readonly Color ZincPanel = Color.FromArgb(244, 238, 231);
        private static readonly Color WarningAmber = Color.FromArgb(184, 89, 0);
        private static readonly Color Ink = Color.FromArgb(47, 38, 32);
        private static readonly Color Ink2 = Color.FromArgb(69, 55, 46);

        private sealed class FeeRuleDisplayRow
        {
            public FeeRule Rule { get; set; }
            public int Id { get; set; }
            public long CategoryId1 { get; set; }
            public long CategoryId2 { get; set; }
            public string Category1 { get; set; }
            public string Category2 { get; set; }
            public decimal FBS { get; set; }
            public decimal FBS1500 { get; set; }
            public decimal FBS5000 { get; set; }
            public decimal FBP { get; set; }
            public decimal FBP1500 { get; set; }
            public decimal FBP5000 { get; set; }
            public decimal FBO { get; set; }
            public decimal FBO1500 { get; set; }
            public decimal FBO5000 { get; set; }
        }

        private sealed class RoundedPanel : Panel
        {
            public int Radius { get; set; }
            public Color BorderColor { get; set; }
            public Color FillColor { get; set; }
            public Color ShadowColor { get; set; }
            public bool DrawShadow { get; set; }

            public RoundedPanel()
            {
                Radius = 16;
                BorderColor = LineWarm;
                FillColor = SoftCardBack;
                ShadowColor = Color.FromArgb(14, 156, 91, 40);
                DrawShadow = true;
                BackColor = Color.Transparent;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle body = ClientRectangle;
                body.Width -= 1;
                body.Height -= 1;

                if (DrawShadow && body.Width > 12 && body.Height > 12)
                {
                    Rectangle shadow = body;
                    shadow.Inflate(-2, -2);
                    shadow.Offset(0, 5);
                    using (GraphicsPath shadowPath = CreateRoundPath(shadow, Radius))
                    using (SolidBrush shadowBrush = new SolidBrush(ShadowColor))
                    {
                        e.Graphics.FillPath(shadowBrush, shadowPath);
                    }
                }

                using (GraphicsPath path = CreateRoundPath(body, Radius))
                using (SolidBrush brush = new SolidBrush(FillColor))
                using (Pen pen = new Pen(BorderColor))
                {
                    e.Graphics.FillPath(brush, path);
                    e.Graphics.DrawPath(pen, path);
                }

                Rectangle highlight = body;
                highlight.Inflate(-2, -2);
                using (GraphicsPath highlightPath = CreateRoundPath(highlight, Math.Max(4, Radius - 2)))
                using (Pen highlightPen = new Pen(Color.FromArgb(112, 255, 255, 255)))
                {
                    e.Graphics.DrawPath(highlightPen, highlightPath);
                }

                base.OnPaint(e);
            }
        }

        private sealed class GradientPanel : Panel
        {
            public Color StartColor { get; set; }
            public Color EndColor { get; set; }
            public float Angle { get; set; }

            public GradientPanel()
            {
                StartColor = Color.FromArgb(255, 253, 249);
                EndColor = Color.FromArgb(247, 239, 231);
                Angle = 90f;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(ClientRectangle, StartColor, EndColor, Angle))
                {
                    e.Graphics.FillRectangle(brush, ClientRectangle);
                }

                base.OnPaint(e);
            }
        }

        private sealed class HeaderlessTabControl : TabControl
        {
            private const int TcmAdjustRect = 0x1328;

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == TcmAdjustRect && !DesignMode)
                {
                    m.Result = (IntPtr)1;
                    return;
                }

                base.WndProc(ref m);
            }
        }

        private readonly AppPaths _paths;
        private readonly ProductAutomationService _automationService;
        private readonly OzonFulfillmentLabelService _fulfillmentLabelService;
        private readonly Random _random;
        private Label _headerClockLabel;
        private Label _headerStatusLabel;
        private CancellationTokenSource _currentProcessCancel;
        private string _currentProcessName;
        private AssetSnapshot _snapshot;
        private SourcingResult _lastSourcingResult;
        private string _fullAutoReport;
        private bool _restoringPersistentState;
        private bool _1688LoginVerified;
        private bool _ozonCredentialsVerified;

        private TabControl _mainTabs;
        private TextBox _overviewBox;
        private ComboBox _languageComboBox;
        private TextBox _autoLoopCountBox;
        private PropertyGrid _configGrid;
        private TreeView _categoryTree;
        private DataGridView _feeGrid;
        private TextBox _assetSearchBox;
        private TextBox _browserUrlBox;
        private Label _browserStatusLabel;
        private Label _setup1688StatusLabel;
        private Label _setupOzonStatusLabel;
        private Label _setupCompletedLabel;
        private Label _setupActionLabel;
        private TableLayoutPanel _setupBodyLayout;
        private Panel _setupPanel;
        private Panel _setupBrowserHost;
        private Panel _operationBrowserHost;
        private TabPage _setupTab;
        private TabPage _operationTab;
        private TabPage _overviewTab;
        private TabPage _assetsTab;
        private TabPage _configTab;
        private TabPage _languageTab;
        private Button _navSetupButton;
        private Button _navOperationButton;
        private Button _navOverviewButton;
        private Button _navAssetsButton;
        private Button _navConfigButton;
        private Button _navLanguageButton;
        private WebView2 _browser;
        private bool _browserExtensionReady;
        private TextBox _autoKeywordsBox;
        private ComboBox _autoProviderBox;
        private TextBox _autoApiKeyBox;
        private TextBox _autoApiSecretBox;
        private TextBox _autoPerKeywordBox;
        private TextBox _autoDetailLimitBox;
        private TextBox _autoRubRateBox;
        private TextBox _autoCategoryIdBox;
        private TextBox _autoTypeIdBox;
        private TextBox _autoPriceMultiplierBox;
        private Label _operationReadinessLabel;
        private Label _operationCategoryLabel;
        private Label _operationEmptyStateLabel;
        private Label _overviewNextActionLabel;
        private Label _overviewSetupLabel;
        private Label _overviewCategoryLabel;
        private Label _overviewResultLabel;
        private Label _overviewLabelLabel;
        private FlowLayoutPanel _operationActionPanel;
        private Panel _operationPreparePanel;
        private Label _operationPrepareCommandLabel;
        private Control _operationKeywordField;
        private Control _operationLoopField;
        private TableLayoutPanel _operationLayout;
        private RoundedPanel _operationCommandPanel;
        private RoundedPanel _operationSettingsPanel;
        private RoundedPanel _operationResultsPanel;
        private Button _fullAutoButton;
        private Button _runSourcingButton;
        private Button _uploadSelectedButton;
        private Button _listFbsButton;
        private Button _downloadLabelsButton;
        private Button _exportResultsButton;
        private TextBox _ozonClientIdBox;
        private TextBox _ozonApiKeyBox;
        private DataGridView _autoResultGrid;
        private TextBox _autoLogBox;
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _statusLabel;
        private string _assetErrorMessage;
        private string _uiLanguage = "zh";

        public MainForm()
        {
            _paths = AppPaths.Discover();
            _automationService = new ProductAutomationService();
            _fulfillmentLabelService = new OzonFulfillmentLabelService();
            _random = new Random();
            LoadUiLanguagePreference();

            Text = "OZON-PILOT";
            AutoScaleMode = AutoScaleMode.None;
            Width = 1280;
            Height = 900;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1120, 800);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            BackColor = ShellBack;
            ApplyWindowIcon();

            InitializeControls();
            LoadAll();
            ApplyOzonSellerDefaults();
            RestorePersistentUiState();
            Shown += delegate { InitializeBrowser(null, EventArgs.Empty); };
            FormClosing += delegate { SavePersistentUiState(); };
        }

        private void ApplyWindowIcon()
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null)
                {
                    Icon = icon;
                }
            }
            catch
            {
            }
        }

        private void InitializeControls()
        {
            Panel header = BuildHeaderPanel();

            _mainTabs = new HeaderlessTabControl();
            _mainTabs.Dock = DockStyle.Fill;
            _mainTabs.ItemSize = new Size(0, 1);
            _mainTabs.SizeMode = TabSizeMode.Fixed;
            _mainTabs.Appearance = TabAppearance.Normal;
            _mainTabs.SelectedIndexChanged += delegate { SyncNavigationState(); };
            _setupTab = BuildSetupTabV5();
            _operationTab = BuildAutomationTabV5();
            _overviewTab = BuildOverviewTab();
            _assetsTab = BuildAssetsTab();
            _configTab = BuildConfigTab();
            _languageTab = BuildLanguageTab();
            _mainTabs.TabPages.Add(_setupTab);
            _mainTabs.TabPages.Add(_operationTab);
            _mainTabs.TabPages.Add(_overviewTab);
            _mainTabs.TabPages.Add(_assetsTab);
            _mainTabs.TabPages.Add(_configTab);
            _mainTabs.TabPages.Add(_languageTab);

            Panel navigation = BuildNavigationPanel();

            _statusStrip = new StatusStrip();
            _statusStrip.SizingGrip = false;
            _statusStrip.BackColor = Color.FromArgb(252, 249, 244);
            _statusLabel = new ToolStripStatusLabel();
            _statusLabel.Text = "Ready";
            _statusLabel.ForeColor = TextMuted;
            _statusStrip.Items.Add(_statusLabel);

            TableLayoutPanel shell = new TableLayoutPanel();
            shell.Dock = DockStyle.Fill;
            shell.ColumnCount = 1;
            shell.RowCount = 4;
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, header.Height));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, navigation.Height));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            header.Dock = DockStyle.Fill;
            navigation.Dock = DockStyle.Fill;
            _statusStrip.Dock = DockStyle.Fill;
            shell.Controls.Add(header, 0, 0);
            shell.Controls.Add(navigation, 0, 1);
            shell.Controls.Add(_mainTabs, 0, 2);
            shell.Controls.Add(_statusStrip, 0, 3);
            Controls.Add(shell);
            SyncNavigationState();
        }

        private void DrawMainTab(object sender, DrawItemEventArgs e)
        {
            TabControl tabs = sender as TabControl;
            if (tabs == null || e.Index < 0 || e.Index >= tabs.TabPages.Count)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = tabs.GetTabRect(e.Index);
            Rectangle clear = bounds;
            clear.Inflate(2, 8);
            using (SolidBrush clearBrush = new SolidBrush(ShellBack))
            {
                e.Graphics.FillRectangle(clearBrush, clear);
            }

            bounds.Inflate(-8, -7);
            bool selected = e.Index == tabs.SelectedIndex;

            Color fill = selected ? Ink : Color.FromArgb(250, 248, 241);
            Color border = selected ? Ink : Color.FromArgb(228, 224, 214);
            Color text = selected ? TextStrong : TextMuted;
            if (selected)
            {
                text = Color.FromArgb(242, 240, 232);
            }

            using (GraphicsPath path = CreateRoundPath(bounds, 6))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(border))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }

            TextRenderer.DrawText(
                e.Graphics,
                tabs.TabPages[e.Index].Text,
                tabs.Font,
                bounds,
                text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private Panel BuildNavigationPanel()
        {
            Panel shell = new Panel();
            shell.Dock = DockStyle.Top;
            shell.Height = 62;
            shell.BackColor = ShellBack;
            shell.Padding = new Padding(24, 8, 24, 8);

            RoundedPanel pill = new RoundedPanel();
            pill.Left = 24;
            pill.Top = 8;
            pill.Width = 596;
            pill.Height = 46;
            pill.Radius = 22;
            pill.FillColor = Color.FromArgb(252, 249, 244);
            pill.BorderColor = Color.FromArgb(238, 229, 219);
            pill.ShadowColor = Color.FromArgb(10, 160, 88, 36);
            pill.Padding = new Padding(6);

            FlowLayoutPanel row = new FlowLayoutPanel();
            row.Dock = DockStyle.Fill;
            row.WrapContents = false;
            row.AutoScroll = false;
            row.BackColor = Color.Transparent;

            _navSetupButton = CreateNavButton("准备", delegate { SelectSetupTab(); });
            _navOperationButton = CreateNavButton("运营", delegate { SelectOperationTab(); });
            _navOverviewButton = CreateNavButton("总览", delegate { SelectOverviewTab(); });
            _navAssetsButton = CreateNavButton("资产", delegate { if (_mainTabs != null && _assetsTab != null) _mainTabs.SelectedTab = _assetsTab; });
            _navConfigButton = CreateNavButton("设置", delegate { if (_mainTabs != null && _configTab != null) _mainTabs.SelectedTab = _configTab; });
            _navLanguageButton = CreateNavButton("语言", delegate { if (_mainTabs != null && _languageTab != null) _mainTabs.SelectedTab = _languageTab; });

            row.Controls.Add(_navSetupButton);
            row.Controls.Add(_navOperationButton);
            row.Controls.Add(_navOverviewButton);
            row.Controls.Add(_navAssetsButton);
            row.Controls.Add(_navConfigButton);
            row.Controls.Add(_navLanguageButton);
            pill.Controls.Add(row);
            shell.Controls.Add(pill);
            return shell;
        }

        private Button CreateNavButton(string text, EventHandler handler)
        {
            Button button = new Button();
            button.Text = text;
            button.Width = 88;
            button.Height = 34;
            button.Margin = new Padding(0, 0, 5, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(252, 249, 244);
            button.BackColor = Color.Transparent;
            button.ForeColor = TextMuted;
            button.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold, GraphicsUnit.Point, 0);
            button.Cursor = Cursors.Hand;
            button.Resize += delegate { SetRoundedRegion(button, 16); };
            SetRoundedRegion(button, 16);
            button.Click += handler;
            return button;
        }

        private void SyncNavigationState()
        {
            if (_mainTabs != null)
            {
                if (_mainTabs.SelectedTab == _setupTab)
                {
                    AttachBrowserToHost(_setupBrowserHost);
                }
                else if (_mainTabs.SelectedTab == _operationTab)
                {
                    AttachBrowserToHost(_operationBrowserHost);
                }
            }

            ApplyNavButtonState(_navSetupButton, _mainTabs != null && _mainTabs.SelectedTab == _setupTab);
            ApplyNavButtonState(_navOperationButton, _mainTabs != null && _mainTabs.SelectedTab == _operationTab);
            ApplyNavButtonState(_navOverviewButton, _mainTabs != null && _mainTabs.SelectedTab == _overviewTab);
            ApplyNavButtonState(_navAssetsButton, _mainTabs != null && _mainTabs.SelectedTab == _assetsTab);
            ApplyNavButtonState(_navConfigButton, _mainTabs != null && _mainTabs.SelectedTab == _configTab);
            ApplyNavButtonState(_navLanguageButton, _mainTabs != null && _mainTabs.SelectedTab == _languageTab);
        }

        private void AttachBrowserToHost(Panel host)
        {
            if (host == null || _browser == null || _browser.Parent == host)
            {
                return;
            }

            _browser.Parent = host;
            _browser.Dock = DockStyle.Fill;
            _browser.BringToFront();
        }

        private void ApplyNavButtonState(Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            button.BackColor = selected ? Color.FromArgb(255, 233, 214) : Color.Transparent;
            button.ForeColor = selected ? PilotGreenDark : TextMuted;
            button.FlatAppearance.BorderColor = selected ? Color.FromArgb(245, 190, 146) : Color.FromArgb(252, 249, 244);
        }

        private Panel BuildHeaderPanel()
        {
            GradientPanel panel = new GradientPanel();
            panel.Dock = DockStyle.Top;
            panel.Height = 74;
            panel.StartColor = Color.FromArgb(255, 253, 249);
            panel.EndColor = Color.FromArgb(249, 242, 234);
            panel.Angle = 0f;
            panel.Padding = new Padding(28, 12, 24, 10);

            RoundedPanel badge = new RoundedPanel();
            badge.Left = 28;
            badge.Top = 15;
            badge.Width = 44;
            badge.Height = 44;
            badge.Radius = 14;
            badge.FillColor = PilotGreen;
            badge.BorderColor = PilotGreen;
            badge.ShadowColor = Color.FromArgb(24, 255, 106, 0);

            Label badgeText = new Label();
            badgeText.Text = "OP";
            badgeText.ForeColor = Color.White;
            badgeText.BackColor = Color.Transparent;
            badgeText.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point, 0);
            badgeText.AutoSize = false;
            badgeText.Dock = DockStyle.Fill;
            badgeText.TextAlign = ContentAlignment.MiddleCenter;
            badge.Controls.Add(badgeText);

            Panel liveDot = new Panel();
            liveDot.Left = 92;
            liveDot.Top = 44;
            liveDot.Width = 7;
            liveDot.Height = 7;
            liveDot.BackColor = PilotGreen;
            SetRoundedRegion(liveDot, 4);

            Label title = new Label();
            title.Text = "OZON-PILOT";
            title.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold, GraphicsUnit.Point, 0);
            title.ForeColor = TextStrong;
            title.BackColor = Color.Transparent;
            title.AutoSize = true;
            title.Location = new Point(90, 14);

            Label subtitle = new Label();
            subtitle.Text = "1688 登录、Ozon API、选品上传和面单下载";
            subtitle.ForeColor = TextMuted;
            subtitle.BackColor = Color.Transparent;
            subtitle.AutoSize = true;
            subtitle.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Regular, GraphicsUnit.Point, 0);
            subtitle.Location = new Point(104, 39);

            _headerClockLabel = new Label();
            _headerClockLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _headerClockLabel.AutoSize = false;
            _headerClockLabel.Width = 190;
            _headerClockLabel.Height = 22;
            _headerClockLabel.Left = Width - 470;
            _headerClockLabel.Top = 19;
            _headerClockLabel.TextAlign = ContentAlignment.MiddleRight;
            _headerClockLabel.ForeColor = TextMuted;
            _headerClockLabel.BackColor = Color.Transparent;
            _headerClockLabel.Font = new Font("Consolas", 9.5F, FontStyle.Regular, GraphicsUnit.Point, 0);
            _headerClockLabel.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            _headerStatusLabel = CreateHeaderBadge("本机客户端", Width - 260, 15, PilotGreenDark);
            _headerStatusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            Label serverBadge = CreateHeaderBadge("本地模式", Width - 140, 15, TextMuted);
            serverBadge.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            panel.Controls.Add(serverBadge);
            panel.Controls.Add(_headerStatusLabel);
            panel.Controls.Add(_headerClockLabel);
            panel.Controls.Add(title);
            panel.Controls.Add(subtitle);
            panel.Controls.Add(liveDot);
            panel.Controls.Add(badge);
            Panel bottomLine = new Panel();
            bottomLine.Dock = DockStyle.Bottom;
            bottomLine.Height = 1;
            bottomLine.BackColor = Color.FromArgb(238, 229, 219);
            panel.Controls.Add(bottomLine);
            return panel;
        }

        private Label CreateHeaderBadge(string text, int left, int top, Color accent)
        {
            Label badge = new Label();
            badge.Text = text;
            badge.AutoSize = false;
            badge.Width = 108;
            badge.Height = 32;
            badge.Left = left;
            badge.Top = top;
            badge.TextAlign = ContentAlignment.MiddleCenter;
            badge.ForeColor = accent;
            badge.BackColor = Color.FromArgb(255, 242, 231);
            badge.BorderStyle = BorderStyle.None;
            badge.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold, GraphicsUnit.Point, 0);
            SetRoundedRegion(badge, 16);
            return badge;
        }

        private TabPage BuildOverviewTab()
        {
            TabPage tab = CreateTabPage("总览");

            Panel page = new Panel();
            page.Dock = DockStyle.Fill;
            page.Padding = new Padding(18);
            page.BackColor = ShellBack;

            _overviewBox = new TextBox();
            _overviewBox.Multiline = true;
            _overviewBox.ReadOnly = true;
            _overviewBox.ScrollBars = ScrollBars.None;
            _overviewBox.BackColor = Color.FromArgb(255, 253, 249);
            _overviewBox.BorderStyle = BorderStyle.None;
            _overviewBox.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            _overviewBox.Dock = DockStyle.Fill;

            RoundedPanel hero = CreateSurfacePanel(26, new Padding(24));
            hero.Dock = DockStyle.Top;
            hero.Height = 164;
            hero.Margin = new Padding(0, 0, 0, 10);
            hero.FillColor = Color.FromArgb(255, 253, 249);
            hero.BorderColor = Color.FromArgb(235, 219, 205);

            Label title = new Label();
            title.Text = "今天从哪开始";
            title.Left = 24;
            title.Top = 18;
            title.Width = 260;
            title.Height = 34;
            title.Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold, GraphicsUnit.Point, 0);
            title.ForeColor = TextStrong;

            Label subtitle = new Label();
            subtitle.Text = "这里不放技术开关，只告诉你账号、类目、结果、面单四件事是否可以继续。";
            subtitle.Left = 24;
            subtitle.Top = 50;
            subtitle.Width = 680;
            subtitle.Height = 24;
            subtitle.Font = new Font("Microsoft YaHei UI", 9.4F, FontStyle.Regular, GraphicsUnit.Point, 0);
            subtitle.ForeColor = TextMuted;

            _overviewNextActionLabel = new Label();
            _overviewNextActionLabel.Left = 24;
            _overviewNextActionLabel.Top = 78;
            _overviewNextActionLabel.Width = 760;
            _overviewNextActionLabel.Height = 38;
            _overviewNextActionLabel.BackColor = Color.FromArgb(255, 237, 222);
            _overviewNextActionLabel.ForeColor = PilotGreenDark;
            _overviewNextActionLabel.Padding = new Padding(16, 9, 12, 7);
            _overviewNextActionLabel.Font = new Font("Microsoft YaHei UI", 10.2F, FontStyle.Bold, GraphicsUnit.Point, 0);
            _overviewNextActionLabel.Resize += delegate { SetRoundedRegion(_overviewNextActionLabel, 16); };

            Button setupButton = CreateButton("去准备", delegate { SelectSetupTab(); }, true);
            setupButton.SetBounds(24, 124, 108, 36);
            Button operationButton = CreateButton("去运营", delegate { SelectOperationTab(); }, false);
            operationButton.SetBounds(146, 124, 108, 36);
            Button assetsButton = CreateButton("选类目", delegate { if (_mainTabs != null && _assetsTab != null) _mainTabs.SelectedTab = _assetsTab; }, false);
            assetsButton.SetBounds(268, 124, 108, 36);

            hero.Resize += delegate
            {
                int buttonWidth = 104;
                int gap = 12;
                setupButton.SetBounds(24, 124, buttonWidth, 36);
                operationButton.SetBounds(24 + buttonWidth + gap, 124, buttonWidth, 36);
                assetsButton.SetBounds(24 + buttonWidth * 2 + gap * 2, 124, buttonWidth, 36);
                _overviewNextActionLabel.Width = Math.Max(360, hero.ClientSize.Width - 48);
            };

            hero.Controls.Add(_overviewNextActionLabel);
            hero.Controls.Add(assetsButton);
            hero.Controls.Add(operationButton);
            hero.Controls.Add(setupButton);
            assetsButton.BringToFront();
            operationButton.BringToFront();
            setupButton.BringToFront();
            hero.Controls.Add(subtitle);
            hero.Controls.Add(title);

            TableLayoutPanel statusGrid = new TableLayoutPanel();
            statusGrid.Dock = DockStyle.Top;
            statusGrid.Height = 106;
            statusGrid.Margin = new Padding(0, 0, 0, 10);
            statusGrid.ColumnCount = 4;
            statusGrid.RowCount = 1;
            statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

            RoundedPanel setupCard = CreateOverviewStatusCard("账号", out _overviewSetupLabel);
            RoundedPanel categoryCard = CreateOverviewStatusCard("类目", out _overviewCategoryLabel);
            RoundedPanel resultCard = CreateOverviewStatusCard("商品", out _overviewResultLabel);
            RoundedPanel labelCard = CreateOverviewStatusCard("面单", out _overviewLabelLabel);
            labelCard.Margin = new Padding(0);
            statusGrid.Controls.Add(setupCard, 0, 0);
            statusGrid.Controls.Add(categoryCard, 1, 0);
            statusGrid.Controls.Add(resultCard, 2, 0);
            statusGrid.Controls.Add(labelCard, 3, 0);

            RoundedPanel detail = CreateSurfacePanel(24, new Padding(20, 48, 20, 16));
            detail.Dock = DockStyle.Fill;
            detail.FillColor = Color.FromArgb(255, 253, 249);
            detail.BorderColor = Color.FromArgb(235, 219, 205);

            Label detailTitle = new Label();
            detailTitle.Text = "现在做到哪";
            detailTitle.Left = 22;
            detailTitle.Top = 16;
            detailTitle.Width = 180;
            detailTitle.Height = 24;
            detailTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point, 0);
            detailTitle.ForeColor = TextStrong;
            detail.Controls.Add(_overviewBox);
            detail.Controls.Add(detailTitle);

            page.Controls.Add(detail);
            page.Controls.Add(statusGrid);
            page.Controls.Add(hero);
            tab.Controls.Add(page);
            return tab;
        }

        private RoundedPanel CreateOverviewStatusCard(string title, out Label valueLabel)
        {
            RoundedPanel card = CreateSurfacePanel(20, new Padding(18));
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0, 0, 12, 0);
            card.DrawShadow = false;
            card.FillColor = Color.FromArgb(255, 252, 248);
            card.BorderColor = Color.FromArgb(236, 224, 214);

            Label titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.Left = 18;
            titleLabel.Top = 16;
            titleLabel.Width = 200;
            titleLabel.Height = 22;
            titleLabel.ForeColor = TextMuted;
            titleLabel.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold, GraphicsUnit.Point, 0);

            valueLabel = new Label();
            valueLabel.Left = 18;
            valueLabel.Top = 40;
            valueLabel.Width = 220;
            valueLabel.Height = 50;
            valueLabel.ForeColor = TextStrong;
            valueLabel.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point, 0);

            card.Controls.Add(valueLabel);
            card.Controls.Add(titleLabel);
            return card;
        }

        private Control BuildOverviewRunPanel()
        {
            RoundedPanel card = CreateModernCard("最近运行", "只显示最近动作和下一步，不再堆技术名词。");

            Label status = new Label();
            status.Text = "当前：等待操作" + Environment.NewLine +
                "建议：先完成 1688 登录检测和 Ozon API 检测，再运行选品。" + Environment.NewLine +
                "面单：下载后会生成 PDF、批次汇总和每日索引。";
            status.Left = 18;
            status.Top = 72;
            status.Width = 420;
            status.Height = 110;
            status.ForeColor = TextStrong;
            status.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point, 0);
            card.Controls.Add(status);
            return card;
        }

        private RoundedPanel CreateModernCard(string title, string subtitle)
        {
            RoundedPanel card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0, 0, 0, 10);
            card.Padding = new Padding(18);
            card.FillColor = CardBack;
            card.BorderColor = LineWarm;
            card.Radius = 8;
            card.ShadowColor = Color.FromArgb(12, 24, 30, 35);

            Label titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.Left = 18;
            titleLabel.Top = 16;
            titleLabel.Width = 430;
            titleLabel.Height = 24;
            titleLabel.ForeColor = TextStrong;
            titleLabel.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point, 0);

            Label subtitleLabel = new Label();
            subtitleLabel.Text = subtitle;
            subtitleLabel.Left = 18;
            subtitleLabel.Top = 40;
            subtitleLabel.Width = 430;
            subtitleLabel.Height = 22;
            subtitleLabel.ForeColor = TextMuted;
            subtitleLabel.Font = new Font("Segoe UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point, 0);

            card.Controls.Add(titleLabel);
            card.Controls.Add(subtitleLabel);
            return card;
        }

        private TabPage BuildConfigTab()
        {
            TabPage tab = CreateTabPage("配置");

            FlowLayoutPanel actions = CreateActionBar();
            actions.Controls.Add(CreateButton("重新加载配置", delegate { LoadConfig(); UpdateOverview(); }, true));
            actions.Controls.Add(CreateButton("保存配置", SaveConfig, false));

            _configGrid = new PropertyGrid();
            _configGrid.Dock = DockStyle.Fill;
            _configGrid.HelpVisible = true;
            _configGrid.ToolbarVisible = false;
            _configGrid.PropertySort = PropertySort.Categorized;
            _configGrid.BackColor = Color.FromArgb(250, 251, 250);
            _configGrid.ViewBackColor = Color.FromArgb(250, 251, 250);

            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.Padding = new Padding(12);
            body.Controls.Add(WrapWithGroup("筛选与定价配置", _configGrid));

            tab.Controls.Add(body);
            tab.Controls.Add(actions);
            return tab;
        }

        private TabPage BuildAssetsTab()
        {
            TabPage tab = CreateTabPage("资产");

            FlowLayoutPanel actions = CreateActionBar();
            actions.Controls.Add(CreateButton("重新加载资产", delegate { LoadAssets(); }, true));

            Label searchLabel = new Label();
            searchLabel.Text = "搜索类目/规则";
            searchLabel.AutoSize = true;
            searchLabel.Margin = new Padding(20, 9, 4, 0);
            actions.Controls.Add(searchLabel);

            _assetSearchBox = new TextBox();
            _assetSearchBox.Width = 260;
            _assetSearchBox.BorderStyle = BorderStyle.FixedSingle;
            _assetSearchBox.BackColor = Color.FromArgb(250, 251, 250);
            _assetSearchBox.TextChanged += FilterAssetsChanged;
            actions.Controls.Add(_assetSearchBox);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.SplitterDistance = 460;
            split.Panel1.Padding = new Padding(12, 10, 6, 12);
            split.Panel2.Padding = new Padding(6, 10, 12, 12);

            _categoryTree = new TreeView();
            _categoryTree.Dock = DockStyle.Fill;
            _categoryTree.BackColor = Color.FromArgb(250, 251, 250);
            _categoryTree.BorderStyle = BorderStyle.None;
            _categoryTree.NodeMouseDoubleClick += UseSelectedCategoryForAutomation;

            _feeGrid = CreateGrid();
            _feeGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _feeGrid.MultiSelect = false;
            _feeGrid.CellDoubleClick += UseSelectedFeeRule;

            split.Panel1.Controls.Add(WrapWithGroup("Ozon 类目树", _categoryTree));
            split.Panel2.Controls.Add(WrapWithGroup("运费规则表", _feeGrid));

            tab.Controls.Add(split);
            tab.Controls.Add(actions);
            return tab;
        }

        private TabPage BuildLanguageTab()
        {
            TabPage tab = CreateTabPage("语言");

            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.Padding = new Padding(18);

            RoundedPanel card = new RoundedPanel();
            card.Dock = DockStyle.Top;
            card.Height = 250;
            card.Padding = new Padding(26, 24, 26, 24);
            card.FillColor = CardBack;
            card.BorderColor = LineWarm;

            Label title = new Label();
            title.Text = "界面语言";
            title.Font = new Font("Segoe UI", 15F, FontStyle.Bold, GraphicsUnit.Point, 0);
            title.ForeColor = TextStrong;
            title.AutoSize = true;
            title.Location = new Point(24, 18);

            Label desc = new Label();
            desc.Text = "切换控制台显示语言。关键词和采集内容不会被语言设置改写。";
            desc.ForeColor = TextMuted;
            desc.AutoSize = false;
            desc.Width = 780;
            desc.Height = 48;
            desc.Location = new Point(24, 56);

            Label comboLabel = new Label();
            comboLabel.Text = "当前语言";
            comboLabel.ForeColor = TextMuted;
            comboLabel.AutoSize = true;
            comboLabel.Location = new Point(24, 118);

            _languageComboBox = new ComboBox();
            _languageComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _languageComboBox.Width = 260;
            _languageComboBox.Location = new Point(24, 144);
            _languageComboBox.Items.AddRange(new object[] { "简体中文", "English", "Русский" });
            _languageComboBox.SelectedIndex = LanguageIndexFromCode(_uiLanguage);

            Button apply = CreateButton("应用语言", ApplyLanguageSelection, true);
            apply.Location = new Point(304, 140);

            Label note = new Label();
            note.Text = "Auto Sourcing 里的关键词始终保持中文；这里仅切换界面文本。";
            note.ForeColor = TextMuted;
            note.AutoSize = false;
            note.Width = 900;
            note.Height = 52;
            note.Location = new Point(24, 188);

            card.Controls.Add(title);
            card.Controls.Add(desc);
            card.Controls.Add(comboLabel);
            card.Controls.Add(_languageComboBox);
            card.Controls.Add(apply);
            card.Controls.Add(note);

            body.Controls.Add(card);
            tab.Controls.Add(body);
            return tab;
        }

        private TabPage BuildAutomationTabV2()
        {
            TabPage tab = CreateTabPage("运营");

            TableLayoutPanel page = new TableLayoutPanel();
            page.Dock = DockStyle.Fill;
            page.RowCount = 2;
            page.ColumnCount = 1;
            page.Padding = new Padding(14);
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 294));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            GradientPanel shell = new GradientPanel();
            shell.Dock = DockStyle.Fill;
            shell.StartColor = Color.FromArgb(250, 248, 240);
            shell.EndColor = Color.FromArgb(232, 238, 230);
            shell.Angle = 0f;
            shell.Padding = new Padding(18);

            TableLayoutPanel console = new TableLayoutPanel();
            console.Dock = DockStyle.Fill;
            console.RowCount = 2;
            console.ColumnCount = 4;
            console.BackColor = Color.Transparent;
            console.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            console.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            console.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            console.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
            console.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
            console.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));

            Panel heading = new Panel();
            heading.Dock = DockStyle.Fill;
            heading.BackColor = Color.Transparent;
            heading.Margin = new Padding(0, 0, 0, 12);

            Label title = new Label();
            title.Text = "运营操作舱";
            title.Left = 0;
            title.Top = 0;
            title.Width = 260;
            title.Height = 32;
            title.ForeColor = TextStrong;
            title.Font = new Font("Segoe UI", 18F, FontStyle.Bold, GraphicsUnit.Point, 0);

            Label subtitle = new Label();
            subtitle.Text = "按顺序执行：选品、上传、订单、面单。日志藏到后台，不挡住主操作。";
            subtitle.Left = 0;
            subtitle.Top = 36;
            subtitle.Width = 760;
            subtitle.Height = 24;
            subtitle.ForeColor = TextMuted;
            subtitle.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);

            heading.Controls.Add(subtitle);
            heading.Controls.Add(title);
            console.Controls.Add(heading, 0, 0);
            console.SetColumnSpan(heading, 4);

            RoundedPanel readinessCard = new RoundedPanel();
            readinessCard.Dock = DockStyle.Fill;
            readinessCard.Margin = new Padding(0, 0, 10, 0);
            readinessCard.Padding = new Padding(16);
            readinessCard.FillColor = Ink;
            readinessCard.BorderColor = Color.FromArgb(45, 55, 54);
            readinessCard.Radius = 18;

            _operationReadinessLabel = new Label();
            _operationReadinessLabel.Dock = DockStyle.Fill;
            _operationReadinessLabel.Padding = new Padding(4);
            _operationReadinessLabel.BackColor = Color.Transparent;
            _operationReadinessLabel.Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point, 0);
            _operationReadinessLabel.TextAlign = ContentAlignment.MiddleLeft;
            readinessCard.Controls.Add(_operationReadinessLabel);
            console.Controls.Add(readinessCard, 0, 1);

            RoundedPanel keywordCard = new RoundedPanel();
            keywordCard.Dock = DockStyle.Fill;
            keywordCard.Margin = new Padding(0, 0, 10, 0);
            keywordCard.Padding = new Padding(16, 14, 16, 16);
            keywordCard.FillColor = CardBack;
            keywordCard.BorderColor = LineWarm;
            keywordCard.Radius = 18;

            Label keywordTitle = new Label();
            keywordTitle.Text = "选品关键词";
            keywordTitle.Dock = DockStyle.Top;
            keywordTitle.Height = 24;
            keywordTitle.ForeColor = TextStrong;
            keywordTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point, 0);

            Label formula = new Label();
            formula.Text = "定价公式已锁定";
            formula.Dock = DockStyle.Bottom;
            formula.Height = 26;
            formula.ForeColor = PilotGreenDark;
            formula.Font = new Font("Segoe UI", 8.8F, FontStyle.Bold, GraphicsUnit.Point, 0);

            _autoKeywordsBox = CreateTextBox(0, 0, 100, "家居收纳\r\n宠物慢食碗\r\n厨房置物架");
            _autoKeywordsBox.Multiline = true;
            _autoKeywordsBox.Dock = DockStyle.Fill;
            _autoKeywordsBox.Margin = new Padding(0, 8, 0, 8);
            _autoKeywordsBox.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);

            keywordCard.Controls.Add(_autoKeywordsBox);
            keywordCard.Controls.Add(formula);
            keywordCard.Controls.Add(keywordTitle);
            console.Controls.Add(keywordCard, 1, 1);

            RoundedPanel paramsCard = new RoundedPanel();
            paramsCard.Dock = DockStyle.Fill;
            paramsCard.Margin = new Padding(0, 0, 10, 0);
            paramsCard.Padding = new Padding(16, 14, 16, 16);
            paramsCard.FillColor = CardBack;
            paramsCard.BorderColor = LineWarm;
            paramsCard.Radius = 18;

            Label paramsTitle = new Label();
            paramsTitle.Text = "循环参数";
            paramsTitle.Dock = DockStyle.Top;
            paramsTitle.Height = 26;
            paramsTitle.ForeColor = TextStrong;
            paramsTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point, 0);

            TableLayoutPanel paramsGrid = new TableLayoutPanel();
            paramsGrid.Dock = DockStyle.Fill;
            paramsGrid.ColumnCount = 2;
            paramsGrid.RowCount = 3;
            paramsGrid.Padding = new Padding(0, 8, 0, 0);
            paramsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
            paramsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++)
            {
                paramsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            }

            _autoPerKeywordBox = CreateTextBox(0, 0, 80, "5");
            _autoDetailLimitBox = CreateTextBox(0, 0, 80, "12");
            _autoRubRateBox = CreateTextBox(0, 0, 80, "12.5");
            _autoCategoryIdBox = CreateTextBox(0, 0, 80, "0");
            _autoCategoryIdBox.Visible = false;
            _autoTypeIdBox = CreateTextBox(0, 0, 80, "0");
            _autoTypeIdBox.Visible = false;

            AddParameterRow(paramsGrid, 0, "每词", _autoPerKeywordBox);
            AddParameterRow(paramsGrid, 1, "详情", _autoDetailLimitBox);
            AddParameterRow(paramsGrid, 2, "汇率", _autoRubRateBox);

            _autoPriceMultiplierBox = CreateTextBox(0, 0, 100, "售价严格按公式：成本 / (1 - 佣金 - 推广 - 利润)");
            _autoPriceMultiplierBox.ReadOnly = true;
            _autoPriceMultiplierBox.Visible = false;

            paramsCard.Controls.Add(paramsGrid);
            paramsCard.Controls.Add(paramsTitle);
            console.Controls.Add(paramsCard, 2, 1);

            RoundedPanel actionsCard = new RoundedPanel();
            actionsCard.Dock = DockStyle.Fill;
            actionsCard.Margin = new Padding(0);
            actionsCard.Padding = new Padding(16, 14, 16, 16);
            actionsCard.FillColor = CardBack;
            actionsCard.BorderColor = LineWarm;
            actionsCard.Radius = 18;

            Label actionTitle = new Label();
            actionTitle.Text = "下一步";
            actionTitle.Dock = DockStyle.Top;
            actionTitle.Height = 26;
            actionTitle.ForeColor = TextStrong;
            actionTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point, 0);

            Label actionHint = new Label();
            actionHint.Text = "按钮已按业务顺序排列。";
            actionHint.Dock = DockStyle.Top;
            actionHint.Height = 24;
            actionHint.ForeColor = TextMuted;
            actionHint.Font = new Font("Segoe UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point, 0);

            FlowLayoutPanel quickActions = new FlowLayoutPanel();
            quickActions.Dock = DockStyle.Fill;
            quickActions.Padding = new Padding(0, 8, 0, 0);
            quickActions.WrapContents = true;
            quickActions.AutoScroll = false;
            quickActions.FlowDirection = FlowDirection.LeftToRight;

            _runSourcingButton = CreateButton("选品", RunAutoSourcing, true);
            _uploadSelectedButton = CreateButton("上传到Ozon", UploadSelectedToOzon, false);
            _listFbsButton = CreateButton("取订单", ListOzonFbsPostings, false);
            _downloadLabelsButton = CreateButton("下载面单", DownloadOzonPackageLabels, false);
            _exportResultsButton = CreateButton("导出结果", ExportAutoCandidates, false);
            _runSourcingButton.Width = 78;
            _uploadSelectedButton.Width = 78;
            _listFbsButton.Width = 88;
            _downloadLabelsButton.Width = 96;
            _exportResultsButton.Width = 78;
            quickActions.Controls.Add(_runSourcingButton);
            quickActions.Controls.Add(_uploadSelectedButton);
            quickActions.Controls.Add(_listFbsButton);
            quickActions.Controls.Add(_downloadLabelsButton);
            quickActions.Controls.Add(_exportResultsButton);

            actionsCard.Controls.Add(quickActions);
            actionsCard.Controls.Add(actionHint);
            actionsCard.Controls.Add(actionTitle);
            console.Controls.Add(actionsCard, 3, 1);

            _autoProviderBox = new ComboBox();
            _autoProviderBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _autoProviderBox.Items.AddRange(new object[] { "browser" });
            _autoProviderBox.SelectedIndex = 0;
            _autoProviderBox.Visible = false;
            _autoApiKeyBox = CreateTextBox(0, 0, 120, string.Empty);
            _autoApiKeyBox.Visible = false;
            _autoApiSecretBox = CreateTextBox(0, 0, 120, string.Empty);
            _autoApiSecretBox.Visible = false;

            shell.Controls.Add(console);

            Panel workspace = new Panel();
            workspace.Dock = DockStyle.Fill;
            workspace.Padding = new Padding(0, 12, 0, 0);

            _autoResultGrid = CreateGrid();
            _autoResultGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _autoResultGrid.MultiSelect = true;
            _operationEmptyStateLabel = new Label();
            _operationEmptyStateLabel.Text = "还没有结果\r\n填写关键词，然后点击“选品”。";
            _operationEmptyStateLabel.Dock = DockStyle.Fill;
            _operationEmptyStateLabel.TextAlign = ContentAlignment.MiddleCenter;
            _operationEmptyStateLabel.ForeColor = TextMuted;
            _operationEmptyStateLabel.BackColor = Color.FromArgb(250, 251, 250);
            _operationEmptyStateLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point, 0);

            _autoLogBox = new TextBox();
            _autoLogBox.Multiline = true;
            _autoLogBox.ReadOnly = true;
            _autoLogBox.ScrollBars = ScrollBars.Vertical;
            _autoLogBox.BackColor = Color.FromArgb(250, 251, 250);
            _autoLogBox.BorderStyle = BorderStyle.None;
            _autoLogBox.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);

            Panel resultSurface = new Panel();
            resultSurface.Dock = DockStyle.Fill;
            resultSurface.Controls.Add(_autoResultGrid);
            resultSurface.Controls.Add(_operationEmptyStateLabel);
            workspace.Controls.Add(WrapWithGroup("商品结果", resultSurface));

            page.Controls.Add(shell, 0, 0);
            page.Controls.Add(workspace, 0, 1);
            tab.Controls.Add(page);
            UpdateOperationReadiness();
            UpdateOperationResultState();
            return tab;
        }

        private TabPage BuildSetupTabV2()
        {
            TabPage tab = CreateTabPage("准备");

            _browserUrlBox = new TextBox();
            _browserUrlBox.Width = 420;
            _browserUrlBox.Text = "https://www.1688.com/";

            TableLayoutPanel body = new TableLayoutPanel();
            _setupBodyLayout = body;
            body.Dock = DockStyle.Fill;
            body.RowCount = 2;
            body.ColumnCount = 1;
            body.Padding = new Padding(14);
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 224));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            Panel setupPanel = new Panel();
            _setupPanel = setupPanel;
            setupPanel.Dock = DockStyle.Fill;
            setupPanel.AutoScroll = true;
            setupPanel.BackColor = Color.FromArgb(246, 244, 235);
            setupPanel.Padding = new Padding(18);

            TableLayoutPanel setupCards = new TableLayoutPanel();
            setupCards.Dock = DockStyle.Fill;
            setupCards.ColumnCount = 3;
            setupCards.RowCount = 1;
            setupCards.BackColor = Color.Transparent;
            setupCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            setupCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            setupCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
            setupCards.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            RoundedPanel introCard = new RoundedPanel();
            introCard.Dock = DockStyle.Fill;
            introCard.Margin = new Padding(0, 0, 10, 0);
            introCard.Padding = new Padding(18);
            introCard.FillColor = Color.FromArgb(250, 248, 240);
            introCard.BorderColor = LineWarm;
            introCard.Radius = 18;

            Label title = new Label();
            title.Text = "准备区";
            title.Dock = DockStyle.Top;
            title.Height = 34;
            title.ForeColor = TextStrong;
            title.Font = new Font("Segoe UI", 17F, FontStyle.Bold, GraphicsUnit.Point, 0);

            Label desc = new Label();
            desc.Text = "第一次只需要完成这里。准备好了，这块会自动收起，把空间留给浏览器。";
            desc.Dock = DockStyle.Top;
            desc.Height = 42;
            desc.ForeColor = TextMuted;
            desc.Font = new Font("Segoe UI", 9.2F, FontStyle.Regular, GraphicsUnit.Point, 0);

            _setupActionLabel = new Label();
            _setupActionLabel.Dock = DockStyle.Top;
            _setupActionLabel.Height = 44;
            _setupActionLabel.Margin = new Padding(0, 6, 0, 8);
            _setupActionLabel.BackColor = Ink;
            _setupActionLabel.Padding = new Padding(14, 11, 12, 8);
            _setupActionLabel.ForeColor = Color.FromArgb(246, 243, 232);
            _setupActionLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point, 0);
            _setupActionLabel.Resize += delegate { SetRoundedRegion(_setupActionLabel, 18); };
            SetRoundedRegion(_setupActionLabel, 18);

            FlowLayoutPanel statusRow = new FlowLayoutPanel();
            statusRow.Dock = DockStyle.Fill;
            statusRow.WrapContents = true;
            statusRow.AutoScroll = false;
            statusRow.Padding = new Padding(0, 4, 0, 0);

            _setup1688StatusLabel = CreateSetupStatusLabel("1688：浏览器未初始化", 0, 0, PilotGreen);
            _setup1688StatusLabel.Width = 142;
            _setupOzonStatusLabel = CreateSetupStatusLabel("Ozon：账号未保存", 0, 0, Color.FromArgb(63, 96, 143));
            _setupOzonStatusLabel.Width = 142;
            statusRow.Controls.Add(_setup1688StatusLabel);
            statusRow.Controls.Add(_setupOzonStatusLabel);

            introCard.Controls.Add(statusRow);
            introCard.Controls.Add(_setupActionLabel);
            introCard.Controls.Add(desc);
            introCard.Controls.Add(title);
            setupCards.Controls.Add(introCard, 0, 0);

            RoundedPanel browserStep = new RoundedPanel();
            browserStep.Dock = DockStyle.Fill;
            browserStep.Margin = new Padding(0, 0, 10, 0);
            browserStep.Padding = new Padding(14);
            browserStep.FillColor = CardBack;
            browserStep.BorderColor = LineWarm;
            browserStep.Radius = 18;

            Label browserStepTitle = new Label();
            browserStepTitle.Text = "1. 打开并检测 1688";
            browserStepTitle.Dock = DockStyle.Top;
            browserStepTitle.Height = 28;
            browserStepTitle.ForeColor = PilotGreenDark;
            browserStepTitle.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point, 0);

            Label browserStepDesc = new Label();
            browserStepDesc.Text = "这里会检查当前浏览器里是不是真的登录了 1688。";
            browserStepDesc.Dock = DockStyle.Top;
            browserStepDesc.Height = 38;
            browserStepDesc.ForeColor = TextMuted;
            browserStepDesc.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);

            FlowLayoutPanel browserButtons = new FlowLayoutPanel();
            browserButtons.Dock = DockStyle.Fill;
            browserButtons.Padding = new Padding(0, 6, 0, 0);
            browserButtons.WrapContents = true;
            browserButtons.AutoScroll = false;
            Button initBrowserButton = CreateButton("初始化浏览器", InitializeBrowser, true);
            Button open1688Button = CreateButton("打开1688", Open1688LoginPage, false);
            Button check1688Button = CreateButton("检测登录", Check1688Login, false);
            initBrowserButton.Width = 138;
            open1688Button.Width = 108;
            check1688Button.Width = 108;
            browserButtons.Controls.Add(initBrowserButton);
            browserButtons.Controls.Add(open1688Button);
            browserButtons.Controls.Add(check1688Button);

            browserStep.Controls.Add(browserButtons);
            browserStep.Controls.Add(browserStepDesc);
            browserStep.Controls.Add(browserStepTitle);
            setupCards.Controls.Add(browserStep, 1, 0);

            RoundedPanel ozonStep = new RoundedPanel();
            ozonStep.Dock = DockStyle.Fill;
            ozonStep.Margin = new Padding(0);
            ozonStep.Padding = new Padding(14);
            ozonStep.FillColor = CardBack;
            ozonStep.BorderColor = LineWarm;
            ozonStep.Radius = 18;

            Label ozonStepTitle = new Label();
            ozonStepTitle.Text = "2. 保存 Ozon API";
            ozonStepTitle.Dock = DockStyle.Top;
            ozonStepTitle.Height = 28;
            ozonStepTitle.ForeColor = Color.FromArgb(63, 96, 143);
            ozonStepTitle.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point, 0);

            Label ozonStepDesc = new Label();
            ozonStepDesc.Text = "Client-Id 和 Api-Key 保存在本机，下次打开自动带出。";
            ozonStepDesc.Dock = DockStyle.Top;
            ozonStepDesc.Height = 32;
            ozonStepDesc.ForeColor = TextMuted;
            ozonStepDesc.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);

            TableLayoutPanel accountGrid = new TableLayoutPanel();
            accountGrid.Dock = DockStyle.Top;
            accountGrid.Height = 66;
            accountGrid.ColumnCount = 2;
            accountGrid.RowCount = 2;
            accountGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            accountGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            accountGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            accountGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            accountGrid.Padding = new Padding(0, 4, 0, 0);

            Label clientLabel = CreateFormLabel("Client-Id", 0, 0, 80);
            clientLabel.Dock = DockStyle.Fill;
            Label apiLabel = CreateFormLabel("Api-Key", 0, 0, 80);
            apiLabel.Dock = DockStyle.Fill;
            _ozonClientIdBox = CreateTextBox(0, 0, 100, DefaultOzonClientId);
            _ozonClientIdBox.Dock = DockStyle.Fill;
            _ozonClientIdBox.Leave += delegate { SavePersistentUiState(); UpdateSetupStatus(); };
            _ozonApiKeyBox = CreateTextBox(0, 0, 100, DefaultOzonApiKey);
            _ozonApiKeyBox.Dock = DockStyle.Fill;
            _ozonApiKeyBox.PasswordChar = '*';
            _ozonApiKeyBox.Leave += delegate { SavePersistentUiState(); UpdateSetupStatus(); };
            accountGrid.Controls.Add(clientLabel, 0, 0);
            accountGrid.Controls.Add(apiLabel, 1, 0);
            accountGrid.Controls.Add(_ozonClientIdBox, 0, 1);
            accountGrid.Controls.Add(_ozonApiKeyBox, 1, 1);

            FlowLayoutPanel ozonButtons = new FlowLayoutPanel();
            ozonButtons.Dock = DockStyle.Fill;
            ozonButtons.Padding = new Padding(0, 4, 0, 0);
            ozonButtons.WrapContents = true;
            ozonButtons.AutoScroll = false;
            Button openApiButton = CreateButton("打开Ozon后台", OpenOzonApiPage, false);
            Button checkApiButton = CreateButton("检测并保存", CheckOzonCredentials, true);
            Button saveOnlyButton = CreateButton("仅保存", delegate { SavePersistentUiState(); _ozonCredentialsVerified = false; UpdateSetupStatus(); UpdateOperationReadiness(); SetStatus("账号已保存，但还没有验证 API 是否可用。"); }, false);
            openApiButton.Width = 132;
            checkApiButton.Width = 132;
            saveOnlyButton.Width = 84;
            ozonButtons.Controls.Add(openApiButton);
            ozonButtons.Controls.Add(checkApiButton);
            ozonButtons.Controls.Add(saveOnlyButton);

            ozonStep.Controls.Add(ozonButtons);
            ozonStep.Controls.Add(accountGrid);
            ozonStep.Controls.Add(ozonStepDesc);
            ozonStep.Controls.Add(ozonStepTitle);
            setupCards.Controls.Add(ozonStep, 2, 0);

            Panel readyBar = new Panel();
            readyBar.Dock = DockStyle.Fill;
            readyBar.Tag = "ready";
            readyBar.Visible = false;
            readyBar.BackColor = Color.Transparent;

            _setupCompletedLabel = new Label();
            _setupCompletedLabel.Text = "准备完成：1688 已登录，Ozon API 已验证。";
            _setupCompletedLabel.Dock = DockStyle.Left;
            _setupCompletedLabel.Width = 520;
            _setupCompletedLabel.ForeColor = PilotGreen;
            _setupCompletedLabel.BackColor = Color.Transparent;
            _setupCompletedLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point, 0);
            _setupCompletedLabel.TextAlign = ContentAlignment.MiddleLeft;

            FlowLayoutPanel readyActions = new FlowLayoutPanel();
            readyActions.Dock = DockStyle.Right;
            readyActions.Width = 390;
            readyActions.FlowDirection = FlowDirection.LeftToRight;
            readyActions.WrapContents = false;
            readyActions.Padding = new Padding(0, 8, 0, 0);
            readyActions.Controls.Add(CreateButton("去运营", GoOperationTab, true));
            readyActions.Controls[0].Tag = "ready";
            readyActions.Controls.Add(CreateButton("重新检测", RecheckSetup, false));
            readyActions.Controls[1].Tag = "ready";
            readyActions.Controls.Add(CreateButton("修改账号", ExpandSetupPanel, false));
            readyActions.Controls[2].Tag = "ready";

            readyBar.Controls.Add(readyActions);
            readyBar.Controls.Add(_setupCompletedLabel);

            setupPanel.Controls.Add(readyBar);
            setupPanel.Controls.Add(setupCards);

            _browserStatusLabel = new Label();
            _browserStatusLabel.Dock = DockStyle.Bottom;
            _browserStatusLabel.Height = 32;
            _browserStatusLabel.ForeColor = Color.FromArgb(210, 216, 212);
            _browserStatusLabel.BackColor = Ink;
            _browserStatusLabel.Padding = new Padding(14, 8, 12, 6);
            _browserStatusLabel.Text = "浏览器尚未初始化。点上方“初始化浏览器”。";

            _browser = new WebView2();
            _browser.ZoomFactor = 0.9d;

            Panel browserPanel = new Panel();
            browserPanel.Dock = DockStyle.Fill;
            browserPanel.BackColor = Ink;
            browserPanel.Padding = new Padding(12, 44, 12, 38);

            Label browserTitle = new Label();
            browserTitle.Text = "1688 工作浏览器";
            browserTitle.Left = 18;
            browserTitle.Top = 13;
            browserTitle.AutoSize = true;
            browserTitle.ForeColor = Color.FromArgb(246, 243, 232);
            browserTitle.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point, 0);

            _browser.Dock = DockStyle.Fill;
            browserPanel.Controls.Add(_browser);
            browserPanel.Controls.Add(browserTitle);
            browserPanel.Controls.Add(_browserStatusLabel);

            body.Controls.Add(setupPanel, 0, 0);
            body.Controls.Add(browserPanel, 0, 1);
            tab.Controls.Add(body);
            UpdateSetupStatus();
            return tab;
        }


        private TabPage BuildAutomationTab()
        {
            TabPage tab = CreateTabPage("运营");

            TableLayoutPanel page = new TableLayoutPanel();
            page.Dock = DockStyle.Fill;
            page.RowCount = 2;
            page.ColumnCount = 1;
            page.Padding = new Padding(16, 14, 16, 16);
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 286));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            GradientPanel console = new GradientPanel();
            console.Dock = DockStyle.Fill;
            console.StartColor = Color.FromArgb(250, 248, 240);
            console.EndColor = Color.FromArgb(232, 238, 230);
            console.Angle = 0f;
            console.Padding = new Padding(20);

            Label title = new Label();
            title.Text = "运营操作舱";
            title.Left = 24;
            title.Top = 18;
            title.Width = 220;
            title.Height = 34;
            title.ForeColor = TextStrong;
            title.Font = new Font("Segoe UI", 18F, FontStyle.Bold, GraphicsUnit.Point, 0);
            console.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "这里不放日志墙，只保留今天真正要点的动作。先选品，再上传，再取订单和面单。";
            subtitle.Left = 24;
            subtitle.Top = 56;
            subtitle.Width = 620;
            subtitle.Height = 24;
            subtitle.ForeColor = TextMuted;
            subtitle.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            console.Controls.Add(subtitle);

            _operationReadinessLabel = new Label();
            _operationReadinessLabel.Left = 24;
            _operationReadinessLabel.Top = 96;
            _operationReadinessLabel.Width = 330;
            _operationReadinessLabel.Height = 118;
            _operationReadinessLabel.Padding = new Padding(20, 18, 16, 12);
            _operationReadinessLabel.BackColor = Ink;
            _operationReadinessLabel.BorderStyle = BorderStyle.None;
            _operationReadinessLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point, 0);
            _operationReadinessLabel.Resize += delegate { SetRoundedRegion(_operationReadinessLabel, 18); };
            SetRoundedRegion(_operationReadinessLabel, 18);
            console.Controls.Add(_operationReadinessLabel);
            UpdateOperationReadiness();

            Label keywordTitle = CreateSectionLabel("选品关键词", 370, 92);
            console.Controls.Add(keywordTitle);

            _autoKeywordsBox = CreateTextBox(370, 122, 250, "家居收纳\r\n宠物慢食碗\r\n厨房置物架");
            _autoKeywordsBox.Multiline = true;
            _autoKeywordsBox.Height = 88;
            _autoKeywordsBox.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            console.Controls.Add(_autoKeywordsBox);

            Label formula = new Label();
            formula.Text = "定价公式已锁定：售价 = 成本 / (1 - 平台佣金 - 推广费用 - 目标利润)";
            formula.Left = 370;
            formula.Top = 220;
            formula.Width = 500;
            formula.Height = 22;
            formula.ForeColor = PilotGreenDark;
            formula.Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 0);
            console.Controls.Add(formula);

            Label paramsTitle = CreateSectionLabel("循环参数", 650, 92);
            console.Controls.Add(paramsTitle);

            console.Controls.Add(CreateFormLabel("每词", 650, 124, 46));
            _autoPerKeywordBox = CreateTextBox(698, 124, 52, "5");
            console.Controls.Add(_autoPerKeywordBox);

            console.Controls.Add(CreateFormLabel("详情", 766, 124, 46));
            _autoDetailLimitBox = CreateTextBox(814, 124, 52, "12");
            console.Controls.Add(_autoDetailLimitBox);

            console.Controls.Add(CreateFormLabel("汇率", 650, 176, 46));
            _autoRubRateBox = CreateTextBox(698, 176, 66, "12.5");
            console.Controls.Add(_autoRubRateBox);

            console.Controls.Add(CreateFormLabel("类目", 782, 176, 46));
            _autoCategoryIdBox = CreateTextBox(830, 176, 64, "0");
            console.Controls.Add(_autoCategoryIdBox);

            console.Controls.Add(CreateFormLabel("类型", 650, 228, 46));
            _autoTypeIdBox = CreateTextBox(698, 228, 64, "0");
            console.Controls.Add(_autoTypeIdBox);

            _autoPriceMultiplierBox = CreateTextBox(370, 248, 500, "售价严格按公式：成本 / (1 - 佣金 - 推广 - 利润)");
            _autoPriceMultiplierBox.ReadOnly = true;
            _autoPriceMultiplierBox.BackColor = Color.FromArgb(242, 247, 244);
            _autoPriceMultiplierBox.Visible = false;
            console.Controls.Add(_autoPriceMultiplierBox);

            _autoProviderBox = new ComboBox();
            _autoProviderBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _autoProviderBox.Left = 420;
            _autoProviderBox.Top = 20;
            _autoProviderBox.Width = 120;
            _autoProviderBox.Items.AddRange(new object[] { "browser" });
            _autoProviderBox.SelectedIndex = 0;
            _autoProviderBox.Visible = false;

            _autoApiKeyBox = CreateTextBox(420, 20, 120, string.Empty);
            _autoApiKeyBox.Visible = false;

            _autoApiSecretBox = CreateTextBox(420, 20, 120, string.Empty);
            _autoApiSecretBox.Visible = false;

            Label actionTitle = CreateSectionLabel("下一步", 910, 92);
            console.Controls.Add(actionTitle);

            Label actionHint = new Label();
            actionHint.Text = "按钮按业务顺序排好。没有准备好时会锁住。";
            actionHint.Left = 910;
            actionHint.Top = 120;
            actionHint.Width = 190;
            actionHint.Height = 42;
            actionHint.ForeColor = TextMuted;
            actionHint.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            console.Controls.Add(actionHint);

            FlowLayoutPanel quickActions = new FlowLayoutPanel();
            quickActions.Left = 910;
            quickActions.Top = 138;
            quickActions.Width = 190;
            quickActions.Height = 128;
            quickActions.WrapContents = true;
            quickActions.AutoScroll = false;
            quickActions.FlowDirection = FlowDirection.LeftToRight;
            _runSourcingButton = CreateButton("选品", RunAutoSourcing, true);
            _uploadSelectedButton = CreateButton("上传", UploadSelectedToOzon, false);
            _listFbsButton = CreateButton("FBS订单", ListOzonFbsPostings, false);
            _downloadLabelsButton = CreateButton("面单PDF", DownloadOzonPackageLabels, false);
            _exportResultsButton = CreateButton("导出结果", ExportAutoCandidates, false);
            quickActions.Controls.Add(_runSourcingButton);
            quickActions.Controls.Add(_uploadSelectedButton);
            quickActions.Controls.Add(_listFbsButton);
            quickActions.Controls.Add(_downloadLabelsButton);
            quickActions.Controls.Add(_exportResultsButton);
            console.Controls.Add(quickActions);

            Panel workspace = new Panel();
            workspace.Dock = DockStyle.Fill;
            workspace.Padding = new Padding(0, 12, 0, 0);

            _autoResultGrid = CreateGrid();
            _autoResultGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _autoResultGrid.MultiSelect = true;
            _operationEmptyStateLabel = new Label();
            _operationEmptyStateLabel.Text = "还没有结果\r\n在上方填写关键词，然后点击“1 选品”。";
            _operationEmptyStateLabel.Dock = DockStyle.Fill;
            _operationEmptyStateLabel.TextAlign = ContentAlignment.MiddleCenter;
            _operationEmptyStateLabel.ForeColor = TextMuted;
            _operationEmptyStateLabel.BackColor = Color.FromArgb(250, 251, 250);
            _operationEmptyStateLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point, 0);

            _autoLogBox = new TextBox();
            _autoLogBox.Multiline = true;
            _autoLogBox.ReadOnly = true;
            _autoLogBox.ScrollBars = ScrollBars.Vertical;
            _autoLogBox.BackColor = Color.FromArgb(250, 251, 250);
            _autoLogBox.BorderStyle = BorderStyle.None;
            _autoLogBox.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);

            Panel resultSurface = new Panel();
            resultSurface.Dock = DockStyle.Fill;
            resultSurface.Controls.Add(_autoResultGrid);
            resultSurface.Controls.Add(_operationEmptyStateLabel);
            workspace.Controls.Add(WrapWithGroup("商品结果", resultSurface));
            UpdateOperationReadiness();
            UpdateOperationResultState();

            page.Controls.Add(console, 0, 0);
            page.Controls.Add(workspace, 0, 1);
            tab.Controls.Add(page);
            return tab;
        }

        private TabPage BuildSetupTabV3()
        {
            TabPage tab = CreateTabPage("准备");

            _browserUrlBox = new TextBox();
            _browserUrlBox.Width = 420;
            _browserUrlBox.Text = "https://www.1688.com/";
            _setupBodyLayout = null;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 2;
            layout.RowCount = 1;
            layout.Padding = new Padding(14);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            RoundedPanel setupPanel = new RoundedPanel();
            _setupPanel = setupPanel;
            setupPanel.Dock = DockStyle.Fill;
            setupPanel.Margin = new Padding(0, 0, 12, 0);
            setupPanel.Padding = new Padding(18);
            setupPanel.FillColor = Color.FromArgb(250, 248, 240);
            setupPanel.BorderColor = LineWarm;
            setupPanel.Radius = 18;

            Label title = new Label();
            title.Text = "准备区";
            title.Left = 18;
            title.Top = 20;
            title.Width = 250;
            title.Height = 34;
            title.ForeColor = TextStrong;
            title.Font = new Font("Segoe UI", 17F, FontStyle.Bold, GraphicsUnit.Point, 0);
            setupPanel.Controls.Add(title);

            Label desc = new Label();
            desc.Text = "先完成 1688 登录和 Ozon API。完成后这里会收起，浏览器继续工作。";
            desc.Left = 18;
            desc.Top = 58;
            desc.Width = 274;
            desc.Height = 48;
            desc.ForeColor = TextMuted;
            desc.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            setupPanel.Controls.Add(desc);

            _setupActionLabel = new Label();
            _setupActionLabel.Left = 18;
            _setupActionLabel.Top = 116;
            _setupActionLabel.Width = 274;
            _setupActionLabel.Height = 46;
            _setupActionLabel.BackColor = Ink;
            _setupActionLabel.Padding = new Padding(14, 12, 12, 8);
            _setupActionLabel.ForeColor = Color.FromArgb(246, 243, 232);
            _setupActionLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point, 0);
            _setupActionLabel.Resize += delegate { SetRoundedRegion(_setupActionLabel, 16); };
            setupPanel.Controls.Add(_setupActionLabel);

            _setup1688StatusLabel = CreateSetupStatusLabel("1688：浏览器未初始化", 18, 174, PilotGreen);
            _setup1688StatusLabel.Width = 274;
            setupPanel.Controls.Add(_setup1688StatusLabel);

            _setupOzonStatusLabel = CreateSetupStatusLabel("Ozon：账号未保存", 18, 208, Color.FromArgb(63, 96, 143));
            _setupOzonStatusLabel.Width = 274;
            setupPanel.Controls.Add(_setupOzonStatusLabel);

            Label step1688 = CreateSectionLabel("1. 1688 登录", 18, 252);
            step1688.Width = 274;
            step1688.ForeColor = PilotGreenDark;
            setupPanel.Controls.Add(step1688);

            Button initButton = CreateButton("初始化浏览器", InitializeBrowser, true);
            initButton.Left = 18;
            initButton.Top = 286;
            initButton.Width = 136;
            setupPanel.Controls.Add(initButton);

            Button open1688Button = CreateButton("1688登录页", Open1688LoginPage, false);
            open1688Button.Left = 166;
            open1688Button.Top = 286;
            open1688Button.Width = 126;
            setupPanel.Controls.Add(open1688Button);

            Button check1688Button = CreateButton("检测登录", Check1688Login, false);
            check1688Button.Left = 18;
            check1688Button.Top = 332;
            check1688Button.Width = 136;
            setupPanel.Controls.Add(check1688Button);

            Label stepOzon = CreateSectionLabel("2. Ozon API", 18, 390);
            stepOzon.Width = 274;
            stepOzon.ForeColor = Color.FromArgb(63, 96, 143);
            setupPanel.Controls.Add(stepOzon);

            Label clientLabel = CreateFormLabel("Client-Id", 18, 424, 120);
            setupPanel.Controls.Add(clientLabel);
            _ozonClientIdBox = CreateTextBox(18, 452, 274, DefaultOzonClientId);
            _ozonClientIdBox.Leave += delegate { SavePersistentUiState(); UpdateSetupStatus(); };
            setupPanel.Controls.Add(_ozonClientIdBox);

            Label apiLabel = CreateFormLabel("Api-Key", 18, 490, 120);
            setupPanel.Controls.Add(apiLabel);
            _ozonApiKeyBox = CreateTextBox(18, 518, 274, DefaultOzonApiKey);
            _ozonApiKeyBox.PasswordChar = '*';
            _ozonApiKeyBox.Leave += delegate { SavePersistentUiState(); UpdateSetupStatus(); };
            setupPanel.Controls.Add(_ozonApiKeyBox);

            Button openApi = CreateButton("打开Ozon后台", OpenOzonApiPage, false);
            openApi.Left = 18;
            openApi.Top = 568;
            openApi.Width = 274;
            setupPanel.Controls.Add(openApi);

            Button checkApi = CreateButton("检测并保存", CheckOzonCredentials, true);
            checkApi.Left = 18;
            checkApi.Top = 614;
            checkApi.Width = 136;
            setupPanel.Controls.Add(checkApi);

            Button saveOnly = CreateButton("只保存", delegate { SavePersistentUiState(); _ozonCredentialsVerified = false; UpdateSetupStatus(); UpdateOperationReadiness(); SetStatus("账号已保存，但还没有验证 API 是否可用。"); }, false);
            saveOnly.Left = 166;
            saveOnly.Top = 614;
            saveOnly.Width = 126;
            setupPanel.Controls.Add(saveOnly);

            _setupCompletedLabel = new Label();
            _setupCompletedLabel.Text = "准备完成\r\n1688 已登录，Ozon API 已验证。";
            _setupCompletedLabel.Left = 18;
            _setupCompletedLabel.Top = 20;
            _setupCompletedLabel.Width = 274;
            _setupCompletedLabel.Height = 60;
            _setupCompletedLabel.Tag = "ready";
            _setupCompletedLabel.Visible = false;
            _setupCompletedLabel.ForeColor = PilotGreen;
            _setupCompletedLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point, 0);
            setupPanel.Controls.Add(_setupCompletedLabel);

            Button readyGo = CreateButton("去运营", GoOperationTab, true);
            readyGo.Left = 18;
            readyGo.Top = 96;
            readyGo.Width = 180;
            readyGo.Tag = "ready";
            readyGo.Visible = false;
            setupPanel.Controls.Add(readyGo);

            Button readyRecheck = CreateButton("重新检测", RecheckSetup, false);
            readyRecheck.Left = 18;
            readyRecheck.Top = 142;
            readyRecheck.Width = 180;
            readyRecheck.Tag = "ready";
            readyRecheck.Visible = false;
            setupPanel.Controls.Add(readyRecheck);

            Button readyEdit = CreateButton("修改账号", ExpandSetupPanel, false);
            readyEdit.Left = 18;
            readyEdit.Top = 188;
            readyEdit.Width = 180;
            readyEdit.Tag = "ready";
            readyEdit.Visible = false;
            setupPanel.Controls.Add(readyEdit);

            _browserStatusLabel = new Label();
            _browserStatusLabel.Dock = DockStyle.Bottom;
            _browserStatusLabel.Height = 32;
            _browserStatusLabel.ForeColor = Color.FromArgb(210, 216, 212);
            _browserStatusLabel.BackColor = Ink;
            _browserStatusLabel.Padding = new Padding(14, 8, 12, 6);
            _browserStatusLabel.Text = "浏览器尚未初始化。点左侧“初始化浏览器”。";

            _browser = new WebView2();
            _browser.ZoomFactor = 0.85d;

            Panel browserPanel = new Panel();
            browserPanel.Dock = DockStyle.Fill;
            browserPanel.BackColor = Ink;
            browserPanel.Padding = new Padding(12, 42, 12, 38);

            Label browserTitle = new Label();
            browserTitle.Text = "1688 工作浏览器";
            browserTitle.Left = 18;
            browserTitle.Top = 12;
            browserTitle.AutoSize = true;
            browserTitle.ForeColor = Color.FromArgb(246, 243, 232);
            browserTitle.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point, 0);

            _browser.Dock = DockStyle.Fill;
            browserPanel.Controls.Add(_browser);
            browserPanel.Controls.Add(browserTitle);
            browserPanel.Controls.Add(_browserStatusLabel);

            layout.Controls.Add(setupPanel, 0, 0);
            layout.Controls.Add(browserPanel, 1, 0);
            tab.Controls.Add(layout);
            UpdateSetupStatus();
            return tab;
        }

        private TabPage BuildAutomationTabV3()
        {
            TabPage tab = CreateTabPage("运营");

            TableLayoutPanel page = new TableLayoutPanel();
            page.Dock = DockStyle.Fill;
            page.ColumnCount = 2;
            page.RowCount = 1;
            page.Padding = new Padding(14);
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            RoundedPanel controls = new RoundedPanel();
            controls.Dock = DockStyle.Fill;
            controls.Margin = new Padding(0, 0, 12, 0);
            controls.Padding = new Padding(18);
            controls.FillColor = Color.FromArgb(250, 248, 240);
            controls.BorderColor = LineWarm;
            controls.Radius = 18;

            Label title = new Label();
            title.Text = "运营操作舱";
            title.Left = 18;
            title.Top = 20;
            title.Width = 274;
            title.Height = 34;
            title.ForeColor = TextStrong;
            title.Font = new Font("Segoe UI", 17F, FontStyle.Bold, GraphicsUnit.Point, 0);
            controls.Controls.Add(title);

            _operationReadinessLabel = new Label();
            _operationReadinessLabel.Left = 18;
            _operationReadinessLabel.Top = 66;
            _operationReadinessLabel.Width = 274;
            _operationReadinessLabel.Height = 88;
            _operationReadinessLabel.BackColor = Ink;
            _operationReadinessLabel.Padding = new Padding(16, 16, 14, 10);
            _operationReadinessLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point, 0);
            _operationReadinessLabel.Resize += delegate { SetRoundedRegion(_operationReadinessLabel, 18); };
            controls.Controls.Add(_operationReadinessLabel);

            Label keywordTitle = CreateSectionLabel("选品关键词", 18, 176);
            controls.Controls.Add(keywordTitle);
            _autoKeywordsBox = CreateTextBox(18, 208, 274, "家居收纳\r\n宠物慢食碗\r\n厨房置物架");
            _autoKeywordsBox.Multiline = true;
            _autoKeywordsBox.Height = 92;
            _autoKeywordsBox.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            controls.Controls.Add(_autoKeywordsBox);

            Label formula = new Label();
            formula.Text = "定价公式已锁定";
            formula.Left = 18;
            formula.Top = 308;
            formula.Width = 274;
            formula.Height = 24;
            formula.ForeColor = PilotGreenDark;
            formula.Font = new Font("Segoe UI", 9.2F, FontStyle.Bold, GraphicsUnit.Point, 0);
            controls.Controls.Add(formula);

            Label paramsTitle = CreateSectionLabel("循环参数", 18, 356);
            controls.Controls.Add(paramsTitle);
            controls.Controls.Add(CreateFormLabel("每词", 18, 388, 48));
            _autoPerKeywordBox = CreateTextBox(76, 388, 216, "5");
            controls.Controls.Add(_autoPerKeywordBox);
            controls.Controls.Add(CreateFormLabel("详情", 18, 428, 48));
            _autoDetailLimitBox = CreateTextBox(76, 428, 216, "12");
            controls.Controls.Add(_autoDetailLimitBox);
            controls.Controls.Add(CreateFormLabel("汇率", 18, 468, 48));
            _autoRubRateBox = CreateTextBox(76, 468, 216, "12.5");
            controls.Controls.Add(_autoRubRateBox);
            _autoCategoryIdBox = CreateTextBox(0, 0, 80, "0");
            _autoCategoryIdBox.Visible = false;
            controls.Controls.Add(_autoCategoryIdBox);
            _autoTypeIdBox = CreateTextBox(0, 0, 80, "0");
            _autoTypeIdBox.Visible = false;
            controls.Controls.Add(_autoTypeIdBox);

            _autoPriceMultiplierBox = CreateTextBox(0, 0, 100, "售价严格按公式：成本 / (1 - 佣金 - 推广 - 利润)");
            _autoPriceMultiplierBox.ReadOnly = true;
            _autoPriceMultiplierBox.Visible = false;
            controls.Controls.Add(_autoPriceMultiplierBox);

            Label actionTitle = CreateSectionLabel("下一步", 18, 500);
            controls.Controls.Add(actionTitle);
            _runSourcingButton = CreateButton("选品", RunAutoSourcing, true);
            _runSourcingButton.Left = 18;
            _runSourcingButton.Top = 534;
            _runSourcingButton.Width = 136;
            controls.Controls.Add(_runSourcingButton);
            _uploadSelectedButton = CreateButton("上传Ozon", UploadSelectedToOzon, false);
            _uploadSelectedButton.Left = 166;
            _uploadSelectedButton.Top = 534;
            _uploadSelectedButton.Width = 126;
            controls.Controls.Add(_uploadSelectedButton);
            _listFbsButton = CreateButton("取订单", ListOzonFbsPostings, false);
            _listFbsButton.Left = 18;
            _listFbsButton.Top = 580;
            _listFbsButton.Width = 136;
            controls.Controls.Add(_listFbsButton);
            _downloadLabelsButton = CreateButton("下载面单", DownloadOzonPackageLabels, false);
            _downloadLabelsButton.Left = 166;
            _downloadLabelsButton.Top = 580;
            _downloadLabelsButton.Width = 126;
            controls.Controls.Add(_downloadLabelsButton);
            _exportResultsButton = CreateButton("导出结果", ExportAutoCandidates, false);
            _exportResultsButton.Left = 18;
            _exportResultsButton.Top = 626;
            _exportResultsButton.Width = 136;
            controls.Controls.Add(_exportResultsButton);

            _autoProviderBox = new ComboBox();
            _autoProviderBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _autoProviderBox.Items.AddRange(new object[] { "browser" });
            _autoProviderBox.SelectedIndex = 0;
            _autoProviderBox.Visible = false;
            _autoApiKeyBox = CreateTextBox(0, 0, 120, string.Empty);
            _autoApiKeyBox.Visible = false;
            _autoApiSecretBox = CreateTextBox(0, 0, 120, string.Empty);
            _autoApiSecretBox.Visible = false;

            Panel workspace = new Panel();
            workspace.Dock = DockStyle.Fill;
            _autoResultGrid = CreateGrid();
            _autoResultGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _autoResultGrid.MultiSelect = true;
            _operationEmptyStateLabel = new Label();
            _operationEmptyStateLabel.Text = "还没有结果\r\n填写关键词，然后点击“选品”。";
            _operationEmptyStateLabel.Dock = DockStyle.Fill;
            _operationEmptyStateLabel.TextAlign = ContentAlignment.MiddleCenter;
            _operationEmptyStateLabel.ForeColor = TextMuted;
            _operationEmptyStateLabel.BackColor = Color.FromArgb(250, 251, 250);
            _operationEmptyStateLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point, 0);
            _autoLogBox = new TextBox();
            _autoLogBox.Multiline = true;
            _autoLogBox.ReadOnly = true;
            _autoLogBox.ScrollBars = ScrollBars.Vertical;
            _autoLogBox.BackColor = Color.FromArgb(250, 251, 250);
            _autoLogBox.BorderStyle = BorderStyle.None;
            _autoLogBox.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            Panel resultSurface = new Panel();
            resultSurface.Dock = DockStyle.Fill;
            resultSurface.Controls.Add(_autoResultGrid);
            resultSurface.Controls.Add(_operationEmptyStateLabel);
            workspace.Controls.Add(WrapWithGroup("商品结果", resultSurface));

            page.Controls.Add(controls, 0, 0);
            page.Controls.Add(workspace, 1, 0);
            tab.Controls.Add(page);
            UpdateOperationReadiness();
            UpdateOperationResultState();
            return tab;
        }

        private TabPage BuildSetupTabV4()
        {
            TabPage tab = CreateTabPage("准备");

            _browserUrlBox = new TextBox();
            _browserUrlBox.Width = 420;
            _browserUrlBox.Text = "https://www.1688.com/";

            TableLayoutPanel layout = new TableLayoutPanel();
            _setupBodyLayout = layout;
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 2;
            layout.RowCount = 1;
            layout.Padding = new Padding(14);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 332));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            RoundedPanel setupPanel = new RoundedPanel();
            _setupPanel = setupPanel;
            setupPanel.Dock = DockStyle.Fill;
            setupPanel.Margin = new Padding(0, 0, 12, 0);
            setupPanel.Padding = new Padding(18);
            setupPanel.FillColor = Color.FromArgb(250, 248, 240);
            setupPanel.BorderColor = LineWarm;
            setupPanel.Radius = 18;

            FlowLayoutPanel setupFlow = new FlowLayoutPanel();
            setupFlow.Dock = DockStyle.Fill;
            setupFlow.FlowDirection = FlowDirection.TopDown;
            setupFlow.WrapContents = false;
            setupFlow.AutoScroll = true;
            setupFlow.Tag = "normal";

            Label title = CreateFlowText("准备区", 302, 34, 17F, FontStyle.Bold, TextStrong);
            Label desc = CreateFlowText("先完成 1688 登录和 Ozon API。完成后这里会收起，浏览器继续工作。", 302, 52, 9F, FontStyle.Regular, TextMuted);
            _setupActionLabel = CreateFlowText("下一步：初始化浏览器", 302, 46, 10F, FontStyle.Bold, Color.FromArgb(246, 243, 232));
            _setupActionLabel.BackColor = Ink;
            _setupActionLabel.Padding = new Padding(14, 12, 12, 8);
            _setupActionLabel.Resize += delegate { SetRoundedRegion(_setupActionLabel, 16); };

            _setup1688StatusLabel = CreateSetupStatusLabel("1688：浏览器未初始化", 0, 0, PilotGreen);
            _setup1688StatusLabel.Width = 302;
            _setup1688StatusLabel.Height = 30;
            _setupOzonStatusLabel = CreateSetupStatusLabel("Ozon：账号未保存", 0, 0, Color.FromArgb(63, 96, 143));
            _setupOzonStatusLabel.Width = 302;
            _setupOzonStatusLabel.Height = 30;

            Label step1688 = CreateFlowText("1. 1688 登录", 302, 28, 10F, FontStyle.Bold, PilotGreenDark);
            FlowLayoutPanel browserButtons = CreateFlowRow(302, 88);
            Button initButton = CreateButton("初始化浏览器", InitializeBrowser, true);
            initButton.Width = 136;
            Button open1688Button = CreateButton("打开1688", Open1688LoginPage, false);
            open1688Button.Width = 136;
            Button check1688Button = CreateButton("检测登录", Check1688Login, false);
            check1688Button.Width = 136;
            browserButtons.Controls.Add(initButton);
            browserButtons.Controls.Add(open1688Button);
            browserButtons.Controls.Add(check1688Button);

            Label stepOzon = CreateFlowText("2. Ozon API", 302, 28, 10F, FontStyle.Bold, Color.FromArgb(63, 96, 143));
            Label clientLabel = CreateFlowText("Client-Id", 302, 24, 9F, FontStyle.Bold, TextMuted);
            _ozonClientIdBox = CreateTextBox(0, 0, 302, DefaultOzonClientId);
            _ozonClientIdBox.Leave += delegate { SavePersistentUiState(); UpdateSetupStatus(); };
            Label apiLabel = CreateFlowText("Api-Key", 302, 24, 9F, FontStyle.Bold, TextMuted);
            _ozonApiKeyBox = CreateTextBox(0, 0, 302, DefaultOzonApiKey);
            _ozonApiKeyBox.PasswordChar = '*';
            _ozonApiKeyBox.Leave += delegate { SavePersistentUiState(); UpdateSetupStatus(); };
            Button openApi = CreateButton("打开Ozon后台", OpenOzonApiPage, false);
            openApi.Width = 302;
            FlowLayoutPanel ozonButtons = CreateFlowRow(302, 44);
            Button checkApi = CreateButton("检测并保存", CheckOzonCredentials, true);
            checkApi.Width = 146;
            Button saveOnly = CreateButton("只保存不检测", delegate { SavePersistentUiState(); _ozonCredentialsVerified = false; UpdateSetupStatus(); UpdateOperationReadiness(); SetStatus("账号已保存，但还没有验证 API 是否可用。"); }, false);
            saveOnly.Width = 146;
            ozonButtons.Controls.Add(checkApi);
            ozonButtons.Controls.Add(saveOnly);

            setupFlow.Controls.Add(title);
            setupFlow.Controls.Add(desc);
            setupFlow.Controls.Add(_setupActionLabel);
            setupFlow.Controls.Add(_setup1688StatusLabel);
            setupFlow.Controls.Add(_setupOzonStatusLabel);
            setupFlow.Controls.Add(step1688);
            setupFlow.Controls.Add(browserButtons);
            setupFlow.Controls.Add(stepOzon);
            setupFlow.Controls.Add(clientLabel);
            setupFlow.Controls.Add(_ozonClientIdBox);
            setupFlow.Controls.Add(apiLabel);
            setupFlow.Controls.Add(_ozonApiKeyBox);
            setupFlow.Controls.Add(openApi);
            setupFlow.Controls.Add(ozonButtons);

            FlowLayoutPanel readyFlow = new FlowLayoutPanel();
            readyFlow.Dock = DockStyle.Fill;
            readyFlow.FlowDirection = FlowDirection.TopDown;
            readyFlow.WrapContents = false;
            readyFlow.Visible = false;
            readyFlow.Tag = "ready";
            _setupCompletedLabel = CreateFlowText("准备完成\r\n1688 已登录，Ozon API 已验证。", 180, 62, 10F, FontStyle.Bold, PilotGreen);
            Button readyGo = CreateButton("去运营", GoOperationTab, true);
            readyGo.Width = 180;
            readyGo.Tag = "ready";
            Button readyRecheck = CreateButton("重新检测", RecheckSetup, false);
            readyRecheck.Width = 180;
            readyRecheck.Tag = "ready";
            Button readyEdit = CreateButton("修改账号", ExpandSetupPanel, false);
            readyEdit.Width = 180;
            readyEdit.Tag = "ready";
            readyFlow.Controls.Add(_setupCompletedLabel);
            readyFlow.Controls.Add(readyGo);
            readyFlow.Controls.Add(readyRecheck);
            readyFlow.Controls.Add(readyEdit);

            setupPanel.Controls.Add(readyFlow);
            setupPanel.Controls.Add(setupFlow);

            _browserStatusLabel = new Label();
            _browserStatusLabel.Dock = DockStyle.Bottom;
            _browserStatusLabel.Height = 32;
            _browserStatusLabel.ForeColor = Color.FromArgb(210, 216, 212);
            _browserStatusLabel.BackColor = Ink;
            _browserStatusLabel.Padding = new Padding(14, 8, 12, 6);
            _browserStatusLabel.Text = "浏览器尚未初始化。点左侧“初始化浏览器”。";

            _browser = new WebView2();
            _browser.ZoomFactor = 0.85d;

            Panel browserPanel = new Panel();
            browserPanel.Dock = DockStyle.Fill;
            browserPanel.BackColor = Ink;
            browserPanel.Padding = new Padding(12, 42, 12, 38);

            Label browserTitle = new Label();
            browserTitle.Text = "1688 工作浏览器";
            browserTitle.Left = 18;
            browserTitle.Top = 12;
            browserTitle.AutoSize = true;
            browserTitle.ForeColor = Color.FromArgb(246, 243, 232);
            browserTitle.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point, 0);

            _browser.Dock = DockStyle.Fill;
            browserPanel.Controls.Add(_browser);
            browserPanel.Controls.Add(browserTitle);
            browserPanel.Controls.Add(_browserStatusLabel);

            layout.Controls.Add(setupPanel, 0, 0);
            layout.Controls.Add(browserPanel, 1, 0);
            tab.Controls.Add(layout);
            UpdateSetupStatus();
            return tab;
        }

        private TabPage BuildSetupTabV5()
        {
            TabPage tab = CreateTabPage("准备");

            _browserUrlBox = new TextBox();
            _browserUrlBox.Width = 420;
            _browserUrlBox.Text = "https://www.1688.com/";
            _setupBodyLayout = null;

            Panel page = new Panel();
            page.Dock = DockStyle.Fill;
            page.Padding = new Padding(14);
            page.BackColor = ShellBack;

            RoundedPanel browserPanel = CreateSurfacePanel(24, new Padding(12, 38, 12, 28));
            browserPanel.Dock = DockStyle.Fill;
            browserPanel.FillColor = Color.FromArgb(255, 252, 248);
            browserPanel.BorderColor = Color.FromArgb(231, 219, 208);

            Label browserTitle = new Label();
            browserTitle.Text = "1688 工作区";
            browserTitle.Left = 18;
            browserTitle.Top = 11;
            browserTitle.AutoSize = true;
            browserTitle.ForeColor = TextStrong;
            browserTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point, 0);

            _browserStatusLabel = new Label();
            _browserStatusLabel.Dock = DockStyle.Bottom;
            _browserStatusLabel.Height = 24;
            _browserStatusLabel.ForeColor = TextMuted;
            _browserStatusLabel.BackColor = Color.FromArgb(255, 252, 248);
            _browserStatusLabel.Padding = new Padding(14, 4, 12, 2);
            _browserStatusLabel.Text = "浏览器尚未初始化。点浮窗里的“启动浏览器”。";

            _setupBrowserHost = new Panel();
            _setupBrowserHost.Dock = DockStyle.Fill;
            _setupBrowserHost.BackColor = Color.White;
            _browser = new WebView2();
            _browser.ZoomFactor = 0.9d;
            _browser.Dock = DockStyle.Fill;
            _browser.BackColor = Color.White;
            _setupBrowserHost.Controls.Add(_browser);
            browserPanel.Controls.Add(_setupBrowserHost);
            browserPanel.Controls.Add(browserTitle);
            browserPanel.Controls.Add(_browserStatusLabel);

            RoundedPanel setupPanel = CreateSurfacePanel(24, new Padding(18));
            _setupPanel = setupPanel;
            setupPanel.Dock = DockStyle.None;
            setupPanel.Width = 1060;
            setupPanel.Height = 202;
            setupPanel.Left = 22;
            setupPanel.Top = 22;
            setupPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            setupPanel.FillColor = Color.FromArgb(255, 253, 249);
            setupPanel.BorderColor = Color.FromArgb(235, 219, 205);
            setupPanel.ShadowColor = Color.FromArgb(22, 126, 78, 40);

            Panel normalPanel = new Panel();
            normalPanel.Dock = DockStyle.Fill;
            normalPanel.BackColor = Color.Transparent;
            normalPanel.Tag = "normal";

            Label title = CreateTextBlock("账号准备", 15F, FontStyle.Bold, TextStrong, 30);
            Label desc = CreateTextBlock("通过后自动收起，不挡浏览器。", 8.6F, FontStyle.Regular, TextMuted, 20);
            _setupActionLabel = CreateTextBlock("下一步：打开1688后检测", 9.2F, FontStyle.Bold, PilotGreenDark, 34);
            _setupActionLabel.BackColor = Color.FromArgb(255, 237, 222);
            _setupActionLabel.Padding = new Padding(14, 12, 12, 8);
            _setupActionLabel.Resize += delegate { SetRoundedRegion(_setupActionLabel, 14); };

            TableLayoutPanel statusGrid = CreateCompactGrid(2, 1);
            statusGrid.Height = 28;
            statusGrid.RowStyles[0].Height = 28;
            _setup1688StatusLabel = CreateSetupStatusLabel("1688：未检测", 0, 0, PilotGreen);
            _setup1688StatusLabel.Dock = DockStyle.Fill;
            _setup1688StatusLabel.Margin = new Padding(0, 0, 6, 0);
            _setupOzonStatusLabel = CreateSetupStatusLabel("Ozon：未检测", 0, 0, PilotGreenDark);
            _setupOzonStatusLabel.Dock = DockStyle.Fill;
            _setupOzonStatusLabel.Margin = new Padding(6, 0, 0, 0);
            statusGrid.Controls.Add(_setup1688StatusLabel, 0, 0);
            statusGrid.Controls.Add(_setupOzonStatusLabel, 1, 0);

            TableLayoutPanel browserButtons = CreateCompactGrid(3, 1);
            browserButtons.Height = 42;
            Button initButton = CreateButton("启动浏览器", InitializeBrowser, true);
            Button open1688Button = CreateButton("打开1688", Open1688LoginPage, false);
            Button check1688Button = CreateButton("检测登录", Check1688Login, false);
            browserButtons.Controls.Add(initButton, 0, 0);
            browserButtons.Controls.Add(open1688Button, 1, 0);
            browserButtons.Controls.Add(check1688Button, 2, 0);
            browserButtons.Top = 30;

            _ozonClientIdBox = CreateTextBox(0, 0, 120, DefaultOzonClientId);
            _ozonClientIdBox.Leave += delegate { SavePersistentUiState(); UpdateSetupStatus(); };
            _ozonApiKeyBox = CreateTextBox(0, 0, 120, DefaultOzonApiKey);
            _ozonApiKeyBox.PasswordChar = '*';
            _ozonApiKeyBox.Leave += delegate { SavePersistentUiState(); UpdateSetupStatus(); };

            TableLayoutPanel accountGrid = CreateCompactGrid(2, 1);
            accountGrid.Height = 58;
            accountGrid.Controls.Add(CreateField("Client-Id", _ozonClientIdBox), 0, 0);
            accountGrid.Controls.Add(CreateField("Api-Key", _ozonApiKeyBox), 1, 0);
            accountGrid.Top = 28;

            TableLayoutPanel ozonButtons = CreateCompactGrid(3, 1);
            ozonButtons.Height = 42;
            Button openApi = CreateButton("获取API", OpenOzonApiPage, false);
            Button checkApi = CreateButton("验证API", CheckOzonCredentials, true);
            Button saveOnly = CreateButton("保存API", delegate { SavePersistentUiState(); _ozonCredentialsVerified = false; UpdateSetupStatus(); UpdateOperationReadiness(); SetStatus("账号已保存，但还没有验证 API 是否可用。"); }, false);
            ozonButtons.Controls.Add(openApi, 0, 0);
            ozonButtons.Controls.Add(checkApi, 1, 0);
            ozonButtons.Controls.Add(saveOnly, 2, 0);
            ozonButtons.Top = 88;

            RoundedPanel intro = CreateSurfacePanel(16, new Padding(16, 14, 16, 12));
            intro.DrawShadow = false;
            intro.FillColor = Color.FromArgb(255, 251, 246);
            intro.Dock = DockStyle.None;
            intro.Controls.Add(statusGrid);
            intro.Controls.Add(_setupActionLabel);
            intro.Controls.Add(desc);
            intro.Controls.Add(title);

            RoundedPanel browserStep = CreateSurfacePanel(16, new Padding(16, 14, 16, 12));
            browserStep.DrawShadow = false;
            browserStep.FillColor = Color.FromArgb(255, 251, 246);
            browserStep.Dock = DockStyle.None;
            browserStep.Margin = new Padding(0);
            browserStep.Controls.Add(browserButtons);
            browserStep.Controls.Add(CreateTextBlock("1688 登录", 10F, FontStyle.Bold, TextStrong, 30));

            RoundedPanel ozonStep = CreateSurfacePanel(16, new Padding(16, 14, 16, 12));
            ozonStep.DrawShadow = false;
            ozonStep.FillColor = Color.FromArgb(255, 251, 246);
            ozonStep.Dock = DockStyle.None;
            ozonStep.Margin = new Padding(0);
            ozonStep.Controls.Add(ozonButtons);
            ozonStep.Controls.Add(accountGrid);
            ozonStep.Controls.Add(CreateTextBlock("Ozon API", 10F, FontStyle.Bold, TextStrong, 30));

            normalPanel.Resize += delegate
            {
                int width = normalPanel.ClientSize.Width;
                int contentWidth = Math.Min(1024, Math.Max(680, width));
                int gap = 14;
                int introWidth = 268;
                int browserWidth = 254;
                int stepHeight = Math.Max(140, normalPanel.ClientSize.Height);
                int ozonLeft = introWidth + browserWidth + gap * 2;
                intro.SetBounds(0, 0, introWidth, stepHeight);
                browserStep.SetBounds(introWidth + gap, 0, browserWidth, stepHeight);
                ozonStep.SetBounds(ozonLeft, 0, Math.Max(320, contentWidth - ozonLeft), stepHeight);
            };
            intro.SetBounds(0, 0, 268, 166);
            browserStep.SetBounds(282, 0, 254, 166);
            ozonStep.SetBounds(550, 0, 474, 166);
            normalPanel.Controls.Add(ozonStep);
            normalPanel.Controls.Add(browserStep);
            normalPanel.Controls.Add(intro);

            Panel readyPanel = new Panel();
            readyPanel.Dock = DockStyle.Fill;
            readyPanel.BackColor = Color.Transparent;
            readyPanel.Tag = "ready";
            readyPanel.Visible = false;

            _setupCompletedLabel = CreateTextBlock("准备完成", 16F, FontStyle.Bold, PilotGreenDark, 34);
            Label readyDesc = CreateTextBlock("账号已可用。需要改账号时再展开，平时把浏览器空间留给 1688。", 9.3F, FontStyle.Regular, TextMuted, 42);
            TableLayoutPanel readyButtons = CreateCompactGrid(3, 1);
            readyButtons.Height = 52;
            readyButtons.Controls.Add(CreateButton("去运营", GoOperationTab, true), 0, 0);
            readyButtons.Controls.Add(CreateButton("重新检测", RecheckSetup, false), 1, 0);
            readyButtons.Controls.Add(CreateButton("修改账号", ExpandSetupPanel, false), 2, 0);
            TableLayoutPanel readyGrid = new TableLayoutPanel();
            readyGrid.Dock = DockStyle.Fill;
            readyGrid.ColumnCount = 2;
            readyGrid.RowCount = 1;
            readyGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            readyGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 390));

            Panel readyText = new Panel();
            readyText.Dock = DockStyle.Fill;
            readyText.Controls.Add(readyDesc);
            readyText.Controls.Add(_setupCompletedLabel);
            readyGrid.Controls.Add(readyText, 0, 0);
            readyGrid.Controls.Add(readyButtons, 1, 0);
            readyPanel.Controls.Add(readyGrid);

            setupPanel.Controls.Add(readyPanel);
            setupPanel.Controls.Add(normalPanel);

            page.Resize += delegate
            {
                if (_setupPanel == null)
                {
                    return;
                }

                int width = Math.Min(1080, Math.Max(720, page.ClientSize.Width - 44));
                _setupPanel.Width = width;
                _setupPanel.Left = 22;
                _setupPanel.Top = 22;
            };
            page.Controls.Add(browserPanel);
            page.Controls.Add(setupPanel);
            setupPanel.BringToFront();
            tab.Controls.Add(page);
            UpdateSetupStatus();
            return tab;
        }

        private TabPage BuildAutomationTabV4()
        {
            TabPage tab = CreateTabPage("运营");

            TableLayoutPanel page = new TableLayoutPanel();
            page.Dock = DockStyle.Fill;
            page.ColumnCount = 2;
            page.RowCount = 1;
            page.Padding = new Padding(14);
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 332));
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            RoundedPanel controls = new RoundedPanel();
            controls.Dock = DockStyle.Fill;
            controls.Margin = new Padding(0, 0, 12, 0);
            controls.Padding = new Padding(18);
            controls.FillColor = Color.FromArgb(250, 248, 240);
            controls.BorderColor = LineWarm;
            controls.Radius = 18;

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = false;
            flow.AutoScroll = true;

            flow.Controls.Add(CreateFlowText("运营操作舱", 302, 34, 17F, FontStyle.Bold, TextStrong));
            _operationReadinessLabel = CreateFlowText(string.Empty, 302, 88, 11F, FontStyle.Bold, Color.FromArgb(255, 220, 162));
            _operationReadinessLabel.BackColor = Ink;
            _operationReadinessLabel.Padding = new Padding(16, 16, 14, 10);
            _operationReadinessLabel.Resize += delegate { SetRoundedRegion(_operationReadinessLabel, 18); };
            flow.Controls.Add(_operationReadinessLabel);

            flow.Controls.Add(CreateFlowText("选品关键词", 302, 28, 10F, FontStyle.Bold, TextStrong));
            _autoKeywordsBox = CreateTextBox(0, 0, 302, "家居收纳\r\n宠物慢食碗\r\n厨房置物架");
            _autoKeywordsBox.Multiline = true;
            _autoKeywordsBox.Height = 92;
            _autoKeywordsBox.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            flow.Controls.Add(_autoKeywordsBox);

            Label formula = CreateFlowText("定价公式已锁定", 302, 30, 9.2F, FontStyle.Bold, PilotGreenDark);
            flow.Controls.Add(formula);

            flow.Controls.Add(CreateFlowText("循环参数", 302, 28, 10F, FontStyle.Bold, TextStrong));
            TableLayoutPanel paramsGrid = new TableLayoutPanel();
            paramsGrid.Width = 302;
            paramsGrid.Height = 106;
            paramsGrid.ColumnCount = 2;
            paramsGrid.RowCount = 3;
            paramsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
            paramsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            paramsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            paramsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            paramsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            _autoPerKeywordBox = CreateTextBox(0, 0, 80, "5");
            _autoDetailLimitBox = CreateTextBox(0, 0, 80, "12");
            _autoRubRateBox = CreateTextBox(0, 0, 80, "12.5");
            AddParameterRow(paramsGrid, 0, "每词", _autoPerKeywordBox);
            AddParameterRow(paramsGrid, 1, "详情", _autoDetailLimitBox);
            AddParameterRow(paramsGrid, 2, "汇率", _autoRubRateBox);
            flow.Controls.Add(paramsGrid);

            _autoCategoryIdBox = CreateTextBox(0, 0, 80, "0");
            _autoCategoryIdBox.Visible = false;
            _autoTypeIdBox = CreateTextBox(0, 0, 80, "0");
            _autoTypeIdBox.Visible = false;
            _autoPriceMultiplierBox = CreateTextBox(0, 0, 100, "售价严格按公式：成本 / (1 - 佣金 - 推广 - 利润)");
            _autoPriceMultiplierBox.ReadOnly = true;
            _autoPriceMultiplierBox.Visible = false;

            flow.Controls.Add(CreateFlowText("下一步", 302, 28, 10F, FontStyle.Bold, TextStrong));
            FlowLayoutPanel actions = CreateFlowRow(302, 134);
            _runSourcingButton = CreateButton("选品", RunAutoSourcing, true);
            _runSourcingButton.Width = 136;
            _uploadSelectedButton = CreateButton("上传到Ozon", UploadSelectedToOzon, false);
            _uploadSelectedButton.Width = 146;
            _listFbsButton = CreateButton("取订单", ListOzonFbsPostings, false);
            _listFbsButton.Width = 136;
            _downloadLabelsButton = CreateButton("下载面单", DownloadOzonPackageLabels, false);
            _downloadLabelsButton.Width = 146;
            _exportResultsButton = CreateButton("导出结果", ExportAutoCandidates, false);
            _exportResultsButton.Width = 136;
            actions.Controls.Add(_runSourcingButton);
            actions.Controls.Add(_uploadSelectedButton);
            actions.Controls.Add(_listFbsButton);
            actions.Controls.Add(_downloadLabelsButton);
            actions.Controls.Add(_exportResultsButton);
            flow.Controls.Add(actions);

            _autoProviderBox = new ComboBox();
            _autoProviderBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _autoProviderBox.Items.AddRange(new object[] { "browser" });
            _autoProviderBox.SelectedIndex = 0;
            _autoProviderBox.Visible = false;
            _autoApiKeyBox = CreateTextBox(0, 0, 120, string.Empty);
            _autoApiKeyBox.Visible = false;
            _autoApiSecretBox = CreateTextBox(0, 0, 120, string.Empty);
            _autoApiSecretBox.Visible = false;

            controls.Controls.Add(_autoCategoryIdBox);
            controls.Controls.Add(_autoTypeIdBox);
            controls.Controls.Add(_autoPriceMultiplierBox);
            controls.Controls.Add(flow);

            Panel workspace = new Panel();
            workspace.Dock = DockStyle.Fill;
            _autoResultGrid = CreateGrid();
            _autoResultGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _autoResultGrid.MultiSelect = true;
            _operationEmptyStateLabel = new Label();
            _operationEmptyStateLabel.Text = "还没有结果\r\n填写关键词，然后点击“选品”。";
            _operationEmptyStateLabel.Dock = DockStyle.Fill;
            _operationEmptyStateLabel.TextAlign = ContentAlignment.MiddleCenter;
            _operationEmptyStateLabel.ForeColor = TextMuted;
            _operationEmptyStateLabel.BackColor = Color.FromArgb(250, 251, 250);
            _operationEmptyStateLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point, 0);
            _autoLogBox = new TextBox();
            _autoLogBox.Multiline = true;
            _autoLogBox.ReadOnly = true;
            _autoLogBox.ScrollBars = ScrollBars.Vertical;
            _autoLogBox.BackColor = Color.FromArgb(250, 251, 250);
            _autoLogBox.BorderStyle = BorderStyle.None;
            _autoLogBox.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            Panel resultSurface = new Panel();
            resultSurface.Dock = DockStyle.Fill;
            resultSurface.Controls.Add(_autoResultGrid);
            resultSurface.Controls.Add(_operationEmptyStateLabel);
            workspace.Controls.Add(WrapWithGroup("商品结果", resultSurface));

            page.Controls.Add(controls, 0, 0);
            page.Controls.Add(workspace, 1, 0);
            tab.Controls.Add(page);
            UpdateOperationReadiness();
            UpdateOperationResultState();
            return tab;
        }

        private TabPage BuildAutomationTabV5()
        {
            TabPage tab = CreateTabPage("运营");

            Panel page = new Panel();
            page.Dock = DockStyle.Fill;
            page.Padding = new Padding(14);
            page.BackColor = ShellBack;

            _operationLayout = null;

            RoundedPanel browserPanel = CreateSurfacePanel(24, new Padding(12, 46, 12, 36));
            browserPanel.Dock = DockStyle.Fill;
            browserPanel.FillColor = Color.FromArgb(255, 252, 248);
            browserPanel.BorderColor = Color.FromArgb(231, 219, 208);

            Label browserTitle = new Label();
            browserTitle.Text = "1688 工作浏览器";
            browserTitle.Left = 18;
            browserTitle.Top = 14;
            browserTitle.AutoSize = true;
            browserTitle.ForeColor = TextStrong;
            browserTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point, 0);

            Label browserHint = new Label();
            browserHint.Text = "浏览器是主工作区，控制台浮在上方。需要改登录/API 去“准备”。";
            browserHint.Dock = DockStyle.Bottom;
            browserHint.Height = 30;
            browserHint.ForeColor = TextMuted;
            browserHint.BackColor = Color.FromArgb(255, 252, 248);
            browserHint.Padding = new Padding(14, 7, 12, 4);

            _operationBrowserHost = new Panel();
            _operationBrowserHost.Dock = DockStyle.Fill;
            _operationBrowserHost.BackColor = Color.White;
            browserPanel.Controls.Add(_operationBrowserHost);
            browserPanel.Controls.Add(browserTitle);
            browserPanel.Controls.Add(browserHint);

            RoundedPanel command = CreateSurfacePanel(24, new Padding(20, 18, 20, 16));
            _operationCommandPanel = command;
            command.Dock = DockStyle.None;
            command.Width = 560;
            command.Height = 174;
            command.Left = 22;
            command.Top = 22;
            command.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            command.FillColor = Color.FromArgb(255, 253, 249);
            command.BorderColor = Color.FromArgb(235, 219, 205);
            command.ShadowColor = Color.FromArgb(26, 126, 78, 40);

            Label title = new Label();
            title.Text = "运营控制台";
            title.Left = 22;
            title.Top = 18;
            title.Width = 220;
            title.Height = 28;
            title.ForeColor = TextStrong;
            title.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold, GraphicsUnit.Point, 0);

            _operationReadinessLabel = new Label();
            _operationReadinessLabel.Left = 22;
            _operationReadinessLabel.Top = 56;
            _operationReadinessLabel.Width = 184;
            _operationReadinessLabel.Height = 76;
            _operationReadinessLabel.BackColor = PilotGreenSoft;
            _operationReadinessLabel.ForeColor = PilotGreenDark;
            _operationReadinessLabel.Font = new Font("Microsoft YaHei UI", 9.6F, FontStyle.Bold, GraphicsUnit.Point, 0);
            _operationReadinessLabel.Padding = new Padding(14, 10, 12, 8);
            _operationReadinessLabel.Resize += delegate { SetRoundedRegion(_operationReadinessLabel, 16); };

            _operationCategoryLabel = new Label();
            _operationCategoryLabel.Left = 22;
            _operationCategoryLabel.Top = 132;
            _operationCategoryLabel.Width = 184;
            _operationCategoryLabel.Height = 70;
            _operationCategoryLabel.ForeColor = TextMuted;
            _operationCategoryLabel.BackColor = Color.FromArgb(255, 248, 241);
            _operationCategoryLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            _operationCategoryLabel.Padding = new Padding(14, 10, 12, 8);
            _operationCategoryLabel.Resize += delegate { SetRoundedRegion(_operationCategoryLabel, 16); };

            _autoKeywordsBox = CreateTextBox(0, 0, 120, "家居收纳\r\n宠物慢食碗\r\n厨房置物架");
            _autoKeywordsBox.Multiline = true;
            _autoKeywordsBox.Height = 70;
            _autoKeywordsBox.ScrollBars = ScrollBars.Vertical;
            _autoKeywordsBox.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            Panel keywordField = CreateField("选品关键词", _autoKeywordsBox);
            keywordField.Dock = DockStyle.None;
            keywordField.SetBounds(250, 56, 328, 100);
            _operationKeywordField = keywordField;

            _autoLoopCountBox = CreateTextBox(0, 0, 80, "1");
            _autoLoopCountBox.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold, GraphicsUnit.Point, 0);
            _autoLoopCountBox.TextAlign = HorizontalAlignment.Center;
            Panel loopField = CreateField("循环次数", _autoLoopCountBox);
            loopField.Dock = DockStyle.None;
            loopField.SetBounds(594, 56, 108, 76);
            _operationLoopField = loopField;

            _fullAutoButton = CreateButton("自动循环", RunFullAutoLoop, true);
            _fullAutoButton.SetBounds(718, 78, 160, 42);

            RoundedPanel settings = CreateSurfacePanel(18, new Padding(14));
            _operationSettingsPanel = settings;
            settings.Dock = DockStyle.None;
            settings.SetBounds(250, 164, 628, 76);
            settings.DrawShadow = false;
            settings.FillColor = Color.FromArgb(255, 248, 241);
            settings.BorderColor = Color.FromArgb(239, 224, 212);

            _autoPerKeywordBox = CreateTextBox(0, 0, 80, "5");
            _autoDetailLimitBox = CreateTextBox(0, 0, 80, "12");
            _autoRubRateBox = CreateTextBox(0, 0, 80, "12.5");

            _runSourcingButton = CreateButton("选品", RunAutoSourcing, true);
            _uploadSelectedButton = CreateButton("上传", UploadSelectedToOzon, false);
            _listFbsButton = CreateButton("订单", ListOzonFbsPostings, false);
            _downloadLabelsButton = CreateButton("面单", DownloadOzonPackageLabels, false);
            _exportResultsButton = CreateButton("导出", ExportAutoCandidates, false);
            _runSourcingButton.Visible = false;
            _uploadSelectedButton.Visible = false;
            _listFbsButton.Visible = false;
            _downloadLabelsButton.Visible = false;
            _exportResultsButton.Visible = false;

            Label runCommand = CreateCommandButton("开始选品", RunAutoSourcing, true);
            Label uploadCommand = CreateCommandButton("上传到Ozon", UploadSelectedToOzon, false);
            Label orderCommand = CreateCommandButton("取订单", ListOzonFbsPostings, false);
            Label labelCommand = CreateCommandButton("下载面单", DownloadOzonPackageLabels, false);
            Label exportCommand = CreateCommandButton("导出结果", ExportAutoCandidates, false);

            FlowLayoutPanel actionGrid = new FlowLayoutPanel();
            _operationActionPanel = actionGrid;
            actionGrid.Dock = DockStyle.None;
            actionGrid.SetBounds(250, 112, 640, 44);
            actionGrid.Height = 48;
            actionGrid.WrapContents = false;
            actionGrid.AutoScroll = false;
            actionGrid.BackColor = Color.Transparent;
            runCommand.Width = 104;
            uploadCommand.Width = 118;
            orderCommand.Width = 86;
            labelCommand.Width = 100;
            exportCommand.Width = 100;
            runCommand.Margin = new Padding(0, 6, 10, 0);
            uploadCommand.Margin = new Padding(0, 6, 10, 0);
            orderCommand.Margin = new Padding(0, 6, 10, 0);
            labelCommand.Margin = new Padding(0, 6, 10, 0);
            exportCommand.Margin = new Padding(0, 6, 0, 0);
            actionGrid.Controls.Add(runCommand);
            actionGrid.Controls.Add(uploadCommand);
            actionGrid.Controls.Add(orderCommand);
            actionGrid.Controls.Add(labelCommand);
            actionGrid.Controls.Add(exportCommand);
            command.Controls.Add(actionGrid);

            _operationPreparePanel = new Panel();
            _operationPreparePanel.Dock = DockStyle.None;
            _operationPreparePanel.SetBounds(226, 56, 302, 96);
            _operationPreparePanel.BackColor = Color.Transparent;
            Label prepareHint = new Label();
            prepareHint.Text = "准备完成后，这里会展开操作按钮。商品结果看右下“结果与反馈”，面单数量回到“总览”看。";
            prepareHint.Left = 0;
            prepareHint.Top = 50;
            prepareHint.Width = 300;
            prepareHint.Height = 38;
            prepareHint.ForeColor = TextMuted;
            prepareHint.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            _operationPrepareCommandLabel = CreateCommandButton("去准备账号", delegate { SelectSetupTab(); }, true);
            _operationPrepareCommandLabel.Width = 132;
            _operationPrepareCommandLabel.Height = 40;
            _operationPrepareCommandLabel.Left = 0;
            _operationPrepareCommandLabel.Top = 4;
            _operationPrepareCommandLabel.Margin = new Padding(0);
            _operationPreparePanel.Controls.Add(prepareHint);
            _operationPreparePanel.Controls.Add(_operationPrepareCommandLabel);
            command.Controls.Add(_operationPreparePanel);

            TableLayoutPanel settingsGrid = new TableLayoutPanel();
            settingsGrid.Dock = DockStyle.Fill;
            settingsGrid.ColumnCount = 3;
            settingsGrid.RowCount = 1;
            settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
            settingsGrid.Controls.Add(CreateField("每词数量", _autoPerKeywordBox), 0, 0);
            settingsGrid.Controls.Add(CreateField("详情上限", _autoDetailLimitBox), 1, 0);
            settingsGrid.Controls.Add(CreateField("卢布汇率", _autoRubRateBox), 2, 0);
            settings.Controls.Add(settingsGrid);

            _autoCategoryIdBox = CreateTextBox(0, 0, 80, "0");
            _autoCategoryIdBox.Visible = false;
            _autoTypeIdBox = CreateTextBox(0, 0, 80, "0");
            _autoTypeIdBox.Visible = false;
            _autoPriceMultiplierBox = CreateTextBox(0, 0, 100, "售价严格按公式：成本 / (1 - 佣金 - 推广 - 利润)");
            _autoPriceMultiplierBox.ReadOnly = true;
            _autoPriceMultiplierBox.Visible = false;
            _autoProviderBox = new ComboBox();
            _autoProviderBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _autoProviderBox.Items.AddRange(new object[] { "browser" });
            _autoProviderBox.SelectedIndex = 0;
            _autoProviderBox.Visible = false;
            _autoApiKeyBox = CreateTextBox(0, 0, 120, string.Empty);
            _autoApiKeyBox.Visible = false;
            _autoApiSecretBox = CreateTextBox(0, 0, 120, string.Empty);
            _autoApiSecretBox.Visible = false;
            settings.Controls.Add(_autoCategoryIdBox);
            settings.Controls.Add(_autoTypeIdBox);
            settings.Controls.Add(_autoPriceMultiplierBox);
            settings.Controls.Add(_autoProviderBox);
            settings.Controls.Add(_autoApiKeyBox);
            settings.Controls.Add(_autoApiSecretBox);

            RoundedPanel results = CreateSurfacePanel(24, new Padding(16, 46, 16, 16));
            _operationResultsPanel = results;
            results.Dock = DockStyle.None;
            results.Width = 590;
            results.Height = 308;
            results.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            results.FillColor = Color.FromArgb(255, 253, 249);
            results.BorderColor = Color.FromArgb(235, 219, 205);
            results.ShadowColor = Color.FromArgb(26, 126, 78, 40);

            Label resultTitle = new Label();
            resultTitle.Text = "结果与反馈";
            resultTitle.Left = 20;
            resultTitle.Top = 16;
            resultTitle.AutoSize = true;
            resultTitle.ForeColor = TextStrong;
            resultTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point, 0);

            Label resultHint = new Label();
            resultHint.Text = "商品表在上，执行反馈在下。面单下载后会写入 PDF 和批次汇总。";
            resultHint.Left = 118;
            resultHint.Top = 18;
            resultHint.Width = 430;
            resultHint.Height = 20;
            resultHint.ForeColor = TextMuted;
            resultHint.Font = new Font("Microsoft YaHei UI", 8.6F, FontStyle.Regular, GraphicsUnit.Point, 0);

            Panel resultSurface = new Panel();
            resultSurface.Dock = DockStyle.Fill;
            resultSurface.BackColor = Color.FromArgb(255, 253, 249);
            _autoResultGrid = CreateGrid();
            _autoResultGrid.Dock = DockStyle.Fill;
            _autoResultGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _autoResultGrid.MultiSelect = true;
            _operationEmptyStateLabel = new Label();
            _operationEmptyStateLabel.Text = "还没有商品结果\r\n从资产页双击类目，或输入关键词后点“开始选品”。";
            _operationEmptyStateLabel.Dock = DockStyle.Fill;
            _operationEmptyStateLabel.TextAlign = ContentAlignment.MiddleCenter;
            _operationEmptyStateLabel.ForeColor = TextMuted;
            _operationEmptyStateLabel.BackColor = Color.FromArgb(255, 253, 249);
            _operationEmptyStateLabel.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold, GraphicsUnit.Point, 0);
            _autoLogBox = new TextBox();
            _autoLogBox.Multiline = true;
            _autoLogBox.ReadOnly = true;
            _autoLogBox.ScrollBars = ScrollBars.Vertical;
            _autoLogBox.Dock = DockStyle.Bottom;
            _autoLogBox.Height = 76;
            _autoLogBox.BackColor = Color.FromArgb(255, 248, 241);
            _autoLogBox.BorderStyle = BorderStyle.None;
            _autoLogBox.ForeColor = TextMuted;
            _autoLogBox.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point, 0);
            resultSurface.Controls.Add(_autoResultGrid);
            resultSurface.Controls.Add(_operationEmptyStateLabel);
            resultSurface.Controls.Add(_autoLogBox);
            results.Controls.Add(resultSurface);
            results.Controls.Add(resultHint);
            results.Controls.Add(resultTitle);

            command.Controls.Add(settings);
            command.Controls.Add(_fullAutoButton);
            command.Controls.Add(loopField);
            command.Controls.Add(keywordField);
            command.Controls.Add(_operationCategoryLabel);
            command.Controls.Add(_operationReadinessLabel);
            command.Controls.Add(title);

            page.Resize += delegate
            {
                bool ready = _1688LoginVerified && _ozonCredentialsVerified;
                int commandWidth = ready
                    ? Math.Min(920, Math.Max(700, page.ClientSize.Width - 44))
                    : Math.Min(580, Math.Max(500, page.ClientSize.Width - 44));
                command.Width = commandWidth;
                command.Height = ready ? 252 : 174;
                command.Left = 22;
                command.Top = 22;
                _operationReadinessLabel.SetBounds(22, 56, 184, ready ? 66 : 76);
                _operationCategoryLabel.SetBounds(22, 130, 184, 70);
                int rightAreaLeft = ready ? 230 : 226;
                int rightAreaWidth = Math.Max(360, command.ClientSize.Width - rightAreaLeft - 28);
                keywordField.SetBounds(rightAreaLeft, 56, Math.Max(260, rightAreaWidth - 300), 100);
                loopField.SetBounds(command.ClientSize.Width - 346, 56, 108, 76);
                _fullAutoButton.SetBounds(command.ClientSize.Width - 176, 78, 150, 42);
                actionGrid.SetBounds(rightAreaLeft, 112, rightAreaWidth, 44);
                _operationPreparePanel.SetBounds(rightAreaLeft, 56, Math.Min(320, rightAreaWidth), 96);
                settings.SetBounds(rightAreaLeft, 164, rightAreaWidth, 76);
                results.Width = Math.Min(600, Math.Max(420, page.ClientSize.Width - 44));
                results.Height = Math.Min(318, Math.Max(236, page.ClientSize.Height - 318));
                results.Left = Math.Max(22, page.ClientSize.Width - results.Width - 22);
                results.Top = Math.Max(command.Bottom + 18, page.ClientSize.Height - results.Height - 22);
            };

            page.Controls.Add(browserPanel);
            page.Controls.Add(results);
            page.Controls.Add(command);
            command.BringToFront();
            results.BringToFront();
            tab.Controls.Add(page);
            UpdateAutomationCategoryDisplay();
            UpdateOperationReadiness();
            UpdateOperationResultState();
            return tab;
        }

        private Label CreateFlowText(string text, int width, int height, float size, FontStyle style, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Width = width;
            label.Height = height;
            label.AutoSize = false;
            label.Margin = new Padding(0, 0, 0, 8);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.Font = new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point, 0);
            return label;
        }

        private FlowLayoutPanel CreateFlowRow(int width, int height)
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.Width = width;
            row.Height = height;
            row.Margin = new Padding(0, 0, 0, 10);
            row.FlowDirection = FlowDirection.LeftToRight;
            row.WrapContents = true;
            row.AutoScroll = false;
            return row;
        }

        private Label CreateTextBlock(string text, float size, FontStyle style, Color color, int height)
        {
            Label label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Top;
            label.Height = height;
            label.AutoSize = false;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.Font = new Font("Segoe UI", size, style, GraphicsUnit.Point, 0);
            return label;
        }

        private TableLayoutPanel CreateCompactGrid(int columns, int rows)
        {
            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Top;
            grid.ColumnCount = columns;
            grid.RowCount = rows;
            grid.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            for (int i = 0; i < columns; i++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            }
            for (int i = 0; i < rows; i++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            }
            return grid;
        }

        private Panel CreateField(string label, Control input)
        {
            Panel field = new Panel();
            field.Dock = DockStyle.Fill;
            field.Padding = new Padding(0, 0, 10, 8);
            field.BackColor = Color.Transparent;

            Label labelControl = new Label();
            labelControl.Text = label;
            labelControl.Dock = DockStyle.Top;
            labelControl.Height = 22;
            labelControl.ForeColor = TextMuted;
            labelControl.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold, GraphicsUnit.Point, 0);

            input.Dock = DockStyle.Top;
            input.Height = Math.Max(input.Height, 30);
            input.Margin = new Padding(0);
            field.Controls.Add(input);
            field.Controls.Add(labelControl);
            return field;
        }

        private RoundedPanel CreateSurfacePanel(int radius, Padding padding)
        {
            RoundedPanel panel = new RoundedPanel();
            panel.Radius = radius;
            panel.Padding = padding;
            panel.FillColor = Color.FromArgb(255, 253, 249);
            panel.BorderColor = Color.FromArgb(236, 226, 216);
            panel.ShadowColor = Color.FromArgb(13, 152, 91, 42);
            return panel;
        }

        private TabPage BuildSetupTab()
        {
            TabPage tab = CreateTabPage("准备");

            _browserUrlBox = new TextBox();
            _browserUrlBox.Width = 420;
            _browserUrlBox.Text = "https://www.1688.com/";

            TableLayoutPanel body = new TableLayoutPanel();
            _setupBodyLayout = body;
            body.Dock = DockStyle.Fill;
            body.RowCount = 2;
            body.ColumnCount = 1;
            body.Padding = new Padding(16, 14, 16, 16);
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 278));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            Panel setupPanel = new Panel();
            _setupPanel = setupPanel;
            setupPanel.Dock = DockStyle.Fill;
            setupPanel.BackColor = Color.FromArgb(246, 244, 235);
            setupPanel.Padding = new Padding(24);

            int left = 24;
            int top = 20;

            Label title = new Label();
            title.Text = "准备区";
            title.Left = left;
            title.Top = top;
            title.Width = 260;
            title.Height = 34;
            title.ForeColor = TextStrong;
            title.Font = new Font("Segoe UI", 18F, FontStyle.Bold, GraphicsUnit.Point, 0);
            setupPanel.Controls.Add(title);

            Label desc = new Label();
            desc.Text = "第一次只需要完成这里。准备好了，这块会自动收起，把空间留给浏览器。";
            desc.Left = left;
            desc.Top = top + 42;
            desc.Width = 360;
            desc.Height = 44;
            desc.ForeColor = TextMuted;
            desc.Font = new Font("Segoe UI", 9.2F, FontStyle.Regular, GraphicsUnit.Point, 0);
            setupPanel.Controls.Add(desc);

            _setupActionLabel = new Label();
            _setupActionLabel.Text = "下一步：初始化浏览器。";
            _setupActionLabel.Left = left;
            _setupActionLabel.Top = 112;
            _setupActionLabel.Width = 360;
            _setupActionLabel.Height = 54;
            _setupActionLabel.BackColor = Ink;
            _setupActionLabel.Padding = new Padding(18, 15, 14, 10);
            _setupActionLabel.ForeColor = Color.FromArgb(246, 243, 232);
            _setupActionLabel.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point, 0);
            _setupActionLabel.Resize += delegate { SetRoundedRegion(_setupActionLabel, 18); };
            SetRoundedRegion(_setupActionLabel, 18);
            setupPanel.Controls.Add(_setupActionLabel);

            _setup1688StatusLabel = CreateSetupStatusLabel("1688：浏览器未初始化", left, 184, PilotGreen);
            _setup1688StatusLabel.Width = 174;
            setupPanel.Controls.Add(_setup1688StatusLabel);

            _setupOzonStatusLabel = CreateSetupStatusLabel("Ozon：账号未保存", 210, 184, Color.FromArgb(63, 96, 143));
            _setupOzonStatusLabel.Width = 174;
            setupPanel.Controls.Add(_setupOzonStatusLabel);

            Label step1688 = CreateSetupStepLabel("1", "打开并检测 1688", "这里会检查当前浏览器里是不是真的登录了 1688。", 396, 26, PilotGreen);
            step1688.Width = 330;
            setupPanel.Controls.Add(step1688);

            Button initBrowser = CreateButton("初始化浏览器", InitializeBrowser, true);
            initBrowser.Left = 396;
            initBrowser.Top = 116;
            initBrowser.Width = 132;
            setupPanel.Controls.Add(initBrowser);

            Button open1688 = CreateButton("打开1688", Open1688LoginPage, false);
            open1688.Left = 540;
            open1688.Top = 116;
            open1688.Width = 116;
            setupPanel.Controls.Add(open1688);

            Button check1688 = CreateButton("检测登录", Check1688Login, false);
            check1688.Left = 668;
            check1688.Top = 116;
            check1688.Width = 118;
            setupPanel.Controls.Add(check1688);

            Label stepOzon = CreateSetupStepLabel("2", "保存 Ozon API", "Client-Id 和 Api-Key 会保存在本机，下次打开自动带出。", 740, 26, Color.FromArgb(63, 96, 143));
            stepOzon.Width = 340;
            setupPanel.Controls.Add(stepOzon);

            Button openOzonApi = CreateButton("打开API页面", OpenOzonApiPage, false);
            openOzonApi.Left = 740;
            openOzonApi.Top = 208;
            openOzonApi.Width = 136;
            setupPanel.Controls.Add(openOzonApi);

            int accountLeft = 740;
            setupPanel.Controls.Add(CreateFormLabel("Client-Id", accountLeft, 118, 76));
            _ozonClientIdBox = CreateTextBox(accountLeft, 148, 162, DefaultOzonClientId);
            _ozonClientIdBox.Leave += delegate { SavePersistentUiState(); UpdateSetupStatus(); };
            setupPanel.Controls.Add(_ozonClientIdBox);

            setupPanel.Controls.Add(CreateFormLabel("Api-Key", 918, 118, 62));
            _ozonApiKeyBox = CreateTextBox(918, 148, 160, DefaultOzonApiKey);
            _ozonApiKeyBox.PasswordChar = '*';
            _ozonApiKeyBox.Leave += delegate { SavePersistentUiState(); UpdateSetupStatus(); };
            setupPanel.Controls.Add(_ozonApiKeyBox);

            Button saveAccount = CreateButton("检测并保存", CheckOzonCredentials, true);
            saveAccount.Left = 884;
            saveAccount.Top = 208;
            saveAccount.Width = 142;
            setupPanel.Controls.Add(saveAccount);

            Button saveOnly = CreateButton("仅保存", delegate { SavePersistentUiState(); _ozonCredentialsVerified = false; UpdateSetupStatus(); UpdateOperationReadiness(); SetStatus("账号已保存，但还没有验证 API 是否可用。"); }, false);
            saveOnly.Left = 1008;
            saveOnly.Top = 208;
            saveOnly.Width = 76;
            setupPanel.Controls.Add(saveOnly);

            _setupCompletedLabel = new Label();
            _setupCompletedLabel.Text = "准备完成：1688 已登录，Ozon API 已验证。";
            _setupCompletedLabel.Left = 24;
            _setupCompletedLabel.Top = 18;
            _setupCompletedLabel.Width = 520;
            _setupCompletedLabel.Height = 32;
            _setupCompletedLabel.ForeColor = PilotGreen;
            _setupCompletedLabel.BackColor = Color.Transparent;
            _setupCompletedLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point, 0);
            _setupCompletedLabel.Visible = false;
            setupPanel.Controls.Add(_setupCompletedLabel);

            Button completedGoOperate = CreateButton("去运营", GoOperationTab, true);
            completedGoOperate.Left = 560;
            completedGoOperate.Top = 14;
            completedGoOperate.Width = 118;
            completedGoOperate.Tag = "ready";
            completedGoOperate.Visible = false;
            setupPanel.Controls.Add(completedGoOperate);

            Button completedRecheck = CreateButton("重新检测", RecheckSetup, false);
            completedRecheck.Left = 694;
            completedRecheck.Top = 14;
            completedRecheck.Width = 118;
            completedRecheck.Tag = "ready";
            completedRecheck.Visible = false;
            setupPanel.Controls.Add(completedRecheck);

            Button completedExpand = CreateButton("修改账号", ExpandSetupPanel, false);
            completedExpand.Left = 828;
            completedExpand.Top = 14;
            completedExpand.Width = 118;
            completedExpand.Tag = "ready";
            completedExpand.Visible = false;
            setupPanel.Controls.Add(completedExpand);

            _browserStatusLabel = new Label();
            _browserStatusLabel.Dock = DockStyle.Bottom;
            _browserStatusLabel.Height = 32;
            _browserStatusLabel.ForeColor = Color.FromArgb(210, 216, 212);
            _browserStatusLabel.BackColor = Ink;
            _browserStatusLabel.Padding = new Padding(14, 8, 12, 6);
            _browserStatusLabel.Text = "浏览器尚未初始化。点上方“初始化浏览器”。";

            _browser = new WebView2();
            _browser.ZoomFactor = 0.9d;
            Panel browserPanel = new Panel();
            browserPanel.Dock = DockStyle.Fill;
            browserPanel.BackColor = Ink;
            browserPanel.Padding = new Padding(12, 44, 12, 38);

            Label browserTitle = new Label();
            browserTitle.Text = "1688 工作浏览器";
            browserTitle.Left = 18;
            browserTitle.Top = 13;
            browserTitle.AutoSize = true;
            browserTitle.ForeColor = Color.FromArgb(246, 243, 232);
            browserTitle.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point, 0);

            _browser.Dock = DockStyle.Fill;
            browserPanel.Controls.Add(_browser);
            browserPanel.Controls.Add(browserTitle);
            browserPanel.Controls.Add(_browserStatusLabel);

            body.Controls.Add(setupPanel, 0, 0);
            body.Controls.Add(browserPanel, 0, 1);

            tab.Controls.Add(body);
            UpdateSetupStatus();
            return tab;
        }

        private void LoadAll()
        {
            _assetErrorMessage = null;

            SetStatus("正在加载恢复工作台...");
            LoadConfig();

            try
            {
                LoadAssets();
            }
            catch (Exception ex)
            {
                _assetErrorMessage = ex.Message;
                ClearAssetViews();
            }

            UpdateOverview();

            if (!string.IsNullOrEmpty(_assetErrorMessage))
            {
                SetStatus("类目/规则读取失败：" + _assetErrorMessage);
                return;
            }

            SetStatus("恢复工作台加载完成。");
        }

        private void LoadConfig()
        {
            EnsureSnapshot();
            _snapshot.Config = ConfigService.Load(_paths.ConfigFile);
            _uiLanguage = NormalizeUiLanguage(_snapshot.Config == null ? null : _snapshot.Config.UiLanguage);
            _configGrid.SelectedObject = _snapshot.Config;
            if (_languageComboBox != null)
            {
                _languageComboBox.SelectedIndex = LanguageIndexFromCode(_uiLanguage);
            }
            ApplyOzonSellerDefaults();
        }

        private void ApplyOzonSellerDefaults()
        {
            if (_ozonClientIdBox != null && string.IsNullOrWhiteSpace(_ozonClientIdBox.Text))
            {
                _ozonClientIdBox.Text = DefaultOzonClientId;
            }

            if (_ozonApiKeyBox != null && string.IsNullOrWhiteSpace(_ozonApiKeyBox.Text))
            {
                _ozonApiKeyBox.Text = DefaultOzonApiKey;
            }

            UpdateSetupStatus();
        }

        private void UpdateSetupStatus()
        {
            if (_setup1688StatusLabel != null)
            {
                bool browserReady = _browser != null && _browser.CoreWebView2 != null;
                _setup1688StatusLabel.Text = _1688LoginVerified
                    ? "1688：已检测到登录"
                    : browserReady ? "1688：待检测" : "1688：未启动";
                _setup1688StatusLabel.ForeColor = _1688LoginVerified ? PilotGreen : WarningAmber;
            }

            if (_setupOzonStatusLabel != null)
            {
                bool hasOzon = _ozonClientIdBox != null && _ozonApiKeyBox != null &&
                    !string.IsNullOrWhiteSpace(_ozonClientIdBox.Text) &&
                    !string.IsNullOrWhiteSpace(_ozonApiKeyBox.Text);
                _setupOzonStatusLabel.Text = _ozonCredentialsVerified
                    ? "Ozon：API 已验证"
                    : hasOzon ? "Ozon：待验证" : "Ozon：待填写";
                _setupOzonStatusLabel.ForeColor = _ozonCredentialsVerified ? PilotGreen : WarningAmber;
            }

            bool ready = _1688LoginVerified && _ozonCredentialsVerified;
            if (_setupCompletedLabel != null)
            {
                _setupCompletedLabel.Visible = ready;
            }

            if (_setupPanel != null)
            {
                for (int i = 0; i < _setupPanel.Controls.Count; i++)
                {
                    Control control = _setupPanel.Controls[i];
                    bool readyControl = control == _setupCompletedLabel ||
                        (control.Tag != null && string.Equals(Convert.ToString(control.Tag), "ready", StringComparison.Ordinal));
                    bool normalControl = control.Tag != null && string.Equals(Convert.ToString(control.Tag), "normal", StringComparison.Ordinal);
                    if (readyControl)
                    {
                        control.Visible = ready;
                    }
                    else if (normalControl)
                    {
                        control.Visible = !ready;
                    }
                    else
                    {
                        control.Visible = !ready;
                    }
                }

                _setupPanel.Height = ready ? 92 : 202;
                if (_setupBodyLayout != null && _setupBodyLayout.RowStyles.Count > 0)
                {
                    _setupBodyLayout.RowStyles[0].Height = ready ? 82 : 376;
                }
            }

            if (_setupBodyLayout != null && _setupBodyLayout.RowCount > 1 && _setupBodyLayout.RowStyles.Count > 0)
            {
                _setupBodyLayout.RowStyles[0].Height = ready ? 82 : 376;
            }
            if (_setupBodyLayout != null && _setupBodyLayout.ColumnStyles.Count > 0)
            {
                _setupBodyLayout.ColumnStyles[0].Width = 100;
            }

            if (_setupActionLabel != null && !ready)
            {
                _setupActionLabel.Text = BuildSetupNextAction();
                _setupActionLabel.ForeColor = PilotGreenDark;
                _setupActionLabel.BackColor = Color.FromArgb(255, 237, 222);
            }
            UpdateAutomationCategoryDisplay();
            UpdateOverviewCards();
            UpdateOperationReadiness();
        }

        private string BuildSetupNextAction()
        {
            if (_browser == null || _browser.CoreWebView2 == null)
            {
                return "下一步：启动浏览器";
            }

            if (!_1688LoginVerified)
            {
                return "下一步：打开1688后检测";
            }

            if (_ozonClientIdBox == null || _ozonApiKeyBox == null ||
                string.IsNullOrWhiteSpace(_ozonClientIdBox.Text) ||
                string.IsNullOrWhiteSpace(_ozonApiKeyBox.Text))
            {
                return "下一步：获取API后填写";
            }

            if (!_ozonCredentialsVerified)
            {
                return "下一步：验证 Ozon API";
            }

            return "准备完成，可以去运营";
        }

        private void SelectSetupTab()
        {
            if (_mainTabs != null && _setupTab != null)
            {
                _mainTabs.SelectedTab = _setupTab;
                AttachBrowserToHost(_setupBrowserHost);
            }
        }

        private void SelectOperationTab()
        {
            if (_mainTabs != null && _operationTab != null)
            {
                _mainTabs.SelectedTab = _operationTab;
                AttachBrowserToHost(_operationBrowserHost);
                UpdateOperationReadiness();
                UpdateOperationResultState();
            }
        }

        private void SelectOverviewTab()
        {
            if (_mainTabs != null && _overviewTab != null)
            {
                _mainTabs.SelectedTab = _overviewTab;
            }
        }

        private bool EnsureOperationReady(string actionName)
        {
            if (_1688LoginVerified && _ozonCredentialsVerified)
            {
                return true;
            }

            SelectSetupTab();
            UpdateSetupStatus();
            UpdateOperationReadiness();
            SetStatus("请先完成准备，再执行“" + actionName + "”。");
            MessageBox.Show(this, "请先在“准备”页完成 1688 登录检测和 Ozon API 检测。", "还没准备好", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        private void UpdateOperationReadiness()
        {
            if (_operationReadinessLabel == null)
            {
                return;
            }

            bool ready = _1688LoginVerified && _ozonCredentialsVerified;
            _operationReadinessLabel.Text = ready
                ? "已准备好\r\n1688 已登录\r\nOzon API 已验证"
                : "还不能开跑\r\n先完成账号准备";
            _operationReadinessLabel.BackColor = ready ? PilotGreenSoft : Color.FromArgb(255, 242, 231);
            _operationReadinessLabel.ForeColor = ready ? PilotGreenDark : WarningAmber;
            UpdateAutomationCategoryDisplay();
            if (_operationActionPanel != null)
            {
                _operationActionPanel.Visible = ready;
            }
            if (_operationPrepareCommandLabel != null)
            {
                _operationPrepareCommandLabel.Visible = !ready;
            }
            if (_operationPreparePanel != null)
            {
                _operationPreparePanel.Visible = !ready;
            }
            if (_operationCategoryLabel != null)
            {
                _operationCategoryLabel.Visible = ready;
            }
            if (_operationCommandPanel != null)
            {
                int targetWidth = ready ? 920 : 580;
                int minWidth = ready ? 700 : 500;
                _operationCommandPanel.Width = Math.Min(targetWidth, Math.Max(minWidth, _operationCommandPanel.Parent == null ? targetWidth : _operationCommandPanel.Parent.ClientSize.Width - 44));
                _operationCommandPanel.Height = ready ? 252 : 174;
            }
            if (_operationKeywordField != null)
            {
                _operationKeywordField.Visible = ready;
            }
            if (_operationLoopField != null)
            {
                _operationLoopField.Visible = ready;
            }
            if (_fullAutoButton != null)
            {
                _fullAutoButton.Visible = ready;
            }
            if (_operationSettingsPanel != null)
            {
                _operationSettingsPanel.Visible = ready;
            }
            if (_operationResultsPanel != null)
            {
                _operationResultsPanel.Visible = ready;
            }
            if (_operationLayout != null && _operationLayout.RowStyles.Count >= 3)
            {
                _operationLayout.RowStyles[0].Height = ready ? 184 : 184;
                _operationLayout.RowStyles[1].Height = ready ? 118 : 0;
                _operationLayout.RowStyles[2].Height = ready ? 100 : 0;
                _operationLayout.RowStyles[2].SizeType = ready ? SizeType.Percent : SizeType.Absolute;
            }
            if (_fullAutoButton != null) _fullAutoButton.Enabled = true;
            if (_runSourcingButton != null) _runSourcingButton.Enabled = true;
            if (_uploadSelectedButton != null) _uploadSelectedButton.Enabled = true;
            if (_listFbsButton != null) _listFbsButton.Enabled = true;
            if (_downloadLabelsButton != null) _downloadLabelsButton.Enabled = true;
            if (_exportResultsButton != null) _exportResultsButton.Enabled = true;
            ApplyActionButtonState(_fullAutoButton, ready, true);
            ApplyActionButtonState(_runSourcingButton, ready, true);
            ApplyActionButtonState(_uploadSelectedButton, ready, false);
            ApplyActionButtonState(_listFbsButton, ready, false);
            ApplyActionButtonState(_downloadLabelsButton, ready, false);
            ApplyActionButtonState(_exportResultsButton, ready && _lastSourcingResult != null && _lastSourcingResult.Products != null && _lastSourcingResult.Products.Count > 0, false);
            UpdateOverviewCards();
        }

        private void UpdateAutomationCategoryDisplay()
        {
            string categoryText = BuildAutomationCategoryText();
            if (_operationCategoryLabel != null)
            {
                _operationCategoryLabel.Text = categoryText;
            }
            if (_overviewCategoryLabel != null)
            {
                _overviewCategoryLabel.Text = categoryText.Replace(Environment.NewLine, " ");
            }
        }

        private string BuildAutomationCategoryText()
        {
            string categoryId = _autoCategoryIdBox == null ? string.Empty : _autoCategoryIdBox.Text.Trim();
            string typeId = _autoTypeIdBox == null ? string.Empty : _autoTypeIdBox.Text.Trim();
            bool hasCategory = !string.IsNullOrEmpty(categoryId) && categoryId != "0" &&
                !string.IsNullOrEmpty(typeId) && typeId != "0";
            if (hasCategory)
            {
                return "当前类目" + Environment.NewLine + categoryId + " / " + typeId + Environment.NewLine + "来自资产页双击";
            }

            return "当前类目" + Environment.NewLine + "未指定" + Environment.NewLine + "自动循环会从类目池挑选";
        }

        private void ApplyActionButtonState(Button button, bool enabled, bool primary)
        {
            if (button == null)
            {
                return;
            }

            button.BackColor = enabled
                ? primary ? PilotGreen : Color.FromArgb(255, 250, 244)
                : Color.FromArgb(236, 229, 221);
            button.ForeColor = enabled
                ? primary ? Color.White : TextStrong
                : Color.FromArgb(139, 126, 114);
        }

        private void UpdateOperationResultState()
        {
            if (_operationEmptyStateLabel == null)
            {
                return;
            }

            bool hasRows = _lastSourcingResult != null &&
                _lastSourcingResult.Products != null &&
                _lastSourcingResult.Products.Count > 0;
            _operationEmptyStateLabel.Visible = !hasRows;
            if (_autoResultGrid != null)
            {
                _autoResultGrid.Visible = hasRows;
            }
            if (_exportResultsButton != null)
            {
                _exportResultsButton.Enabled = true;
                ApplyActionButtonState(_exportResultsButton, _1688LoginVerified && _ozonCredentialsVerified && hasRows, false);
            }
        }

        private void ExpandSetupPanel(object sender, EventArgs e)
        {
            _1688LoginVerified = false;
            _ozonCredentialsVerified = false;
            UpdateSetupStatus();
            UpdateOperationReadiness();
            SetStatus("准备区已展开，可以修改账号或重新检测。");
        }

        private void RecheckSetup(object sender, EventArgs e)
        {
            _1688LoginVerified = false;
            _ozonCredentialsVerified = false;
            UpdateSetupStatus();
            UpdateOperationReadiness();
            SetStatus("请重新检测 1688 登录和 Ozon API。");
        }

        private void GoOperationTab(object sender, EventArgs e)
        {
            SavePersistentUiState();
            UpdateSetupStatus();
            if (_ozonClientIdBox == null || _ozonApiKeyBox == null ||
                string.IsNullOrWhiteSpace(_ozonClientIdBox.Text) ||
                string.IsNullOrWhiteSpace(_ozonApiKeyBox.Text))
            {
                SetStatus("请先填写 Ozon Client-Id 和 Api-Key，再点击“检测并保存”。");
                return;
            }

            if (!_ozonCredentialsVerified)
            {
                SetStatus("请先点击“检测并保存”，确认 Ozon API 可用。");
                return;
            }

            if (_browser == null || _browser.CoreWebView2 == null)
            {
                SetStatus("请先初始化 1688 浏览器，并确认已经登录。");
                return;
            }

            if (!_1688LoginVerified)
            {
                SetStatus("请先点击“检测登录”，确认 1688 已登录。");
                return;
            }

            SelectOperationTab();
        }

        private void SaveConfig(object sender, EventArgs e)
        {
            try
            {
                AppConfig config = _configGrid.SelectedObject as AppConfig;
                if (config == null)
                {
                    return;
                }

                config.UiLanguage = _uiLanguage;
                ConfigService.Save(_paths.ConfigFile, config);
                UpdateOverview();
                SetStatus("配置已保存。");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadAssets()
        {
            EnsureSnapshot();
            _snapshot.Categories = AssetCatalogService.LoadCategories(_paths.CategoryFile);
            _snapshot.FeeRules = AssetCatalogService.LoadFeeRules(_paths.FeeFile);
            _assetErrorMessage = null;
            ApplyAssetFilter();
            UpdateOverview();
        }

        private void UpdateOverview()
        {
            if (_snapshot == null)
            {
                return;
            }

            int categoryCount = _snapshot.Categories == null ? 0 : AssetCatalogService.CountCategories(_snapshot.Categories);
            int feeCount = _snapshot.FeeRules == null ? 0 : _snapshot.FeeRules.Count;
            int pluginFileCount = Directory.Exists(_paths.Plugin1688Folder)
                ? Directory.GetFiles(_paths.Plugin1688Folder, "*", SearchOption.AllDirectories).Length
                : 0;
            int labelPdfCount = Directory.Exists(_paths.OzonLabelDirectory)
                ? Directory.GetFiles(_paths.OzonLabelDirectory, "*.pdf", SearchOption.AllDirectories).Length
                : 0;
            int labelSummaryCount = Directory.Exists(_paths.OzonLabelDirectory)
                ? Directory.GetFiles(_paths.OzonLabelDirectory, "label-batch-*.txt", SearchOption.AllDirectories).Length
                : 0;

            bool ready = _1688LoginVerified && _ozonCredentialsVerified;
            int productCount = _lastSourcingResult == null || _lastSourcingResult.Products == null
                ? 0
                : _lastSourcingResult.Products.Count;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("下一步");
            builder.AppendLine(ready
                ? productCount > 0
                    ? "去运营页处理商品：上传到 Ozon、取订单或下载面单。"
                    : "去运营页开始选品；也可以先到资产页双击一个类目。"
                : "先去准备页完成 1688 登录检测和 Ozon API 验证。");
            builder.AppendLine();
            builder.AppendLine("结果会出现在哪里");
            builder.AppendLine("商品候选：运营页右下“结果与反馈”。");
            builder.AppendLine("面单 PDF：下载后在这里显示数量，并保存到本机面单目录。");
            builder.AppendLine("类目选择：资产页双击后会自动写入运营页。");
            builder.AppendLine();
            builder.AppendLine("当前状态");
            builder.AppendLine("账号：" + (ready ? "已准备好。" : "还没完成准备。"));
            builder.AppendLine("浏览器环境：" + (pluginFileCount > 0 ? "已就绪；账号登录仍以准备页检测为准。" : "还没检测到 1688 插件文件。"));
            builder.AppendLine("资产：" + categoryCount + " 个类目，" + feeCount + " 条规则。");
            builder.AppendLine("面单：" + labelPdfCount + " 个 PDF，" + labelSummaryCount + " 个批次汇总。");

            if (!string.IsNullOrEmpty(_assetErrorMessage))
            {
                builder.AppendLine();
                builder.AppendLine("资产读取需要处理：" + _assetErrorMessage);
            }

            if (!string.IsNullOrEmpty(_fullAutoReport))
            {
                builder.AppendLine();
                builder.AppendLine("最近一次自动循环");
                builder.AppendLine(_fullAutoReport);
            }

            _overviewBox.Text = builder.ToString();
            UpdateOverviewCards();
        }

        private void UpdateOverviewCards()
        {
            bool ready = _1688LoginVerified && _ozonCredentialsVerified;
            if (_overviewSetupLabel != null)
            {
                _overviewSetupLabel.Text = ready ? "已完成" : "需要先准备";
                _overviewSetupLabel.ForeColor = ready ? PilotGreenDark : WarningAmber;
            }

            if (_overviewCategoryLabel != null)
            {
                _overviewCategoryLabel.Text = BuildAutomationCategoryText().Replace(Environment.NewLine, " ");
                _overviewCategoryLabel.ForeColor = TextStrong;
            }

            int productCount = _lastSourcingResult == null || _lastSourcingResult.Products == null
                ? 0
                : _lastSourcingResult.Products.Count;
            if (_overviewResultLabel != null)
            {
                _overviewResultLabel.Text = productCount > 0 ? productCount + " 个候选" : "还没有结果";
                _overviewResultLabel.ForeColor = productCount > 0 ? PilotGreenDark : TextMuted;
            }

            int labelPdfCount = Directory.Exists(_paths.OzonLabelDirectory)
                ? Directory.GetFiles(_paths.OzonLabelDirectory, "*.pdf", SearchOption.AllDirectories).Length
                : 0;
            if (_overviewLabelLabel != null)
            {
                _overviewLabelLabel.Text = labelPdfCount > 0 ? labelPdfCount + " 个 PDF" : "未下载";
                _overviewLabelLabel.ForeColor = labelPdfCount > 0 ? PilotGreenDark : TextMuted;
            }

            if (_overviewNextActionLabel != null)
            {
                if (!ready)
                {
                    _overviewNextActionLabel.Text = "下一步：去“准备”完成 1688 登录和 Ozon API 验证";
                }
                else if (productCount == 0)
                {
                    _overviewNextActionLabel.Text = "下一步：去“资产”双击类目，或直接去“运营”开始选品";
                }
                else
                {
                    _overviewNextActionLabel.Text = "下一步：在“运营”上传商品、取订单或下载面单";
                }
            }
        }

        private void ExportFeeRules(object sender, EventArgs e)
        {
            if (_snapshot == null || _snapshot.FeeRules == null || _snapshot.FeeRules.Count == 0)
            {
                SetStatus("当前没有可导出的运费规则。");
                return;
            }

            string path = Path.Combine(_paths.WorkRoot, "运费规则导出.xlsx");
            AssetCatalogService.ExportFeeRulesToExcel(_snapshot.FeeRules, path);
            _paths.OpenPath(path);
            SetStatus("运费规则已导出到：" + path);
        }

        private void FilterAssetsChanged(object sender, EventArgs e)
        {
            ApplyAssetFilter();
        }

        private void ApplyAssetFilter()
        {
            if (_snapshot == null || _snapshot.Categories == null || _snapshot.FeeRules == null)
            {
                return;
            }

            string keyword = _assetSearchBox == null ? string.Empty : _assetSearchBox.Text;
            List<CategoryNode> categories = AssetCatalogService.FilterCategories(_snapshot.Categories, keyword);
            List<FeeRule> rules = AssetCatalogService.FilterFeeRules(_snapshot.FeeRules, keyword);

            _categoryTree.BeginUpdate();
            _categoryTree.Nodes.Clear();

            int i;
            for (i = 0; i < categories.Count; i++)
            {
                _categoryTree.Nodes.Add(BuildTreeNode(categories[i]));
            }

            if (_categoryTree.Nodes.Count > 0)
            {
                _categoryTree.Nodes[0].Expand();
            }

            _categoryTree.EndUpdate();
            _feeGrid.DataSource = null;
            _feeGrid.DataSource = BuildFeeRuleDisplayRows(rules);
        }

        private void UseSelectedFeeRule(object sender, EventArgs e)
        {
            FeeRule rule = GetSelectedFeeRule();
            if (rule == null)
            {
                SetStatus("请先在运费规则表中选中一条规则。");
                return;
            }

            CategoryNode mappedNode = FindBestLeafCategoryForFeeRule(_snapshot == null ? null : _snapshot.Categories, rule);
            if (mappedNode == null)
            {
                SetStatus("这条运费规则没有匹配到可上传的 Ozon 类目，请从左侧类目树双击一个叶子类目。");
                return;
            }

            string categoryId = ResolveUploadCategoryId(mappedNode);
            string typeId = string.IsNullOrEmpty(mappedNode.DescriptionTypeId) ? "0" : mappedNode.DescriptionTypeId;
            string keyword = !string.IsNullOrEmpty(mappedNode.DescriptionTypeName)
                ? mappedNode.DescriptionTypeName
                : (!string.IsNullOrEmpty(rule.Category2) ? rule.Category2 : mappedNode.DescriptionCategoryName);
            FillAutomationCategory(categoryId, typeId, keyword);
            SelectOperationTab();
            SetStatus(T("ruleAppliedToAuto") + " [" + categoryId + "/" + typeId + "]");
        }

        private void UseSelectedCategoryForAutomation(object sender, TreeNodeMouseClickEventArgs e)
        {
            CategoryNode node = e == null ? null : e.Node.Tag as CategoryNode;
            if (node == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(node.DescriptionTypeId) || node.DescriptionTypeId == "0")
            {
                node = FindFirstOzonLeafCategory(node);
                if (node == null)
                {
                    SetStatus("Selected category has no uploadable Ozon type.");
                    return;
                }
            }

            string categoryId = ResolveUploadCategoryId(node);
            string typeId = string.IsNullOrEmpty(node.DescriptionTypeId) ? "0" : node.DescriptionTypeId;
            string keyword = !string.IsNullOrEmpty(node.DescriptionTypeName)
                ? node.DescriptionTypeName
                : node.DescriptionCategoryName;
            FillAutomationCategory(categoryId, typeId, keyword);
            SetStatus("已把类目树选中的 Category/Type 填入 Auto Sourcing：" + categoryId + " / " + typeId);
        }

        private void FillAutomationCategory(string categoryId, string typeId)
        {
            FillAutomationCategory(categoryId, typeId, null);
        }

        private void FillAutomationCategory(string categoryId, string typeId, string keyword)
        {
            if (_autoCategoryIdBox != null && !string.IsNullOrEmpty(categoryId))
            {
                _autoCategoryIdBox.Text = categoryId;
            }

            if (_autoTypeIdBox != null && !string.IsNullOrEmpty(typeId))
            {
                _autoTypeIdBox.Text = typeId;
            }

            if (_autoKeywordsBox != null && !string.IsNullOrEmpty(keyword))
            {
                _autoKeywordsBox.Text = keyword;
            }
            else if (_autoKeywordsBox != null)
            {
                _autoKeywordsBox.Text = string.Empty;
            }
            UpdateAutomationCategoryDisplay();
            UpdateOverviewCards();
        }

        private List<FeeRuleDisplayRow> BuildFeeRuleDisplayRows(IList<FeeRule> rules)
        {
            List<FeeRuleDisplayRow> rows = new List<FeeRuleDisplayRow>();
            for (int i = 0; rules != null && i < rules.Count; i++)
            {
                FeeRule rule = rules[i];
                if (rule == null)
                {
                    continue;
                }

                rows.Add(new FeeRuleDisplayRow
                {
                    Rule = rule,
                    Id = rule.Id,
                    CategoryId1 = rule.CategoryId1,
                    CategoryId2 = rule.CategoryId2,
                    Category1 = BuildBilingualCategoryName(rule.Category1),
                    Category2 = BuildBilingualCategoryName(rule.Category2),
                    FBS = rule.FBS,
                    FBS1500 = rule.FBS1500,
                    FBS5000 = rule.FBS5000,
                    FBP = rule.FBP,
                    FBP1500 = rule.FBP1500,
                    FBP5000 = rule.FBP5000,
                    FBO = rule.FBO,
                    FBO1500 = rule.FBO1500,
                    FBO5000 = rule.FBO5000
                });
            }

            return rows;
        }

        private string BuildBilingualCategoryName(string name)
        {
            string text = string.IsNullOrEmpty(name) ? string.Empty : name.Trim();
            if (string.IsNullOrEmpty(text) || IsAsciiKeyword(text))
            {
                return text;
            }

            string english = BuildEnglishKeywordFromRules(text);
            if (string.IsNullOrEmpty(english))
            {
                english = MakeFallbackKeywordFromCategory(text);
            }

            return text + " / " + english;
        }

        private string BuildEnglishKeyword(string keyword)
        {
            string text = string.IsNullOrEmpty(keyword) ? string.Empty : keyword.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            if (IsAsciiKeyword(text))
            {
                return text;
            }

            string ruleKeyword = BuildEnglishKeywordFromRules(text);
            if (!string.IsNullOrEmpty(ruleKeyword))
            {
                return ruleKeyword;
            }

            string aiKeyword = _automationService.GenerateEnglishCategoryKeyword(text);
            if (!string.IsNullOrEmpty(aiKeyword) &&
                aiKeyword.IndexOf("general merchandise", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return aiKeyword;
            }

            return MakeFallbackKeywordFromCategory(text);
        }

        private string BuildEnglishKeywordFromRules(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            if (ContainsAny(text, "\u60c5\u8da3", "\u6210\u4eba", "\u6027\u7231", "\u907f\u5b55", "\u5b89\u5168\u5957", "\u79c1\u5904"))
            {
                return "adult wellness product";
            }

            string[,] map = new string[,]
            {
                { "\u5c0f\u5de5\u5177", "hand tool accessory" },
                { "\u5546\u4e1a\u8bbe\u5907", "commercial equipment" },
                { "\u6253\u5370\u8017\u6750", "printer supplies" },
                { "\u58a8\u76d2", "printer ink cartridge" },
                { "\u7852\u9f13", "printer toner cartridge" },
                { "\u624b\u673a", "phone accessory" },
                { "\u5e73\u677f", "tablet accessory" },
                { "\u7535\u8111", "computer accessory" },
                { "\u6570\u7801", "electronics accessory" },
                { "\u7535\u5b50", "electronics accessory" },
                { "\u6c7d\u8f66", "car accessory" },
                { "\u6469\u6258", "motorcycle accessory" },
                { "\u81ea\u884c\u8f66", "bicycle accessory" },
                { "\u5ba0\u7269", "pet supplies" },
                { "\u732b", "cat supplies" },
                { "\u72d7", "dog supplies" },
                { "\u53a8\u623f", "kitchen organizer" },
                { "\u9910\u5177", "kitchen utensil" },
                { "\u70d8\u7119", "baking tool" },
                { "\u6536\u7eb3", "storage organizer" },
                { "\u6d74\u5ba4", "bathroom organizer" },
                { "\u536b\u6d74", "bathroom accessory" },
                { "\u5bb6\u5c45", "home improvement product" },
                { "\u4f4f\u5b85", "home improvement product" },
                { "\u56ed\u827a", "garden tool" },
                { "\u82b1\u56ed", "garden tool" },
                { "\u529e\u516c", "office organizer" },
                { "\u6587\u5177", "stationery supplies" },
                { "\u4e66\u5199", "writing supplies" },
                { "\u65c5\u884c", "travel organizer" },
                { "\u884c\u674e", "travel organizer" },
                { "\u8fd0\u52a8", "sports accessory" },
                { "\u5065\u8eab", "fitness accessory" },
                { "\u6237\u5916", "outdoor accessory" },
                { "\u513f\u7ae5", "kids product" },
                { "\u5a74\u513f", "baby product" },
                { "\u6bcd\u5a74", "baby product" },
                { "\u73a9\u5177", "kids toy" },
                { "\u6e05\u6d01", "cleaning tool" },
                { "\u5de5\u5177", "tool accessory" },
                { "\u670d\u88c5", "clothing accessory" },
                { "\u978b", "shoe accessory" },
                { "\u5185\u8863", "underwear organizer" },
                { "\u914d\u9970", "fashion accessory" },
                { "\u706f", "led light" },
                { "\u8282\u5e86", "party decoration" },
                { "\u88c5\u9970", "home decoration" },
                { "\u7f8e\u5bb9", "beauty tool" },
                { "\u5316\u5986", "makeup organizer" },
                { "\u62a4\u80a4", "skin care tool" },
                { "\u5065\u5eb7", "health care product" },
                { "\u533b\u7597", "health care accessory" },
                { "\u98df\u54c1", "food storage container" },
                { "\u5305\u88c5", "packing supplies" },
                { "\u5c55\u793a", "display stand" },
                { "\u652f\u67b6", "holder stand" },
                { "\u67b6", "storage rack" },
                { "\u76d2", "storage box" },
                { "\u888b", "storage bag" },
                { "\u7845\u80f6", "silicone product" },
                { "\u5851\u6599", "plastic product" }
            };

            for (int i = 0; i < map.GetLength(0); i++)
            {
                if (text.IndexOf(map[i, 0], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return map[i, 1];
                }
            }

            return string.Empty;
        }

        private string MakeFallbackKeywordFromCategory(string category)
        {
            string text = category ?? string.Empty;
            if (ContainsAny(text, "\u5c0f\u5de5\u5177"))
            {
                return "hand tool accessory";
            }
            if (ContainsAny(text, "\u5546\u4e1a\u8bbe\u5907"))
            {
                return "commercial equipment";
            }
            if (ContainsAny(text, "\u7535\u5b50\u4ea7\u54c1"))
            {
                return "electronics accessory";
            }
            if (ContainsAny(text, "\u65e5\u7528\u54c1"))
            {
                return "household supplies";
            }

            return "category specific product";
        }

        private bool ContainsAny(string text, params string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
            {
                if (text.IndexOf(needles[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsAsciiKeyword(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] > 127)
                {
                    return false;
                }
            }

            return true;
        }

        private bool StartCurrentProcess(string name)
        {
            if (_currentProcessCancel != null && !_currentProcessCancel.IsCancellationRequested)
            {
                SetStatus("Another process is already running: " + _currentProcessName);
                return false;
            }

            _currentProcessCancel = new CancellationTokenSource();
            _currentProcessName = name;
            return true;
        }

        private void FinishCurrentProcess()
        {
            if (_currentProcessCancel != null)
            {
                _currentProcessCancel.Dispose();
                _currentProcessCancel = null;
            }

            _currentProcessName = null;
        }

        private void ThrowIfCurrentProcessStopped()
        {
            if (_currentProcessCancel != null && _currentProcessCancel.IsCancellationRequested)
            {
                throw new OperationCanceledException();
            }
        }

        private void EmergencyStopCurrentProcess(object sender, EventArgs e)
        {
            if (_currentProcessCancel != null)
            {
                _currentProcessCancel.Cancel();
            }

            try
            {
                if (_browser != null && _browser.CoreWebView2 != null)
                {
                    _browser.CoreWebView2.Stop();
                }
            }
            catch
            {
            }

            _fullAutoReport = "Emergency brake pressed: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
                "Current process: " + SafeValue(_currentProcessName) + Environment.NewLine +
                "The running browser/navigation task was asked to stop.";
            SelectOverviewTab();
            UpdateOverview();
            SetStatus("Emergency brake requested.");
        }

        private List<string> GetSeedKeywords(IList<SourcingSeed> seeds)
        {
            List<string> values = new List<string>();
            for (int i = 0; seeds != null && i < seeds.Count; i++)
            {
                if (seeds[i] != null && !string.IsNullOrEmpty(seeds[i].Keyword))
                {
                    values.Add(seeds[i].Keyword);
                }
            }

            return values;
        }

        private async void RunAutoSourcing(object sender, EventArgs e)
        {
            if (!EnsureOperationReady("选品"))
            {
                return;
            }

            if (!StartCurrentProcess("Run 1688 selection"))
            {
                return;
            }

            StringBuilder report = new StringBuilder();
            try
            {
                EnsureSnapshot();
                SourcingOptions options = ReadSourcingOptions();
                List<SourcingSeed> seeds = ReadSourcingSeeds();
                if (_browser == null || _browser.CoreWebView2 == null)
                {
                    SetStatus("请先初始化浏览器并检测 1688 登录。");
                    SelectSetupTab();
                    return;
                }

                SelectSetupTab();
                report.AppendLine("Manual 1688 selection brief");
                report.AppendLine("---------------------------");
                report.AppendLine("Start: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                report.AppendLine("Keywords: " + string.Join(", ", GetSeedKeywords(seeds).ToArray()));
                SetStatus("Running 1688 selection...");
                ThrowIfCurrentProcessStopped();
                _lastSourcingResult = await Collect1688CandidatesFromBrowser(seeds, _snapshot.Config, options);
                ThrowIfCurrentProcessStopped();
                _autoResultGrid.DataSource = _lastSourcingResult.Products;
                UpdateOperationResultState();
                WriteAutomationLog(_lastSourcingResult.Logs);
                report.AppendLine("Candidates: " + _lastSourcingResult.Products.Count);
                report.AppendLine("End: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                _fullAutoReport = report.ToString();
                SelectOperationTab();
                UpdateOverview();
                SetStatus("1688 selection finished: " + _lastSourcingResult.Products.Count + " candidates.");
            }
            catch (OperationCanceledException)
            {
                report.AppendLine("Stopped by emergency brake: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                _fullAutoReport = report.ToString();
                SelectOperationTab();
                UpdateOverview();
                SetStatus("Current process stopped.");
            }
            catch (Exception ex)
            {
                AppendAutomationLog("ERROR: " + ex.Message);
                report.AppendLine("ERROR: " + ex.Message);
                _fullAutoReport = report.ToString();
                SelectOperationTab();
                UpdateOverview();
                MessageBox.Show(this, ex.ToString(), "Auto sourcing failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Auto sourcing failed: " + ex.Message);
            }
            finally
            {
                FinishCurrentProcess();
            }
        }

        private async void RunFullAutoLoop(object sender, EventArgs e)
        {
            if (!EnsureOperationReady("全自动循环"))
            {
                return;
            }

            if (!StartCurrentProcess("Full auto loop"))
            {
                return;
            }

            StringBuilder report = new StringBuilder();
            try
            {
                EnsureSnapshot();
                if (_browser == null || _browser.CoreWebView2 == null)
                {
                    SelectSetupTab();
                    SetStatus("请先初始化插件浏览器并登录 1688，然后再启动全链路循环。");
                    return;
                }

                int loopCount = (int)ParseLong(_autoLoopCountBox == null ? "1" : _autoLoopCountBox.Text);
                if (loopCount <= 0)
                {
                    loopCount = 1;
                }

                report.AppendLine("启动时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                report.AppendLine("计划循环：" + loopCount + " 次");
                SetStatus("全链路自动循环开始...");

                for (int i = 0; i < loopCount; i++)
                {
                    ThrowIfCurrentProcessStopped();
                    CategoryNode category = PickRandomOzonLeafCategory();
                    if (category == null)
                    {
                        report.AppendLine("Round " + (i + 1) + ": no usable Ozon leaf category; stopped.");
                        break;
                    }

                    string categoryKeyword = !string.IsNullOrEmpty(category.DescriptionTypeName) ? category.DescriptionTypeName : category.DescriptionCategoryName;
                    SelectOperationTab();
                    FillAutomationCategory(ResolveUploadCategoryId(category), category.DescriptionTypeId, categoryKeyword);
                    await Task.Delay(300);

                    report.AppendLine();
                    report.AppendLine("Round " + (i + 1));
                    report.AppendLine("Ozon category: " + category.DescriptionCategoryName + " / " + category.DescriptionTypeName + " [" + ResolveUploadCategoryId(category) + "/" + category.DescriptionTypeId + "]");
                    report.AppendLine("Keyword: " + categoryKeyword);

                    SelectSetupTab();
                    SourcingOptions options = ReadSourcingOptions();
                    List<SourcingSeed> seeds = ReadSourcingSeeds();
                    ThrowIfCurrentProcessStopped();
                    _lastSourcingResult = await Collect1688CandidatesFromBrowser(seeds, _snapshot.Config, options);
                    ThrowIfCurrentProcessStopped();
                    _autoResultGrid.DataSource = _lastSourcingResult.Products;
                    UpdateOperationResultState();
                    WriteAutomationLog(_lastSourcingResult.Logs);
                    report.AppendLine("选品候选：" + _lastSourcingResult.Products.Count + " 个");
                    _fullAutoReport = report.ToString();
                    SelectOperationTab();
                    UpdateOverview();
                    SetStatus("1688 抓取完成，正在提交 Ozon 上传...");

                    List<SourceProduct> uploadProducts = PickProductsForUpload(_lastSourcingResult.Products);
                    if (uploadProducts.Count == 0)
                    {
                        report.AppendLine("上传：跳过，没有可上传候选。");
                        continue;
                    }

                    try
                    {
                        OzonImportResult uploadResult = _automationService.UploadToOzon(
                            uploadProducts,
                            options,
                            _ozonClientIdBox.Text.Trim(),
                            _ozonApiKeyBox.Text.Trim(),
                            delegate(string line)
                            {
                                AppendAutomationLog(line);
                            });

                        if (uploadResult.Success)
                        {
                            report.AppendLine("Ozon task submitted: " + uploadProducts.Count + " items, task_id=" + SafeValue(uploadResult.TaskId));

                            ThrowIfCurrentProcessStopped();
                            OzonImportResult importResult = await Task.Run(() => _automationService.WaitForOzonImportInfo(
                                uploadResult.TaskId,
                                _ozonClientIdBox.Text.Trim(),
                                _ozonApiKeyBox.Text.Trim(),
                                12,
                                10000));

                            report.AppendLine("Ozon import result:");
                            report.AppendLine(importResult.ImportSummary);
                            importResult = await RetryFailedOzonImportsOnce(uploadProducts, options, importResult, _ozonClientIdBox.Text.Trim(), _ozonApiKeyBox.Text.Trim(), delegate(string line)
                            {
                                report.AppendLine(line);
                            });
                            if (importResult.AcceptedOfferIds.Count > 0)
                            {
                                if (!importResult.Success)
                                {
                                    report.AppendLine("Ozon import had partial failures, but accepted offers will still continue to SKU/stock update.");
                                }

                                try
                                {
                                    List<string> readyOfferIds = await WaitForSkuCreationWithBrief(report, importResult.AcceptedOfferIds, 20, 30000);
                                    if (readyOfferIds.Count == 0)
                                    {
                                        report.AppendLine("Ozon SKU creation is still pending; stock update skipped until SKU exists.");
                                    }
                                    else
                                    {
                                        report.AppendLine("SKU 已创建，正在设置库存 100...");
                                        _fullAutoReport = report.ToString();
                                        UpdateOverview();
                                        string stockResponse = await Task.Run(() => _automationService.SetOzonStockTo100(
                                            readyOfferIds,
                                            _ozonClientIdBox.Text.Trim(),
                                            _ozonApiKeyBox.Text.Trim()));
                                        report.AppendLine("Ozon stock set to 100: " + stockResponse);
                                    }
                                }
                                catch (Exception stockEx)
                                {
                                    report.AppendLine("Ozon stock update failed: " + stockEx.Message);
                                    string stockLogPath = WriteExceptionLog("ozon-stock", stockEx, report.ToString());
                                    if (!string.IsNullOrEmpty(stockLogPath))
                                    {
                                        report.AppendLine("库存异常日志：" + stockLogPath);
                                    }
                                }
                            }

                            if (!importResult.Success)
                            {
                                report.AppendLine("Ozon import was not accepted. Check missing attributes/category requirements above.");
                            }
                        }
                        else
                        {
                            report.AppendLine("Ozon upload failed: " + uploadResult.ErrorMessage);
                            report.AppendLine("本轮未成功上架到 Ozon，请以上传失败/校验失败信息为准。");
                        }
                    }
                    catch (Exception uploadEx)
                    {
                        report.AppendLine("Ozon upload exception: " + uploadEx.Message);
                        string uploadLogPath = WriteExceptionLog("ozon-upload", uploadEx, report.ToString());
                        if (!string.IsNullOrEmpty(uploadLogPath))
                        {
                            report.AppendLine("上传异常日志：" + uploadLogPath);
                        }
                        report.AppendLine("本轮未成功上架到 Ozon，请先处理上述异常后再重试。");
                    }

                    _fullAutoReport = report.ToString();
                    UpdateOverview();
                }

                report.AppendLine();
                report.AppendLine("结束时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                _fullAutoReport = report.ToString();
                SelectOverviewTab();
                UpdateOverview();
                SetStatus("全链路自动循环完成。");
            }
            catch (OperationCanceledException)
            {
                report.AppendLine();
                report.AppendLine("Stopped by emergency brake: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                _fullAutoReport = report.ToString();
                SelectOverviewTab();
                UpdateOverview();
                SetStatus("Current process stopped.");
            }
            catch (Exception ex)
            {
                report.AppendLine();
                report.AppendLine("异常：" + ex.Message);
                string fullRunLogPath = WriteExceptionLog("full-auto", ex, report.ToString());
                if (!string.IsNullOrEmpty(fullRunLogPath))
                {
                    report.AppendLine("完整异常日志：" + fullRunLogPath);
                }
                _fullAutoReport = report.ToString();
                SelectOverviewTab();
                UpdateOverview();
                MessageBox.Show(this, ex.ToString(), "全链路自动循环失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("全链路自动循环失败：" + ex.Message);
            }
            finally
            {
                FinishCurrentProcess();
            }
        }

        private FeeRule PickRandomSecondLevelRule()
        {
            if (_snapshot == null || _snapshot.FeeRules == null || _snapshot.FeeRules.Count == 0)
            {
                return null;
            }

            List<FeeRule> candidates = new List<FeeRule>();
            for (int i = 0; i < _snapshot.FeeRules.Count; i++)
            {
                FeeRule rule = _snapshot.FeeRules[i];
                if (rule != null && rule.CategoryId1 > 0 && rule.CategoryId2 > 0 && !string.IsNullOrEmpty(rule.Category2))
                {
                    candidates.Add(rule);
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            return candidates[_random.Next(candidates.Count)];
        }

        private async Task<List<string>> WaitForSkuCreationWithBrief(StringBuilder report, IList<string> offerIds, int attempts, int delayMs)
        {
            List<string> ready = new List<string>();
            if (offerIds == null || offerIds.Count == 0)
            {
                report.AppendLine("SKU wait skipped: no accepted offer_id values.");
                return ready;
            }

            SelectOverviewTab();
            report.AppendLine("绛夊緟 Ozon 鍒涘缓 SKU...");
            _fullAutoReport = report.ToString();
            UpdateOverview();

            int maxAttempts = attempts <= 0 ? 20 : attempts;
            int wait = delayMs <= 0 ? 30000 : delayMs;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                ThrowIfCurrentProcessStopped();
                if (attempt > 0)
                {
                    await Task.Delay(wait);
                }

                Dictionary<string, string> skuByOffer = await Task.Run(() => _automationService.GetOzonSkuMap(
                    offerIds,
                    _ozonClientIdBox.Text.Trim(),
                    _ozonApiKeyBox.Text.Trim()));

                ready.Clear();
                for (int i = 0; i < offerIds.Count; i++)
                {
                    string offerId = offerIds[i];
                    if (skuByOffer.ContainsKey(offerId) && !string.IsNullOrEmpty(skuByOffer[offerId]))
                    {
                        ready.Add(offerId);
                    }
                }

                report.AppendLine("SKU 检查 " + (attempt + 1) + "/" + maxAttempts + "：" + ready.Count + "/" + offerIds.Count + " 已创建");
                int shown = 0;
                foreach (KeyValuePair<string, string> pair in skuByOffer)
                {
                    if (shown >= 5)
                    {
                        break;
                    }

                    report.AppendLine("  " + pair.Key + " -> " + (string.IsNullOrEmpty(pair.Value) ? "绛夊緟 SKU" : "SKU " + pair.Value));
                    shown += 1;
                }

                _fullAutoReport = report.ToString();
                UpdateOverview();
                SetStatus("等待 Ozon SKU 创建：" + ready.Count + "/" + offerIds.Count + " 已创建");

                if (ready.Count >= offerIds.Count)
                {
                    report.AppendLine("SKU 创建完成，可以开始设置库存。");
                    _fullAutoReport = report.ToString();
                    UpdateOverview();
                    return new List<string>(ready);
                }
            }

            report.AppendLine("SKU 等待超时：Ozon 仍在处理，已创建 " + ready.Count + "/" + offerIds.Count + "。");
            _fullAutoReport = report.ToString();
            UpdateOverview();
            return new List<string>(ready);
        }

        private CategoryNode PickRandomOzonLeafCategory()
        {
            EnsureSnapshot();
            List<CategoryNode> candidates = new List<CategoryNode>();
            CollectOzonLeafCategories(_snapshot == null ? null : _snapshot.Categories, candidates);
            if (candidates.Count == 0)
            {
                return null;
            }

            return candidates[_random.Next(candidates.Count)];
        }

        private void CollectOzonLeafCategories(IList<CategoryNode> nodes, IList<CategoryNode> output)
        {
            for (int i = 0; nodes != null && i < nodes.Count; i++)
            {
                CategoryNode node = nodes[i];
                if (node == null || node.Disabled)
                {
                    continue;
                }

                if (IsHiddenFromAutoLoopCategory(node))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(node.DescriptionCategoryId) &&
                    !string.IsNullOrEmpty(node.DescriptionTypeId) &&
                    node.DescriptionCategoryId != "0" &&
                    node.DescriptionTypeId != "0")
                {
                    output.Add(node);
                }

                CollectOzonLeafCategories(node.Children, output);
            }
        }

        private CategoryNode FindFirstOzonLeafCategory(CategoryNode node)
        {
            if (node == null || node.Disabled)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(node.DescriptionCategoryId) &&
                !string.IsNullOrEmpty(node.DescriptionTypeId) &&
                node.DescriptionCategoryId != "0" &&
                node.DescriptionTypeId != "0")
            {
                return node;
            }

            for (int i = 0; node.Children != null && i < node.Children.Count; i++)
            {
                CategoryNode found = FindFirstOzonLeafCategory(node.Children[i]);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static bool IsHiddenFromAutoLoopCategory(CategoryNode node)
        {
            if (node == null)
            {
                return false;
            }

            string text = ((node.DescriptionCategoryName ?? string.Empty) + " " + (node.DescriptionTypeName ?? string.Empty)).ToLowerInvariant();
            string[] hiddenTerms = new string[]
            {
                "成人", "情趣", "避孕", "安全套", "成人用品", "adult", "sex", "sexual", "condom", "эрот"
            };

            for (int i = 0; i < hiddenTerms.Length; i++)
            {
                if (text.IndexOf(hiddenTerms[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string ResolveUploadCategoryId(CategoryNode node)
        {
            if (node == null)
            {
                return "0";
            }

            string categoryId = !string.IsNullOrEmpty(node.UploadCategoryId)
                ? node.UploadCategoryId
                : node.DescriptionCategoryId;
            return string.IsNullOrEmpty(categoryId) ? "0" : categoryId;
        }

        private CategoryNode FindBestLeafCategoryForFeeRule(IList<CategoryNode> nodes, FeeRule rule)
        {
            if (rule == null)
            {
                return null;
            }

            CategoryNode bySecondLevel = FindLeafCategoryByCategoryId(nodes, rule.CategoryId2, new List<long>());
            if (bySecondLevel != null)
            {
                return bySecondLevel;
            }

            return FindLeafCategoryByCategoryId(nodes, rule.CategoryId1, new List<long>());
        }

        private CategoryNode FindLeafCategoryByCategoryId(IList<CategoryNode> nodes, long targetCategoryId, IList<long> ancestors)
        {
            if (targetCategoryId <= 0)
            {
                return null;
            }

            for (int i = 0; nodes != null && i < nodes.Count; i++)
            {
                CategoryNode node = nodes[i];
                if (node == null || node.Disabled)
                {
                    continue;
                }

                List<long> nextAncestors = new List<long>(ancestors);
                long nodeCategoryId = ParseLong(node.DescriptionCategoryId);
                if (nodeCategoryId > 0 && !nextAncestors.Contains(nodeCategoryId))
                {
                    nextAncestors.Add(nodeCategoryId);
                }

                string uploadCategoryIdText = ResolveUploadCategoryId(node);
                long uploadCategoryId = ParseLong(uploadCategoryIdText);
                bool matches = nodeCategoryId == targetCategoryId ||
                    uploadCategoryId == targetCategoryId ||
                    nextAncestors.Contains(targetCategoryId);

                bool isLeaf = !string.IsNullOrEmpty(node.DescriptionTypeId) &&
                    node.DescriptionTypeId != "0" &&
                    !string.IsNullOrEmpty(node.DescriptionCategoryId) &&
                    node.DescriptionCategoryId != "0";
                if (matches && isLeaf)
                {
                    return node;
                }

                CategoryNode found = FindLeafCategoryByCategoryId(node.Children, targetCategoryId, nextAncestors);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private List<SourceProduct> PickProductsForUpload(IList<SourceProduct> products)
        {
            List<SourceProduct> selected = new List<SourceProduct>();
            if (products == null)
            {
                return selected;
            }

            for (int i = 0; i < products.Count && selected.Count < 3; i++)
            {
                SourceProduct product = products[i];
                if (product != null &&
                    !IsComplianceRestrictedProduct(product) &&
                    string.Equals(product.Decision, "Go", StringComparison.OrdinalIgnoreCase) &&
                    IsStrongKeywordMatch(product))
                {
                    selected.Add(product);
                }
            }

            if (selected.Count == 0)
            {
                for (int i = 0; i < products.Count; i++)
                {
                    SourceProduct product = products[i];
                    if (product != null &&
                        !IsComplianceRestrictedProduct(product) &&
                        string.Equals(product.Decision, "Watch", StringComparison.OrdinalIgnoreCase) &&
                        IsStrongKeywordMatch(product))
                    {
                        selected.Add(product);
                        break;
                    }
                }
            }

            return selected;
        }

        private static bool IsComplianceRestrictedProduct(SourceProduct product)
        {
            if (product == null)
            {
                return false;
            }

            StringBuilder builder = new StringBuilder();
            builder.Append(product.Title).Append(' ')
                .Append(product.Keyword).Append(' ')
                .Append(product.Reason).Append(' ')
                .Append(product.ShopName);

            foreach (KeyValuePair<string, string> pair in product.Attributes)
            {
                builder.Append(' ').Append(pair.Key).Append(' ').Append(pair.Value);
            }

            string text = builder.ToString().ToLowerInvariant();
            string[] restrictedTerms = new string[]
            {
                "轮椅", "助行", "拐杖", "康复", "护理床", "病床", "矫形", "残疾", "残障",
                "医疗", "医用", "药品", "药物", "保健", "wheelchair", "walker", "crutch",
                "rehabilitation", "orthopedic", "disabled", "invalid", "medical", "medicine",
                "инвалид", "коляск", "медицин",
                "杀虫", "杀虫剂", "除虫", "灭虫", "驱虫", "农药", "气雾剂", "insecticide", "pesticide", "repellent",
                "инсектицид", "пестицид", "аэрозоль", "яд"
            };

            for (int i = 0; i < restrictedTerms.Length; i++)
            {
                if (text.IndexOf(restrictedTerms[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private async Task<SourcingResult> Collect1688CandidatesFromBrowser(IList<SourcingSeed> seeds, AppConfig config, SourcingOptions options)
        {
            SourcingResult result = new SourcingResult();
            Dictionary<string, SourceProduct> byOfferId = new Dictionary<string, SourceProduct>();
            int perKeywordLimit = options.PerKeywordLimit <= 0 ? 5 : options.PerKeywordLimit;
            int detailLimit = options.DetailLimit <= 0 ? 12 : options.DetailLimit;

            for (int i = 0; seeds != null && i < seeds.Count; i++)
            {
                SourcingSeed seed = seeds[i];
                if (seed == null || string.IsNullOrEmpty(seed.Keyword))
                {
                    continue;
                }

                AppendAutomationLog("Search 1688 in browser: " + seed.Keyword);
                string url = "https://s.1688.com/selloffer/offer_search.htm?keywords=" + Encode1688Keyword(seed.Keyword);
                await NavigateAndWait(url, 1500);
                bool rendered = await WaitForSearchResults(seed.Keyword, 35000);
                result.Logs.Add(seed.Keyword + " render wait: " + (rendered ? "ready" : "timeout"));
                List<SourceProduct> cards = await ScrapeSearchPage(seed.Keyword, perKeywordLimit);
                if (cards.Count == 0)
                {
                    AppendAutomationLog("Search cards not ready, retrying after extra wait: " + seed.Keyword);
                    await Task.Delay(8000);
                    cards = await ScrapeSearchPage(seed.Keyword, perKeywordLimit);
                }
                result.Logs.Add(seed.Keyword + " search cards: " + cards.Count);

                for (int j = 0; j < cards.Count; j++)
                {
                    SourceProduct card = cards[j];
                    if (!string.IsNullOrEmpty(card.OfferId) && !byOfferId.ContainsKey(card.OfferId))
                    {
                        byOfferId[card.OfferId] = card;
                    }
                }
            }

            List<SourceProduct> candidates = new List<SourceProduct>(byOfferId.Values);
            int maxDetails = Math.Min(detailLimit, candidates.Count);
            for (int i = 0; i < maxDetails; i++)
            {
                SourceProduct candidate = candidates[i];
                AppendAutomationLog("Detail " + (i + 1) + "/" + maxDetails + ": " + candidate.OfferId);
                await NavigateAndWait(candidate.SourceUrl, 5500);
                SourceProduct detail = await ScrapeDetailPage(candidate);
                MergeBrowserProduct(candidate, detail);
                ScoreBrowserCandidate(candidate, config);
            }

            for (int i = maxDetails; i < candidates.Count; i++)
            {
                ScoreBrowserCandidate(candidates[i], config);
            }

            candidates.Sort(delegate(SourceProduct left, SourceProduct right)
            {
                int decision = BrowserDecisionRank(right.Decision).CompareTo(BrowserDecisionRank(left.Decision));
                if (decision != 0) return decision;
                return right.Score.CompareTo(left.Score);
            });

            result.Products.AddRange(candidates);
            result.Logs.Add("Browser scrape finished: " + result.Products.Count + " candidates.");
            return result;
        }

        private async Task NavigateAndWait(string url, int waitAfterMs)
        {
            TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
            EventHandler<CoreWebView2NavigationCompletedEventArgs> handler = null;
            handler = delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                _browser.CoreWebView2.NavigationCompleted -= handler;
                completion.TrySetResult(true);
            };

            _browser.CoreWebView2.NavigationCompleted += handler;
            _browser.CoreWebView2.Navigate(url);
            Task finished = await Task.WhenAny(completion.Task, Task.Delay(25000));
            if (finished != completion.Task)
            {
                _browser.CoreWebView2.NavigationCompleted -= handler;
            }

            await Task.Delay(waitAfterMs);
        }

        private async Task<bool> WaitForSearchResults(string keyword, int timeoutMs)
        {
            DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMs <= 0 ? 30000 : timeoutMs);
            string script = @"
(function(){
  var links = document.querySelectorAll('a[href*=""offer""], a[href*=""detail.1688.com""]');
  if (links && links.length > 0) return true;
  return /offer\/\d+\.html|offerId=\d+|object_id=\d+/.test(document.body ? document.body.innerHTML : '');
})();";

            while (DateTime.Now < deadline)
            {
                ThrowIfCurrentProcessStopped();
                try
                {
                    string value = await _browser.CoreWebView2.ExecuteScriptAsync(script);
                    if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                }

                AppendAutomationLog("Waiting 1688 render: " + keyword);
                await Task.Delay(1000);
            }

            return false;
        }

        private async Task<List<SourceProduct>> ScrapeSearchPage(string keyword, int limit)
        {
            string script = @"
(function(){
  function text(node){ return (node && (node.innerText || node.textContent) || '').replace(/\s+/g,' ').trim(); }
  function attr(node, name){ return node ? (node.getAttribute(name) || '') : ''; }
  function abs(url){ try { return new URL(url, location.href).href; } catch(e) { return url || ''; } }
  function offerId(url){ var m=String(url||'').match(/offer\/(\d+)\.html|offerId=(\d+)|object_id=(\d+)/i); return m ? (m[1]||m[2]||m[3]||'') : ''; }
  var anchors = Array.prototype.slice.call(document.querySelectorAll('a[href*=""offer""], a[href*=""detail.1688.com""]'));
  var seen = {};
  var items = [];
  anchors.forEach(function(a){
    var href = abs(a.href);
    var id = offerId(href);
    if(!id || seen[id]) return;
    seen[id] = true;
    var card = a;
    for(var i=0;i<6 && card && card.parentElement;i++){
      card = card.parentElement;
      if(text(card).length > 30) break;
    }
    var img = card ? card.querySelector('img') : null;
    var title = attr(a,'title') || attr(img,'alt') || text(a) || text(card).slice(0,80);
    var blockText = text(card);
    var priceMatch = blockText.match(/[¥￥]\s*([0-9]+(?:\.[0-9]+)?)/) || blockText.match(/([0-9]+(?:\.[0-9]+)?)\s*元/);
    var salesMatch = blockText.match(/([0-9.]+)\s*(万)?\s*(?:人付款|成交|已售|销量)/);
    var sales = 0;
    if(salesMatch){ sales = parseFloat(salesMatch[1] || '0') || 0; if(salesMatch[2]) sales *= 10000; }
    items.push({
      OfferId:id,
      Title:title,
      SourceUrl:'https://detail.1688.com/offer/' + id + '.html',
      PriceText:priceMatch ? priceMatch[1] : '',
      PriceCny:priceMatch ? parseFloat(priceMatch[1]) || 0 : 0,
      SalesCount:Math.round(sales),
      ShopName:'',
      ShopUrl:'',
      MainImage:img ? abs(img.currentSrc || img.src || attr(img,'data-src')) : '',
      Images:img ? [abs(img.currentSrc || img.src || attr(img,'data-src'))] : [],
      Keyword:'" + EscapeJavaScript(keyword) + @"',
      Attributes:{}
    });
  });
  return items.slice(0," + limit + @");
})();";
            string json = await _browser.CoreWebView2.ExecuteScriptAsync(script);
            return ParseBrowserProducts(json);
        }

        private async Task<SourceProduct> ScrapeDetailPage(SourceProduct fallback)
        {
            string script = @"
(function(){
  function text(node){ return (node && (node.innerText || node.textContent) || '').replace(/\s+/g,' ').trim(); }
  function attr(node, name){ return node ? (node.getAttribute(name) || '') : ''; }
  function abs(url){ try { return new URL(url, location.href).href; } catch(e) { return url || ''; } }
  function offerId(url){ var m=String(url||location.href).match(/offer\/(\d+)\.html|offerId=(\d+)/i); return m ? (m[1]||m[2]||'') : ''; }
  var title = attr(document.querySelector('meta[property=""og:title""]'),'content') || text(document.querySelector('h1')) || document.title;
  var body = text(document.body);
  var priceMatch = body.match(/[¥￥]\s*([0-9]+(?:\.[0-9]+)?)/) || body.match(/价格\s*[:：]?\s*([0-9]+(?:\.[0-9]+)?)/);
  var salesMatch = body.match(/([0-9.]+)\s*(万)?\s*(?:人付款|成交|已售|销量)/);
  var sales = 0;
  if(salesMatch){ sales = parseFloat(salesMatch[1] || '0') || 0; if(salesMatch[2]) sales *= 10000; }
  function bestImg(node){
    var srcset = attr(node,'srcset');
    if(srcset){
      var first = srcset.split(',')[0].trim().split(' ')[0];
      if(first) return abs(first);
    }
    return abs(node.currentSrc || node.src || attr(node,'data-src') || attr(node,'data-lazy-src') || attr(node,'data-ks-lazyload') || attr(node,'data-img-url'));
  }
  var imgs = Array.prototype.slice.call(document.querySelectorAll('img, source'))
    .map(function(img){ return bestImg(img); })
    .filter(function(url){ return /alicdn|cbu|1688/i.test(url) && !/avatar|logo|icon|lazyload/i.test(url); });
  var unique = [];
  imgs.forEach(function(url){ if(url && unique.indexOf(url) < 0) unique.push(url.split('?')[0]); });
  var attrs = {};
  function addAttr(k, v){
    k = (k || '').replace(/[：:]+$/,'').trim();
    v = (v || '').trim();
    if(!k || !v || k.length > 30 || v.length > 120) return;
    if(Object.keys(attrs).length >= 40) return;
    if(!attrs[k]) attrs[k] = v;
  }
  Array.prototype.slice.call(document.querySelectorAll('tr')).forEach(function(row){
    var cells = row.querySelectorAll('th,td');
    if(cells.length >= 2){
      addAttr(text(cells[0]), text(cells[cells.length - 1]));
    }
  });
  Array.prototype.slice.call(document.querySelectorAll('dl')).forEach(function(row){
    addAttr(text(row.querySelector('dt')), text(row.querySelector('dd')));
  });
  Array.prototype.slice.call(document.querySelectorAll('li, .offer-attr, .mod-detail-attributes')).slice(0,120).forEach(function(row){
    var t = text(row);
    var m = t.match(/^(.{1,30})[:：]\s*(.{1,120})$/);
    if(m) addAttr(m[1], m[2]);
  });
  return {
    OfferId:offerId(location.href),
    Title:title.replace(/\s*[-_].*1688.*$/i,'').slice(0,160),
    SourceUrl:location.href,
    PriceText:priceMatch ? priceMatch[1] : '',
    PriceCny:priceMatch ? parseFloat(priceMatch[1]) || 0 : 0,
    SalesCount:Math.round(sales),
    ShopName:text(document.querySelector('[class*=""shop""] a, [class*=""company""] a')).slice(0,80),
    ShopUrl:abs(attr(document.querySelector('a[href*=""shop.1688.com""]'),'href')),
    MainImage:unique[0] || '',
    Images:unique.slice(0,12),
    Attributes:attrs
  };
})();";
            string json = await _browser.CoreWebView2.ExecuteScriptAsync(script);
            List<SourceProduct> products = ParseBrowserProducts("[" + json + "]");
            return products.Count > 0 ? products[0] : fallback;
        }

        private List<SourceProduct> ParseBrowserProducts(string json)
        {
            List<SourceProduct> products = new List<SourceProduct>();
            if (string.IsNullOrEmpty(json) || json == "null")
            {
                return products;
            }

            JArray array = JArray.Parse(json);
            for (int i = 0; i < array.Count; i++)
            {
                JObject item = array[i] as JObject;
                if (item == null)
                {
                    continue;
                }

                SourceProduct product = new SourceProduct();
                product.OfferId = JsonString(item, "OfferId");
                product.Title = JsonString(item, "Title");
                product.SourceUrl = JsonString(item, "SourceUrl");
                product.PriceText = JsonString(item, "PriceText");
                product.PriceCny = JsonDecimal(item, "PriceCny");
                product.SalesCount = (int)JsonDecimal(item, "SalesCount");
                product.ShopName = JsonString(item, "ShopName");
                product.ShopUrl = JsonString(item, "ShopUrl");
                product.MainImage = JsonString(item, "MainImage");
                product.Keyword = JsonString(item, "Keyword");

                JArray images = item["Images"] as JArray;
                for (int j = 0; images != null && j < images.Count; j++)
                {
                    string image = Convert.ToString(images[j]);
                    if (!string.IsNullOrEmpty(image) && !product.Images.Contains(image))
                    {
                        product.Images.Add(image);
                    }
                }

                JObject attrs = item["Attributes"] as JObject;
                if (attrs != null)
                {
                    foreach (JProperty property in attrs.Properties())
                    {
                        product.Attributes[property.Name] = Convert.ToString(property.Value);
                    }
                }

                if (!string.IsNullOrEmpty(product.OfferId))
                {
                    products.Add(product);
                }
            }

            return products;
        }

        private void MergeBrowserProduct(SourceProduct target, SourceProduct detail)
        {
            if (target == null || detail == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(detail.Title)) target.Title = detail.Title;
            if (!string.IsNullOrEmpty(detail.SourceUrl)) target.SourceUrl = detail.SourceUrl;
            if (detail.PriceCny > 0) target.PriceCny = detail.PriceCny;
            if (!string.IsNullOrEmpty(detail.PriceText)) target.PriceText = detail.PriceText;
            if (detail.SalesCount > 0) target.SalesCount = detail.SalesCount;
            if (!string.IsNullOrEmpty(detail.ShopName)) target.ShopName = detail.ShopName;
            if (!string.IsNullOrEmpty(detail.ShopUrl)) target.ShopUrl = detail.ShopUrl;
            if (!string.IsNullOrEmpty(detail.MainImage)) target.MainImage = detail.MainImage;
            if (detail.Images.Count > 0) target.Images = detail.Images;
            foreach (KeyValuePair<string, string> pair in detail.Attributes)
            {
                target.Attributes[pair.Key] = pair.Value;
            }
        }

        private void ScoreBrowserCandidate(SourceProduct product, AppConfig config)
        {
            decimal score = 0m;
            List<string> reasons = new List<string>();
            if (product.PriceCny > 0) score += 20m; else reasons.Add("missing price");
            if (product.SalesCount > 0) score += Math.Min(25m, product.SalesCount / 20m); else reasons.Add("missing sales");
            if (!string.IsNullOrEmpty(product.MainImage) || product.Images.Count > 0) score += 20m; else reasons.Add("missing image");
            if (product.Attributes.Count > 0) score += Math.Min(15m, product.Attributes.Count * 2m);
            if (!string.IsNullOrEmpty(product.ShopName)) score += 10m;
            int relevance = ComputeKeywordRelevance(product);
            if (relevance >= 55)
            {
                score += 20m;
            }
            else if (relevance >= 35)
            {
                score += 10m;
            }
            else
            {
                reasons.Add("weak keyword relevance");
            }

            if (config != null && config.MinSaleNum > 0 && product.SalesCount > 0 && product.SalesCount < config.MinSaleNum) reasons.Add("sales below config");
            if (IsComplianceRestrictedProduct(product)) reasons.Add("restricted compliance category");
            product.Score = Math.Round(score, 2);
            product.Decision = reasons.Count == 0 && score >= 45m ? "Go" : (score >= 30m ? "Watch" : "No-Go");
            product.Reason = reasons.Count == 0 ? "browser scrape signals look usable" : string.Join("; ", reasons.ToArray());
        }

        private static bool IsStrongKeywordMatch(SourceProduct product)
        {
            return ComputeKeywordRelevance(product) >= 35;
        }

        private static int ComputeKeywordRelevance(SourceProduct product)
        {
            if (product == null)
            {
                return 0;
            }

            string keyword = Convert.ToString(product.Keyword ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(keyword))
            {
                return 100;
            }

            string haystack = BuildKeywordHaystack(product);
            string normalizedHaystack = NormalizeKeywordText(haystack);
            string normalizedTitle = NormalizeKeywordText(product.Title);
            string normalizedKeyword = NormalizeKeywordText(keyword);
            if (string.IsNullOrEmpty(normalizedHaystack) || string.IsNullOrEmpty(normalizedKeyword))
            {
                return 0;
            }

            int score = 0;
            if (normalizedTitle.IndexOf(normalizedKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 60;
            }
            else if (normalizedHaystack.IndexOf(normalizedKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 45;
            }

            List<string> tokens = BuildKeywordTokens(keyword);
            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];
                string normalizedToken = NormalizeKeywordText(token);
                if (string.IsNullOrEmpty(normalizedToken))
                {
                    continue;
                }

                if (normalizedTitle.IndexOf(normalizedToken, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += normalizedToken.Length >= 4 ? 18 : 12;
                }
                else if (normalizedHaystack.IndexOf(normalizedToken, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += normalizedToken.Length >= 4 ? 10 : 6;
                }
            }

            return Math.Min(100, score);
        }

        private static string BuildKeywordHaystack(SourceProduct product)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(product.Title).Append(' ')
                .Append(product.Keyword).Append(' ')
                .Append(product.ShopName).Append(' ');

            foreach (KeyValuePair<string, string> pair in product.Attributes)
            {
                builder.Append(pair.Key).Append(' ').Append(pair.Value).Append(' ');
            }

            return builder.ToString();
        }

        private static List<string> BuildKeywordTokens(string keyword)
        {
            List<string> tokens = new List<string>();
            Dictionary<string, bool> unique = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            string[] pieces = (keyword ?? string.Empty)
                .Replace("/", " ")
                .Replace("\\", " ")
                .Replace(",", " ")
                .Replace("，", " ")
                .Replace("|", " ")
                .Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < pieces.Length; i++)
            {
                string piece = pieces[i].Trim();
                AddKeywordToken(tokens, unique, piece);
                if (ContainsChinese(piece) && piece.Length >= 4)
                {
                    for (int j = 0; j <= piece.Length - 2; j++)
                    {
                        AddKeywordToken(tokens, unique, piece.Substring(j, 2));
                    }
                }
            }

            return tokens;
        }

        private static void AddKeywordToken(List<string> tokens, Dictionary<string, bool> unique, string token)
        {
            string normalized = NormalizeKeywordText(token);
            if (string.IsNullOrEmpty(normalized) || normalized.Length < 2 || unique.ContainsKey(normalized))
            {
                return;
            }

            unique[normalized] = true;
            tokens.Add(token);
        }

        private static string NormalizeKeywordText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            string lowered = value.Trim().ToLowerInvariant();
            for (int i = 0; i < lowered.Length; i++)
            {
                char c = lowered[i];
                if (char.IsLetterOrDigit(c) || (c >= 0x4E00 && c <= 0x9FFF))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        private static bool ContainsChinese(string value)
        {
            for (int i = 0; !string.IsNullOrEmpty(value) && i < value.Length; i++)
            {
                char c = value[i];
                if (c >= 0x4E00 && c <= 0x9FFF)
                {
                    return true;
                }
            }

            return false;
        }

        private int BrowserDecisionRank(string decision)
        {
            if (string.Equals(decision, "Go", StringComparison.OrdinalIgnoreCase)) return 3;
            if (string.Equals(decision, "Watch", StringComparison.OrdinalIgnoreCase)) return 2;
            return 1;
        }

        private static string JsonString(JObject obj, string name)
        {
            JToken token = obj[name];
            return token == null || token.Type == JTokenType.Null ? string.Empty : Convert.ToString(token);
        }

        private static decimal JsonDecimal(JObject obj, string name)
        {
            decimal value;
            return decimal.TryParse(JsonString(obj, name), out value) ? value : 0m;
        }

        private static string EscapeJavaScript(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("'", "\\'");
        }

        private static string Encode1688Keyword(string value)
        {
            byte[] bytes = Encoding.GetEncoding("GB18030").GetBytes(value ?? string.Empty);
            StringBuilder builder = new StringBuilder(bytes.Length * 3);
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                bool keep = (b >= (byte)'A' && b <= (byte)'Z') ||
                    (b >= (byte)'a' && b <= (byte)'z') ||
                    (b >= (byte)'0' && b <= (byte)'9') ||
                    b == (byte)'-' || b == (byte)'_' || b == (byte)'.' || b == (byte)'~';
                if (keep)
                {
                    builder.Append((char)b);
                }
                else if (b == (byte)' ')
                {
                    builder.Append("%20");
                }
                else
                {
                    builder.Append('%');
                    builder.Append(b.ToString("X2"));
                }
            }

            return builder.ToString();
        }

        private async void UploadSelectedToOzon(object sender, EventArgs e)
        {
            if (!EnsureOperationReady("上传选中"))
            {
                return;
            }

            try
            {
                List<SourceProduct> selected = GetSelectedSourceProducts();
                if (selected.Count == 0)
                {
                    SetStatus("No selected candidates.");
                    return;
                }

                SourcingOptions options = ReadSourcingOptions();
                OzonImportResult result = _automationService.UploadToOzon(
                    selected,
                    options,
                    _ozonClientIdBox.Text.Trim(),
                    _ozonApiKeyBox.Text.Trim(),
                    delegate(string line)
                    {
                        AppendAutomationLog(line);
                    });

                if (!result.Success)
                {
                    AppendAutomationLog("Ozon upload failed: " + result.ErrorMessage);
                    SetStatus("Ozon upload failed: " + result.ErrorMessage);
                    return;
                }

                AppendAutomationLog("Ozon upload response: " + result.RawResponse);
                if (!string.IsNullOrEmpty(result.TaskId))
                {
                    AppendAutomationLog("Ozon task id: " + result.TaskId);
                    Clipboard.SetText(result.TaskId);
                    SetStatus("Ozon task submitted; checking import result...");

                    OzonImportResult importResult = await Task.Run(() => _automationService.WaitForOzonImportInfo(
                        result.TaskId,
                        _ozonClientIdBox.Text.Trim(),
                        _ozonApiKeyBox.Text.Trim(),
                        12,
                        10000));

                    AppendAutomationLog("Ozon import result: " + importResult.ImportSummary);
                    importResult = await RetryFailedOzonImportsOnce(selected, options, importResult, _ozonClientIdBox.Text.Trim(), _ozonApiKeyBox.Text.Trim(), delegate(string line)
                    {
                        AppendAutomationLog(line);
                    });
                    if (importResult.AcceptedOfferIds.Count > 0)
                    {
                        if (!importResult.Success)
                        {
                            AppendAutomationLog("Ozon import had partial failures; continuing stock update for accepted offers.");
                        }

                        try
                        {
                            AppendAutomationLog("Waiting for Ozon SKU creation...");
                            OzonSkuWaitResult skuResult = await Task.Run(() => _automationService.WaitForOzonSkuCreation(
                                importResult.AcceptedOfferIds,
                                _ozonClientIdBox.Text.Trim(),
                                _ozonApiKeyBox.Text.Trim(),
                                20,
                                30000));
                            AppendAutomationLog(skuResult.Summary);

                            if (skuResult.ReadyOfferIds.Count == 0)
                            {
                                AppendAutomationLog("Ozon SKU creation is still pending; stock update skipped until SKU exists.");
                            }
                            else
                            {
                            string stockResponse = await Task.Run(() => _automationService.SetOzonStockTo100(
                                skuResult.ReadyOfferIds,
                                _ozonClientIdBox.Text.Trim(),
                                _ozonApiKeyBox.Text.Trim()));
                            AppendAutomationLog("Ozon stock set to 100: " + stockResponse);
                            }
                        }
                        catch (Exception stockEx)
                        {
                            AppendAutomationLog("Ozon stock update failed: " + stockEx.Message);
                        }
                    }

                    if (!importResult.Success)
                    {
                        SetStatus("Ozon import returned errors. See automation log.");
                        return;
                    }
                }

                SetStatus("Ozon import accepted. Products may still need Ozon moderation.");
            }
            catch (Exception ex)
            {
                AppendAutomationLog("OZON ERROR: " + ex.Message);
                MessageBox.Show(this, ex.ToString(), "Ozon upload failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Ozon upload failed: " + ex.Message);
            }
        }

        private async void ListOzonFbsPostings(object sender, EventArgs e)
        {
            if (!EnsureOperationReady("获取FBS订单"))
            {
                return;
            }

            try
            {
                string clientId = _ozonClientIdBox == null ? string.Empty : _ozonClientIdBox.Text.Trim();
                string apiKey = _ozonApiKeyBox == null ? string.Empty : _ozonApiKeyBox.Text.Trim();
                AppendAutomationLog("Listing Ozon FBS postings awaiting delivery...");
                List<OzonFbsPosting> postings = await Task.Run(() => _fulfillmentLabelService.ListFbsPostings(
                    clientId,
                    apiKey,
                    "awaiting_deliver",
                    30,
                    50));

                if (postings.Count == 0)
                {
                    AppendAutomationLog("No awaiting_deliver FBS postings returned.");
                    SetStatus("No FBS postings found.");
                    return;
                }

                StringBuilder postingNumbers = new StringBuilder();
                for (int i = 0; i < postings.Count; i++)
                {
                    OzonFbsPosting posting = postings[i];
                    postingNumbers.AppendLine(posting.PostingNumber);
                    AppendAutomationLog("FBS " + posting.PostingNumber + " status=" + posting.Status + " ship=" + posting.ShipmentDate);
                }

                Clipboard.SetText(postingNumbers.ToString());
                SetStatus("FBS postings copied to clipboard: " + postings.Count);
            }
            catch (Exception ex)
            {
                AppendAutomationLog("Ozon FBS list failed: " + ex.Message);
                MessageBox.Show(this, ex.ToString(), "Ozon FBS list failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Ozon FBS list failed: " + ex.Message);
            }
        }

        private async void DownloadOzonPackageLabels(object sender, EventArgs e)
        {
            if (!EnsureOperationReady("下载面单"))
            {
                return;
            }

            try
            {
                string postingText = ShowMultilinePrompt(
                    "Download Ozon FBS labels",
                    "Paste posting_number values, separated by newline, comma, or space.",
                    Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty);
                if (postingText == null)
                {
                    return;
                }

                List<string> postingNumbers = OzonFulfillmentLabelService.ParsePostingNumbers(postingText);
                if (postingNumbers.Count == 0)
                {
                    SetStatus("No posting_number values entered.");
                    return;
                }

                string clientId = _ozonClientIdBox == null ? string.Empty : _ozonClientIdBox.Text.Trim();
                string apiKey = _ozonApiKeyBox == null ? string.Empty : _ozonApiKeyBox.Text.Trim();
                AppendAutomationLog("Downloading Ozon FBS package labels: " + postingNumbers.Count);
                OzonLabelDownloadResult result = await Task.Run(() => _fulfillmentLabelService.DownloadPackageLabels(
                    postingNumbers,
                    clientId,
                    apiKey,
                    _paths.OzonLabelDirectory));

                for (int i = 0; i < result.Logs.Count; i++)
                {
                    AppendAutomationLog(result.Logs[i]);
                }

                if (result.Files.Count > 0)
                {
                    _paths.OpenPath(Path.GetDirectoryName(result.Files[0]));
                }

                if (!string.IsNullOrEmpty(result.SummaryFile))
                {
                    AppendAutomationLog("Label summary file: " + result.SummaryFile);
                }

                if (!string.IsNullOrEmpty(result.DayIndexFile))
                {
                    AppendAutomationLog("Label daily index: " + result.DayIndexFile);
                }

                UpdateOverview();
                SetStatus("Ozon labels downloaded: " + result.Files.Count);
            }
            catch (Exception ex)
            {
                AppendAutomationLog("Ozon label download failed: " + ex.Message);
                MessageBox.Show(this, ex.ToString(), "Ozon label download failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Ozon label download failed: " + ex.Message);
            }
        }

        private void ExportAutoCandidates(object sender, EventArgs e)
        {
            if (!EnsureOperationReady("导出结果"))
            {
                return;
            }

            if (_lastSourcingResult == null || _lastSourcingResult.Products.Count == 0)
            {
                SetStatus("No candidates to export.");
                return;
            }

            string path = Path.Combine(_paths.WorkRoot, "1688-ozon-candidates.json");
            _automationService.ExportCandidates(path, _lastSourcingResult.Products);
            _paths.OpenPath(path);
            SetStatus("Candidates exported: " + path);
        }

        private SourcingOptions ReadSourcingOptions()
        {
            long categoryId = ParseLong(_autoCategoryIdBox.Text);
            long typeId = ParseLong(_autoTypeIdBox.Text);
            return new SourcingOptions
            {
                Provider = Convert.ToString(_autoProviderBox.SelectedItem),
                ApiKey = _autoApiKeyBox.Text.Trim(),
                ApiSecret = _autoApiSecretBox.Text.Trim(),
                PerKeywordLimit = (int)ParseLong(_autoPerKeywordBox.Text),
                DetailLimit = (int)ParseLong(_autoDetailLimitBox.Text),
                RubPerCny = ParseDecimal(_autoRubRateBox.Text),
                OzonCategoryId = categoryId,
                OzonTypeId = typeId,
                OzonCategoryCandidateIds = BuildOzonCategoryCandidateIds(categoryId, typeId),
                PriceMultiplier = ParseDecimal(_autoPriceMultiplierBox.Text),
                MinOzonPrice = 0m,
                CurrencyCode = "CNY",
                Vat = "0",
                Config = _snapshot == null ? null : _snapshot.Config,
                FeeRules = _snapshot == null ? new List<FeeRule>() : _snapshot.FeeRules,
                FulfillmentMode = _snapshot != null && _snapshot.Config != null && _snapshot.Config.IsFbo ? "FBO" : "FBS"
            };
        }

        private async Task<OzonImportResult> RetryFailedOzonImportsOnce(IList<SourceProduct> products, SourcingOptions options, OzonImportResult importResult, string clientId, string apiKey, Action<string> log)
        {
            if (importResult == null || string.IsNullOrEmpty(importResult.ImportInfoResponse))
            {
                return importResult;
            }

            List<SourceProduct> retryProducts;
            string repairSummary = _automationService.PrepareRetryForFailedImports(products, options, importResult.ImportInfoResponse, clientId, apiKey, out retryProducts);
            if (retryProducts == null || retryProducts.Count == 0)
            {
                return importResult;
            }

            if (log != null)
            {
                log("Ozon auto-repair prepared for failed items:");
                if (!string.IsNullOrEmpty(repairSummary))
                {
                    string[] lines = repairSummary.Replace("\r", string.Empty).Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        log("  " + lines[i]);
                    }
                }
            }

            OzonImportResult retrySubmit = _automationService.UploadToOzon(retryProducts, options, clientId, apiKey, log);
            if (!retrySubmit.Success)
            {
                if (log != null)
                {
                    log("Ozon auto-repair retry submit failed: " + retrySubmit.ErrorMessage);
                }

                return importResult;
            }

            if (log != null)
            {
                log("Ozon auto-repair retry task submitted: " + retryProducts.Count + " items, task_id=" + SafeValue(retrySubmit.TaskId));
            }

            OzonImportResult retryImportResult = await Task.Run(() => _automationService.WaitForOzonImportInfo(
                retrySubmit.TaskId,
                clientId,
                apiKey,
                12,
                10000));

            if (log != null)
            {
                log("Ozon auto-repair retry result:");
                log(retryImportResult.ImportSummary);
            }

            return MergeOzonImportResults(importResult, retryImportResult);
        }

        private static OzonImportResult MergeOzonImportResults(OzonImportResult original, OzonImportResult retry)
        {
            if (original == null)
            {
                return retry;
            }

            if (retry == null)
            {
                return original;
            }

            OzonImportResult merged = new OzonImportResult();
            merged.Success = original.Success || retry.Success;
            merged.TaskId = string.IsNullOrEmpty(retry.TaskId) ? original.TaskId : retry.TaskId;
            merged.RawResponse = string.IsNullOrEmpty(retry.RawResponse) ? original.RawResponse : retry.RawResponse;
            merged.ErrorMessage = string.IsNullOrEmpty(retry.ErrorMessage) ? original.ErrorMessage : retry.ErrorMessage;
            merged.ImportInfoResponse = string.IsNullOrEmpty(retry.ImportInfoResponse) ? original.ImportInfoResponse : retry.ImportInfoResponse;
            merged.ImportSummary = original.ImportSummary;
            if (!string.IsNullOrEmpty(retry.ImportSummary))
            {
                merged.ImportSummary += Environment.NewLine + "Auto-repair retry:" + Environment.NewLine + retry.ImportSummary;
            }

            AppendUniqueOfferIds(merged.AcceptedOfferIds, original.AcceptedOfferIds);
            AppendUniqueOfferIds(merged.AcceptedOfferIds, retry.AcceptedOfferIds);
            return merged;
        }

        private static void AppendUniqueOfferIds(IList<string> target, IList<string> source)
        {
            if (target == null || source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                string offerId = source[i];
                if (string.IsNullOrEmpty(offerId) || target.Contains(offerId))
                {
                    continue;
                }

                target.Add(offerId);
            }
        }

        private List<long> BuildOzonCategoryCandidateIds(long categoryId, long typeId)
        {
            List<long> candidates = new List<long>();
            if (categoryId > 0)
            {
                candidates.Add(categoryId);
            }

            EnsureSnapshot();
            AppendCategoryCandidatesByType(_snapshot == null ? null : _snapshot.Categories, typeId, candidates, new List<long>());
            return candidates;
        }

        private void AppendCategoryCandidatesByType(IList<CategoryNode> nodes, long targetTypeId, IList<long> output, IList<long> ancestors)
        {
            for (int i = 0; nodes != null && i < nodes.Count; i++)
            {
                CategoryNode node = nodes[i];
                if (node == null)
                {
                    continue;
                }

                List<long> nextAncestors = new List<long>(ancestors);
                long nodeCategoryId = ParseLong(node.DescriptionCategoryId);
                if (nodeCategoryId > 0 && !nextAncestors.Contains(nodeCategoryId))
                {
                    nextAncestors.Add(nodeCategoryId);
                }

                if (ParseLong(node.DescriptionTypeId) == targetTypeId)
                {
                    AppendUniqueLong(output, nodeCategoryId);
                    for (int ancestorIndex = nextAncestors.Count - 1; ancestorIndex >= 0; ancestorIndex--)
                    {
                        AppendUniqueLong(output, nextAncestors[ancestorIndex]);
                    }
                }

                AppendCategoryCandidatesByType(node.Children, targetTypeId, output, nextAncestors);
            }
        }

        private static void AppendUniqueLong(IList<long> values, long value)
        {
            if (value <= 0 || values == null)
            {
                return;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == value)
                {
                    return;
                }
            }

            values.Add(value);
        }

        private List<SourcingSeed> ReadSourcingSeeds()
        {
            string text = _autoKeywordsBox == null ? string.Empty : _autoKeywordsBox.Text;
            string[] lines = text.Replace("\r", "\n").Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            List<SourcingSeed> seeds = new List<SourcingSeed>();
            Dictionary<string, bool> unique = new Dictionary<string, bool>();
            for (int i = 0; i < lines.Length; i++)
            {
                string keyword = lines[i].Trim();
                if (string.IsNullOrEmpty(keyword) || unique.ContainsKey(keyword))
                {
                    continue;
                }

                unique[keyword] = true;
                seeds.Add(new SourcingSeed { Keyword = keyword });
            }

            return seeds;
        }

        private List<SourceProduct> GetSelectedSourceProducts()
        {
            List<SourceProduct> products = new List<SourceProduct>();
            if (_autoResultGrid == null)
            {
                return products;
            }

            for (int i = 0; i < _autoResultGrid.SelectedRows.Count; i++)
            {
                SourceProduct product = _autoResultGrid.SelectedRows[i].DataBoundItem as SourceProduct;
                if (product != null && !products.Contains(product))
                {
                    products.Add(product);
                }
            }

            return products;
        }

        private void WriteAutomationLog(IList<string> lines)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; lines != null && i < lines.Count; i++)
            {
                builder.AppendLine(lines[i]);
            }

            _autoLogBox.Text = builder.ToString();
        }

        private void AppendAutomationLog(string line)
        {
            if (_autoLogBox == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(_autoLogBox.Text))
            {
                _autoLogBox.AppendText(Environment.NewLine);
            }

            _autoLogBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + " " + line);
        }

        private string ShowMultilinePrompt(string title, string message, string initialValue)
        {
            Form form = new Form();
            form.Text = title;
            form.Width = 720;
            form.Height = 360;
            form.StartPosition = FormStartPosition.CenterParent;
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.MaximizeBox = false;
            form.MinimizeBox = false;

            Label label = new Label();
            label.Text = message;
            label.Left = 12;
            label.Top = 12;
            label.Width = 680;
            label.Height = 28;

            TextBox text = new TextBox();
            text.Left = 12;
            text.Top = 44;
            text.Width = 680;
            text.Height = 220;
            text.Multiline = true;
            text.ScrollBars = ScrollBars.Vertical;
            text.Text = initialValue ?? string.Empty;

            Button ok = new Button();
            ok.Text = "OK";
            ok.Left = 536;
            ok.Top = 278;
            ok.Width = 75;
            ok.DialogResult = DialogResult.OK;

            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.Left = 617;
            cancel.Top = 278;
            cancel.Width = 75;
            cancel.DialogResult = DialogResult.Cancel;

            form.Controls.Add(label);
            form.Controls.Add(text);
            form.Controls.Add(ok);
            form.Controls.Add(cancel);
            form.AcceptButton = ok;
            form.CancelButton = cancel;

            DialogResult result = form.ShowDialog(this);
            return result == DialogResult.OK ? text.Text.Trim() : null;
        }

        private string WriteExceptionLog(string stage, Exception ex, string reportSnapshot)
        {
            try
            {
                string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(logDirectory);

                string safeStage = string.IsNullOrEmpty(stage) ? "error" : stage.Replace(' ', '-');
                string path = Path.Combine(logDirectory, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + safeStage + ".log");

                StringBuilder builder = new StringBuilder();
                builder.AppendLine("Stage: " + safeStage);
                builder.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                builder.AppendLine();
                builder.AppendLine("Exception");
                builder.AppendLine("---------");
                builder.AppendLine(ex == null ? "(null)" : ex.ToString());

                if (!string.IsNullOrEmpty(reportSnapshot))
                {
                    builder.AppendLine();
                    builder.AppendLine("Report Snapshot");
                    builder.AppendLine("---------------");
                    builder.AppendLine(reportSnapshot);
                }

                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
                return path;
            }
            catch
            {
                return null;
            }
        }

        private void InitializeBrowser(object sender, EventArgs e)
        {
            try
            {
                _browserExtensionReady = false;
                _browser.CoreWebView2InitializationCompleted -= BrowserInitializationCompleted;
                _browser.CoreWebView2InitializationCompleted += BrowserInitializationCompleted;
                CoreWebView2Environment env = BrowserBootstrap.CreateEnvironment(_paths);
                if (env == null)
                {
                    _browserStatusLabel.Text = "未找到 1688 插件目录。";
                    return;
                }

                _browser.EnsureCoreWebView2Async(env);
                _browserStatusLabel.Text = "正在初始化插件浏览器...";
            }
            catch (Exception ex)
            {
                string message = ex.ToString();
                if (message.IndexOf("WebView2", StringComparison.OrdinalIgnoreCase) >= 0 && File.Exists(_paths.WebViewRuntimeInstaller))
                {
                    message += Environment.NewLine + "可尝试运行本地安装包：" + _paths.WebViewRuntimeInstaller;
                }

                _browserStatusLabel.Text = "初始化失败：" + ex.Message;
                MessageBox.Show(this, message, "浏览器初始化失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void BrowserInitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                _browserStatusLabel.Text = "浏览器初始化失败。";
                string message = e.InitializationException == null ? "未知错误。" : e.InitializationException.ToString();
                if (File.Exists(_paths.WebViewRuntimeInstaller))
                {
                    message += Environment.NewLine + "可尝试运行本地安装包：" + _paths.WebViewRuntimeInstaller;
                }

                MessageBox.Show(this, message, "浏览器初始化失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                if (!_browserExtensionReady)
                {
                    await _browser.CoreWebView2.Profile.AddBrowserExtensionAsync(_paths.Plugin1688Folder);
                    _browserExtensionReady = true;
                }

                _browser.ZoomFactor = 0.9d;
                _browserStatusLabel.Text = "浏览器已就绪，1688 插件已挂载，页面缩放 90%。";
                _browser.CoreWebView2.NavigationCompleted -= BrowserNavigationCompleted;
                _browser.CoreWebView2.NavigationCompleted += BrowserNavigationCompleted;
                UpdateSetupStatus();
                if (!string.IsNullOrEmpty(_browserUrlBox.Text))
                {
                    _browser.CoreWebView2.Navigate(_browserUrlBox.Text.Trim());
                }
            }
            catch (Exception ex)
            {
                _browserStatusLabel.Text = "插件挂载失败：" + ex.Message;
                MessageBox.Show(this, ex.ToString(), "插件挂载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void BrowserNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            await HideBrowserPageFloatingChrome();
        }

        private async Task HideBrowserPageFloatingChrome()
        {
            try
            {
                if (_browser == null || _browser.CoreWebView2 == null)
                {
                    return;
                }

                string script = @"(function(){
  var id='ozon-pilot-page-chrome-style';
  if(!document.getElementById(id)){
    var style=document.createElement('style');
    style.id=id;
    style.textContent='[style*=""position: fixed""][style*=""rgb(51, 51, 51)""], [style*=""position:fixed""][style*=""rgb(51, 51, 51)""]{display:none!important;}';
    document.documentElement.appendChild(style);
  }
  var nodes=document.querySelectorAll('body *');
  for(var i=0;i<nodes.length;i++){
    var el=nodes[i], cs=getComputedStyle(el), r=el.getBoundingClientRect();
    if(cs.position==='fixed' && r.left<260 && r.top>window.innerHeight-210){
      var bg=cs.backgroundColor || '';
      if(bg.indexOf('rgb(51, 51, 51)')>=0 || bg.indexOf('rgb(0, 0, 0)')>=0 || bg.indexOf('rgba(0, 0, 0')>=0){
        el.style.display='none';
      }
    }
  }
})();";
                await _browser.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch
            {
                // Cosmetic browser-page cleanup must never block automation.
            }
        }

        private void NavigateBrowser(object sender, EventArgs e)
        {
            try
            {
                if (_browser.CoreWebView2 == null)
                {
                    _browserStatusLabel.Text = "请先初始化插件浏览器。";
                    return;
                }

                _browser.CoreWebView2.Navigate(_browserUrlBox.Text.Trim());
                _browser.ZoomFactor = 0.9d;
                _browserStatusLabel.Text = "正在打开：" + _browserUrlBox.Text.Trim();
                BeginInvoke(new MethodInvoker(async delegate { await HideBrowserPageFloatingChrome(); }));
                UpdateSetupStatus();
            }
            catch (Exception ex)
            {
                _browserStatusLabel.Text = ex.Message;
            }
        }

        private void OpenOzonApiPage(object sender, EventArgs e)
        {
            if (_browserUrlBox != null)
            {
                _browserUrlBox.Text = "https://seller.ozon.ru/app/settings/api-keys";
            }

            if (_browser == null || _browser.CoreWebView2 == null)
            {
                SetStatus("请先初始化浏览器，再打开 Ozon API 页面。");
                if (_browserStatusLabel != null)
                {
                    _browserStatusLabel.Text = "请先初始化浏览器，再打开 Ozon API 页面。";
                }
                return;
            }

            NavigateBrowser(sender, e);
            SetStatus("已打开 Ozon Seller API Keys 页面。拿到 Client-Id 和 Api-Key 后填到上方。");
        }

        private void Open1688LoginPage(object sender, EventArgs e)
        {
            if (_browserUrlBox != null)
            {
                _browserUrlBox.Text = "https://www.1688.com/";
            }

            if (_browser == null || _browser.CoreWebView2 == null)
            {
                SetStatus("请先初始化浏览器，再打开 1688。");
                return;
            }

            NavigateBrowser(sender, e);
            SetStatus("已打开 1688，请完成登录后点击“检测登录”。");
        }

        private async void Check1688Login(object sender, EventArgs e)
        {
            try
            {
                if (_browser == null || _browser.CoreWebView2 == null)
                {
                    SetStatus("请先初始化 1688 浏览器。");
                    return;
                }

                string script = "(function(){var text=(document.body&&document.body.innerText)||'';var href=location.href;var logged=/我的阿里|买家中心|卖家中心|消息|已登录|退出/.test(text);var login=/请登录|登录\\/注册|免费注册/.test(text);return JSON.stringify({href:href,logged:logged,login:login,text:text.substring(0,800)});})()";
                string raw = await _browser.CoreWebView2.ExecuteScriptAsync(script);
                string json = JsonConvert.DeserializeObject<string>(raw);
                JObject result = JObject.Parse(json);
                string href = Convert.ToString(result["href"] ?? string.Empty);
                bool on1688 = href.IndexOf("1688.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    href.IndexOf("alibaba.com", StringComparison.OrdinalIgnoreCase) >= 0;
                bool logged = Convert.ToBoolean(result["logged"] ?? false);
                bool login = Convert.ToBoolean(result["login"] ?? false);
                _1688LoginVerified = on1688 && logged && !login;
                UpdateSetupStatus();
                UpdateOperationReadiness();
                if (_1688LoginVerified)
                {
                    SetStatus("1688 登录检测通过。");
                }
                else if (!on1688)
                {
                    SetStatus("当前浏览器不在 1688 页面，请先点“打开1688”。");
                    if (_setup1688StatusLabel != null) _setup1688StatusLabel.Text = "1688：当前不在 1688 页面";
                }
                else if (login)
                {
                    SetStatus("页面仍显示登录入口，请先完成 1688 登录。");
                    if (_setup1688StatusLabel != null) _setup1688StatusLabel.Text = "1688：页面仍显示登录入口";
                }
                else
                {
                    SetStatus("未确认 1688 已登录，请刷新或重新登录后再检测。");
                    if (_setup1688StatusLabel != null) _setup1688StatusLabel.Text = "1688：未确认登录";
                }
            }
            catch (Exception ex)
            {
                _1688LoginVerified = false;
                UpdateSetupStatus();
                UpdateOperationReadiness();
                string message = "1688 登录检测失败：" + ex.Message;
                if (_setup1688StatusLabel != null) _setup1688StatusLabel.Text = message;
                SetStatus(message);
            }
        }

        private async void CheckOzonCredentials(object sender, EventArgs e)
        {
            try
            {
                string clientId = _ozonClientIdBox == null ? string.Empty : _ozonClientIdBox.Text.Trim();
                string apiKey = _ozonApiKeyBox == null ? string.Empty : _ozonApiKeyBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(apiKey))
                {
                    _ozonCredentialsVerified = false;
                    UpdateSetupStatus();
                    UpdateOperationReadiness();
                    SetStatus("请先填写 Ozon Client-Id 和 Api-Key。");
                    return;
                }

                SetStatus("正在检测 Ozon API...");
                string result = await Task.Run(() => _fulfillmentLabelService.CheckCredentials(clientId, apiKey));
                _ozonCredentialsVerified = true;
                SavePersistentUiState();
                UpdateSetupStatus();
                UpdateOperationReadiness();
                SetStatus("Ozon API 检测通过。" + result);
            }
            catch (Exception ex)
            {
                _ozonCredentialsVerified = false;
                UpdateSetupStatus();
                UpdateOperationReadiness();
                string message = "Ozon API 检测失败：" + ex.Message;
                if (_setupOzonStatusLabel != null) _setupOzonStatusLabel.Text = message;
                SetStatus(message);
            }
        }

        private void EnsureSnapshot()
        {
            if (_snapshot == null)
            {
                _snapshot = new AssetSnapshot();
            }
        }

        private void ClearAssetViews()
        {
            EnsureSnapshot();
            _snapshot.Categories = new List<CategoryNode>();
            _snapshot.FeeRules = new List<FeeRule>();
            _categoryTree.Nodes.Clear();
            _feeGrid.DataSource = null;
        }

        private FeeRule GetSelectedFeeRule()
        {
            if (_feeGrid.CurrentRow == null)
            {
                return null;
            }

            FeeRuleDisplayRow display = _feeGrid.CurrentRow.DataBoundItem as FeeRuleDisplayRow;
            if (display != null)
            {
                return display.Rule;
            }

            return _feeGrid.CurrentRow.DataBoundItem as FeeRule;
        }

        private TreeNode BuildTreeNode(CategoryNode source)
        {
            string text = BuildBilingualCategoryName(source.DescriptionCategoryName);
            if (!string.IsNullOrEmpty(source.DescriptionTypeName))
            {
                text += " / " + BuildBilingualCategoryName(source.DescriptionTypeName);
            }

            if (!string.IsNullOrEmpty(source.DescriptionCategoryId))
            {
                text += " [" + source.DescriptionCategoryId + "]";
            }

            TreeNode node = new TreeNode(text);
            node.Tag = source;
            int i;
            for (i = 0; i < source.Children.Count; i++)
            {
                node.Nodes.Add(BuildTreeNode(source.Children[i]));
            }

            return node;
        }

        private TabPage CreateTabPage(string title)
        {
            TabPage tab = new TabPage(title);
            tab.BackColor = ShellBack;
            return tab;
        }

        private FlowLayoutPanel CreateActionBar()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Top;
            panel.Height = 68;
            panel.WrapContents = false;
            panel.AutoScroll = true;
            panel.Padding = new Padding(26, 14, 16, 0);
            panel.BackColor = ShellBack;
            return panel;
        }

        private Control CreateStatCard(string title, string value, string description, Color accent, out Label valueLabel)
        {
            RoundedPanel card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(10);
            card.FillColor = CardBack;
            card.BorderColor = LineWarm;
            card.ShadowColor = Color.FromArgb(13, 152, 91, 42);
            card.Radius = 18;
            card.Padding = new Padding(16, 14, 16, 14);

            Panel line = new Panel();
            line.BackColor = accent;
            line.Width = 1;
            line.Dock = DockStyle.Left;
            line.Margin = new Padding(0, 12, 0, 12);

            Label titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.AutoSize = true;
            titleLabel.ForeColor = TextMuted;
            titleLabel.Location = new Point(18, 14);

            valueLabel = new Label();
            valueLabel.Text = value;
            valueLabel.AutoSize = true;
            valueLabel.Font = new Font("Segoe UI", 23F, FontStyle.Bold, GraphicsUnit.Point, 0);
            valueLabel.ForeColor = TextStrong;
            valueLabel.Location = new Point(18, 36);

            Label descriptionLabel = new Label();
            descriptionLabel.Text = description;
            descriptionLabel.AutoSize = false;
            descriptionLabel.Width = 240;
            descriptionLabel.Height = 36;
            descriptionLabel.ForeColor = TextMuted;
            descriptionLabel.Location = new Point(18, 78);

            card.Controls.Add(descriptionLabel);
            card.Controls.Add(valueLabel);
            card.Controls.Add(titleLabel);
            card.Controls.Add(line);
            return card;
        }

        private static Control WrapWithGroup(string title, Control inner)
        {
            RoundedPanel group = new RoundedPanel();
            group.Dock = DockStyle.Fill;
            group.Padding = new Padding(14, 40, 14, 14);
            group.FillColor = CardBack;
            group.BorderColor = LineWarm;
            group.ShadowColor = Color.FromArgb(13, 152, 91, 42);
            group.Radius = 16;

            Label titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.AutoSize = true;
            titleLabel.Left = 16;
            titleLabel.Top = 12;
            titleLabel.ForeColor = TextStrong;
            titleLabel.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point, 0);

            Panel content = new Panel();
            content.Dock = DockStyle.Fill;
            content.BackColor = Color.FromArgb(255, 253, 249);
            inner.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            inner.Dock = DockStyle.Fill;
            content.Controls.Add(inner);
            group.Controls.Add(content);
            group.Controls.Add(titleLabel);
            return group;
        }

        private DataGridView CreateGrid()
        {
            DataGridView grid = new DataGridView();
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
            grid.BackgroundColor = Color.FromArgb(255, 253, 249);
            grid.BorderStyle = BorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.RowTemplate.Height = 34;
            grid.GridColor = Color.FromArgb(238, 229, 219);
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(250, 244, 238);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextStrong;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 0);
            grid.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            grid.DefaultCellStyle.BackColor = Color.FromArgb(255, 253, 249);
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(252, 248, 242);
            grid.DefaultCellStyle.SelectionBackColor = PilotGreenSoft;
            grid.DefaultCellStyle.SelectionForeColor = TextStrong;
            return grid;
        }

        private Button CreateButton(string text, EventHandler handler, bool primary)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = false;
            button.Height = 36;
            button.Width = Math.Max(76, Math.Min(180, text.Length * 12 + 34));
            button.Margin = new Padding(3, 1, 4, 7);
            button.Padding = new Padding(8, 4, 8, 4);
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.BorderSize = 1;
            button.Cursor = Cursors.Hand;
            Color normalBack = primary ? PilotGreen : Color.FromArgb(255, 250, 244);
            Color hoverBack = primary ? PilotGreenDark : Color.FromArgb(255, 240, 225);
            Color downBack = primary ? Color.FromArgb(145, 52, 0) : Color.FromArgb(246, 226, 207);
            Color normalBorder = primary ? PilotGreen : Color.FromArgb(230, 211, 195);
            Color hoverBorder = primary ? PilotGreenDark : Color.FromArgb(220, 174, 136);
            button.FlatAppearance.BorderColor = normalBorder;
            button.BackColor = normalBack;
            button.ForeColor = primary ? Color.White : TextStrong;
            button.FlatAppearance.MouseOverBackColor = hoverBack;
            button.FlatAppearance.MouseDownBackColor = downBack;
            button.Font = new Font("Microsoft YaHei UI", 9.1F, FontStyle.Bold, GraphicsUnit.Point, 0);
            Padding normalMargin = button.Margin;
            Padding pressedMargin = new Padding(normalMargin.Left, normalMargin.Top + 2, normalMargin.Right, Math.Max(0, normalMargin.Bottom - 2));
            button.MouseEnter += delegate
            {
                button.BackColor = hoverBack;
                button.FlatAppearance.BorderColor = hoverBorder;
            };
            button.MouseLeave += delegate
            {
                button.BackColor = normalBack;
                button.FlatAppearance.BorderColor = normalBorder;
                button.Margin = normalMargin;
            };
            button.MouseDown += delegate
            {
                button.BackColor = downBack;
                button.Margin = pressedMargin;
            };
            button.MouseUp += delegate
            {
                button.BackColor = hoverBack;
                button.Margin = normalMargin;
            };
            button.Resize += delegate { SetRoundedRegion(button, 14); };
            SetRoundedRegion(button, 14);
            button.Click += handler;
            return button;
        }

        private Label CreateCommandButton(string text, EventHandler handler, bool primary)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Height = 36;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Margin = new Padding(0, 0, 8, 0);
            label.Padding = new Padding(8, 4, 8, 4);
            label.Cursor = Cursors.Hand;
            label.Font = new Font("Microsoft YaHei UI", 9.3F, FontStyle.Bold, GraphicsUnit.Point, 0);
            Color normalBack = primary ? PilotGreen : Color.FromArgb(255, 246, 238);
            Color hoverBack = primary ? PilotGreenDark : Color.FromArgb(255, 234, 215);
            label.BackColor = normalBack;
            label.ForeColor = primary ? Color.White : TextStrong;
            label.Resize += delegate { SetRoundedRegion(label, 14); };
            label.MouseEnter += delegate { label.BackColor = hoverBack; };
            label.MouseLeave += delegate { label.BackColor = normalBack; };
            label.MouseDown += delegate { label.Padding = new Padding(8, 6, 8, 2); };
            label.MouseUp += delegate { label.Padding = new Padding(8, 4, 8, 4); };
            label.Click += handler;
            SetRoundedRegion(label, 14);
            return label;
        }

        private Label CreateFormLabel(string text, int left, int top, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.Left = left;
            label.Top = top + 6;
            label.Width = width;
            label.ForeColor = TextMuted;
            label.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 0);
            return label;
        }

        private void AddParameterRow(TableLayoutPanel grid, int row, string labelText, TextBox input)
        {
            Label label = CreateFormLabel(labelText, 0, 0, 58);
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            input.Dock = DockStyle.Fill;
            input.Margin = new Padding(0, 2, 0, 4);
            grid.Controls.Add(label, 0, row);
            grid.Controls.Add(input, 1, row);
        }

        private Label CreateSectionLabel(string text, int left, int top)
        {
            Label label = new Label();
            label.Text = text;
            label.Left = left;
            label.Top = top;
            label.Width = 360;
            label.Height = 24;
            label.ForeColor = TextStrong;
            label.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point, 0);
            return label;
        }

        private Label CreateSetupStepLabel(string number, string title, string detail, int left, int top, Color accent)
        {
            Label label = new Label();
            label.Text = number + ". " + title + Environment.NewLine + detail;
            label.Left = left;
            label.Top = top;
            label.Width = 360;
            label.Height = 68;
            label.ForeColor = accent;
            label.BackColor = Color.FromArgb(255, 245, 236);
            label.BorderStyle = BorderStyle.None;
            label.Padding = new Padding(14, 9, 10, 6);
            label.Font = new Font("Microsoft YaHei UI", 9.3F, FontStyle.Bold, GraphicsUnit.Point, 0);
            label.Resize += delegate { SetRoundedRegion(label, 10); };
            SetRoundedRegion(label, 10);
            return label;
        }

        private Label CreateSetupStatusLabel(string text, int left, int top, Color accent)
        {
            Label label = new Label();
            label.Text = text;
            label.Left = left;
            label.Top = top;
            label.Width = 340;
            label.Height = 28;
            label.ForeColor = accent;
            label.BackColor = Color.FromArgb(255, 245, 236);
            label.BorderStyle = BorderStyle.None;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold, GraphicsUnit.Point, 0);
            label.Resize += delegate { SetRoundedRegion(label, 10); };
            SetRoundedRegion(label, 10);
            return label;
        }

        private TextBox CreateTextBox(int left, int top, int width, string value)
        {
            TextBox textBox = new TextBox();
            textBox.Left = left;
            textBox.Top = top;
            textBox.Width = width;
            textBox.Text = value;
            textBox.BorderStyle = BorderStyle.FixedSingle;
            textBox.BackColor = Color.FromArgb(255, 252, 248);
            textBox.ForeColor = TextStrong;
            textBox.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Regular, GraphicsUnit.Point, 0);
            textBox.Height = 32;
            return textBox;
        }

        private static void SetRoundedRegion(Control control, int radius)
        {
            if (control.Width <= 0 || control.Height <= 0)
            {
                return;
            }

            if (control.Region != null)
            {
                control.Region.Dispose();
            }

            control.Region = new Region(CreateRoundPath(new Rectangle(0, 0, control.Width, control.Height), radius));
        }

        private static GraphicsPath CreateRoundPath(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(1, radius * 2);
            Rectangle arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        private decimal ParseDecimal(string text)
        {
            decimal value;
            return decimal.TryParse(text, out value) ? value : 0m;
        }

        private long ParseLong(string text)
        {
            long value;
            return long.TryParse(text, out value) ? value : 0L;
        }

        private void LoadUiLanguagePreference()
        {
            try
            {
                AppConfig config = ConfigService.Load(_paths.ConfigFile);
                _uiLanguage = NormalizeUiLanguage(config == null ? null : config.UiLanguage);
            }
            catch
            {
                _uiLanguage = "zh";
            }
        }

        private static string NormalizeUiLanguage(string language)
        {
            string text = Convert.ToString(language ?? string.Empty).Trim().ToLowerInvariant();
            if (text == "en" || text == "ru")
            {
                return text;
            }

            return "zh";
        }

        private int LanguageIndexFromCode(string language)
        {
            switch (NormalizeUiLanguage(language))
            {
                case "en":
                    return 1;
                case "ru":
                    return 2;
                default:
                    return 0;
            }
        }

        private string LanguageCodeFromIndex(int index)
        {
            switch (index)
            {
                case 1:
                    return "en";
                case 2:
                    return "ru";
                default:
                    return "zh";
            }
        }

        private Dictionary<string, string> CaptureUiState()
        {
            Dictionary<string, string> state = new Dictionary<string, string>();
            state["tab"] = GetSelectedTabKey();
            state["keywords"] = _autoKeywordsBox == null ? string.Empty : _autoKeywordsBox.Text;
            state["categoryId"] = _autoCategoryIdBox == null ? string.Empty : _autoCategoryIdBox.Text;
            state["typeId"] = _autoTypeIdBox == null ? string.Empty : _autoTypeIdBox.Text;
            state["clientId"] = _ozonClientIdBox == null ? string.Empty : _ozonClientIdBox.Text;
            state["apiKey"] = _ozonApiKeyBox == null ? string.Empty : _ozonApiKeyBox.Text;
            state["loop"] = _autoLoopCountBox == null ? "1" : _autoLoopCountBox.Text;
            state["browserUrl"] = _browserUrlBox == null ? string.Empty : _browserUrlBox.Text;
            return state;
        }

        private void SavePersistentUiState()
        {
            if (_restoringPersistentState || _paths == null)
            {
                return;
            }

            try
            {
                Dictionary<string, string> state = CaptureUiState();
                JObject root = new JObject();
                root["tab"] = SafeStateValue(state, "tab");
                root["keywords"] = SafeStateValue(state, "keywords");
                root["categoryId"] = SafeStateValue(state, "categoryId");
                root["typeId"] = SafeStateValue(state, "typeId");
                root["clientId"] = SafeStateValue(state, "clientId");
                root["apiKeyProtected"] = ProtectLocalSecret(SafeStateValue(state, "apiKey"));
                root["loop"] = SafeStateValue(state, "loop");
                root["browserUrl"] = SafeStateValue(state, "browserUrl");
                File.WriteAllText(_paths.UiStateFile, root.ToString(), new UTF8Encoding(false));
            }
            catch
            {
                // UI state is convenience data; failed persistence must not block operations.
            }
        }

        private void RestorePersistentUiState()
        {
            if (_paths == null || !File.Exists(_paths.UiStateFile))
            {
                return;
            }

            try
            {
                _restoringPersistentState = true;
                JObject root = JObject.Parse(File.ReadAllText(_paths.UiStateFile, Encoding.UTF8));
                Dictionary<string, string> state = new Dictionary<string, string>();
                state["tab"] = ReadStateValue(root, "tab");
                state["keywords"] = ReadStateValue(root, "keywords");
                state["categoryId"] = ReadStateValue(root, "categoryId");
                state["typeId"] = ReadStateValue(root, "typeId");
                state["clientId"] = ReadStateValue(root, "clientId");
                state["apiKey"] = UnprotectLocalSecret(ReadStateValue(root, "apiKeyProtected"));
                state["loop"] = ReadStateValue(root, "loop");
                state["browserUrl"] = ReadStateValue(root, "browserUrl");
                RestoreUiState(state);
            }
            catch
            {
            }
            finally
            {
                _restoringPersistentState = false;
            }
        }

        private static string SafeStateValue(Dictionary<string, string> state, string key)
        {
            return state != null && state.ContainsKey(key) ? state[key] ?? string.Empty : string.Empty;
        }

        private static string ReadStateValue(JObject root, string key)
        {
            JToken token = root == null ? null : root[key];
            return token == null ? string.Empty : token.ToString();
        }

        private static string ProtectLocalSecret(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            try
            {
                byte[] plain = Encoding.UTF8.GetBytes(value);
                byte[] protectedBytes = ProtectedData.Protect(plain, Encoding.UTF8.GetBytes("OZON-PILOT"), DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(protectedBytes);
            }
            catch
            {
                return "plain:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
            }
        }

        private static string UnprotectLocalSecret(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            try
            {
                if (value.StartsWith("plain:", StringComparison.Ordinal))
                {
                    return Encoding.UTF8.GetString(Convert.FromBase64String(value.Substring(6)));
                }

                byte[] protectedBytes = Convert.FromBase64String(value);
                byte[] plain = ProtectedData.Unprotect(protectedBytes, Encoding.UTF8.GetBytes("OZON-PILOT"), DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return string.Empty;
            }
        }

        private void RestoreUiState(Dictionary<string, string> state)
        {
            if (state == null)
            {
                return;
            }

            if (_autoKeywordsBox != null && state.ContainsKey("keywords"))
            {
                _autoKeywordsBox.Text = state["keywords"];
            }

            if (_autoCategoryIdBox != null && state.ContainsKey("categoryId"))
            {
                _autoCategoryIdBox.Text = state["categoryId"];
            }

            if (_autoTypeIdBox != null && state.ContainsKey("typeId"))
            {
                _autoTypeIdBox.Text = state["typeId"];
            }

            if (_ozonClientIdBox != null && state.ContainsKey("clientId") && !string.IsNullOrWhiteSpace(state["clientId"]))
            {
                _ozonClientIdBox.Text = state["clientId"];
            }

            if (_ozonApiKeyBox != null && state.ContainsKey("apiKey") && !string.IsNullOrWhiteSpace(state["apiKey"]))
            {
                _ozonApiKeyBox.Text = state["apiKey"];
            }

            if (_autoLoopCountBox != null && state.ContainsKey("loop"))
            {
                _autoLoopCountBox.Text = state["loop"];
            }

            if (_browserUrlBox != null && state.ContainsKey("browserUrl") && !string.IsNullOrWhiteSpace(state["browserUrl"]))
            {
                _browserUrlBox.Text = state["browserUrl"];
            }

            if (_languageComboBox != null)
            {
                _languageComboBox.SelectedIndex = LanguageIndexFromCode(_uiLanguage);
            }

            RestoreSelectedTab(SafeStateValue(state, "tab"));
        }

        private string GetSelectedTabKey()
        {
            if (_mainTabs == null)
            {
                return "setup";
            }

            if (_mainTabs.SelectedTab == _operationTab) return "operation";
            if (_mainTabs.SelectedTab == _overviewTab) return "overview";
            if (_mainTabs.SelectedTab == _assetsTab) return "assets";
            if (_mainTabs.SelectedTab == _configTab) return "config";
            if (_mainTabs.SelectedTab == _languageTab) return "language";
            return "setup";
        }

        private void RestoreSelectedTab(string tabKey)
        {
            if (_mainTabs == null)
            {
                return;
            }

            string normalized = string.IsNullOrWhiteSpace(tabKey) ? "setup" : tabKey.Trim().ToLowerInvariant();
            if (normalized == "1" || normalized == "operation")
            {
                if (_1688LoginVerified && _ozonCredentialsVerified)
                {
                    SelectOperationTab();
                }
                else
                {
                    SelectSetupTab();
                }
            }
            else if (normalized == "2" || normalized == "overview")
            {
                SelectOverviewTab();
            }
            else if (normalized == "3" || normalized == "assets")
            {
                _mainTabs.SelectedTab = _assetsTab;
            }
            else if (normalized == "4" || normalized == "config")
            {
                _mainTabs.SelectedTab = _configTab;
            }
            else if (normalized == "5" || normalized == "language")
            {
                _mainTabs.SelectedTab = _languageTab;
            }
            else
            {
                SelectSetupTab();
            }
        }

        private void ApplyLanguageSelection(object sender, EventArgs e)
        {
            string selected = LanguageCodeFromIndex(_languageComboBox == null ? 0 : _languageComboBox.SelectedIndex);
            if (selected == _uiLanguage)
            {
                SetStatus(T("languageNoChange"));
                return;
            }

            Dictionary<string, string> state = CaptureUiState();
            _uiLanguage = selected;

            AppConfig config = _snapshot == null ? ConfigService.Load(_paths.ConfigFile) : _snapshot.Config;
            if (config == null)
            {
                config = AppConfig.CreateDefault();
            }

            config.UiLanguage = _uiLanguage;
            ConfigService.Save(_paths.ConfigFile, config);

            if (_browser != null)
            {
                try
                {
                    _browser.Dispose();
                }
                catch
                {
                }
            }

            SuspendLayout();
            Controls.Clear();
            InitializeControls();
            LoadAll();
            ApplyOzonSellerDefaults();
            RestoreUiState(state);
            ResumeLayout();
            PerformLayout();
            SetStatus(T("languageApplied"));
        }

        private string T(string key)
        {
            switch (NormalizeUiLanguage(_uiLanguage))
            {
                case "en":
                    switch (key)
                    {
                        case "subtitle": return "1688 sourcing to Ozon upload workspace";
                        case "overview": return "Overview";
                        case "config": return "Config Center";
                        case "assets": return "Categories & Rules";
                        case "language": return "Language";
                        case "browser": return "Plugin Browser";
                        case "reload": return "Reload";
                        case "openBase": return "Open Assets";
                        case "openPlugin": return "Open 1688 Plugin";
                        case "runUpdater": return "Run Updater";
                        case "brake": return "Emergency Stop";
                        case "loopCount": return "Loop count";
                        case "fullAuto": return "Full Auto Loop";
                        case "categoryNodes": return "Category Nodes";
                        case "categoryNodesDesc": return "Total nodes loaded from category.txt";
                        case "feeRules": return "Fee Rules";
                        case "feeRulesDesc": return "Total rules loaded from fee.txt";
                        case "pluginFiles": return "Plugin Files";
                        case "pluginFilesDesc": return "Files detected in the 1688 plugin folder";
                        case "overviewDetails": return "Workspace Summary";
                        case "resourcePaths": return "Resource Paths";
                        case "workRoot": return "Workspace: ";
                        case "baselineRoot": return "Baseline: ";
                        case "configFile": return "Config file: ";
                        case "plugin1688": return "1688 plugin: ";
                        case "updaterPath": return "Updater: ";
                        case "recoveryStatus": return "Recovery Status";
                        case "categoryCountLine": return "Category nodes: ";
                        case "feeCountLine": return "Fee rules: ";
                        case "pluginCountLine": return "Plugin files: ";
                        case "assetLoadFailed": return "Category/rule status: load failed";
                        case "failureReason": return "Reason: ";
                        case "currentFilters": return "Current Filters";
                        case "saveDir": return "Save directory: ";
                        case "priceRange": return "Price range: ";
                        case "minProfit": return "Minimum profit: ";
                        case "defaultShipping": return "Default shipping: ";
                        case "enable1688": return "Enable 1688: ";
                        case "autoExport": return "Auto export: ";
                        case "cloudFilter": return "Cloud filter: ";
                        case "yes": return "Yes";
                        case "no": return "No";
                        case "quickStart": return "Quick Start";
                        case "quickGuide": return "1. Open Plugin Browser and sign in to 1688.\r\n2. Double-click a second-level category in Categories & Rules.\r\n3. Run Auto Sourcing. The browser will search 1688 with the Chinese keyword.\r\n4. Upload to Ozon and watch the SKU and stock brief on Overview.\r\n5. Keywords always stay in Chinese and are not translated.";
                        case "reloadConfig": return "Reload Config";
                        case "saveConfig": return "Save Config";
                        case "initBrowser": return "Initialize Browser";
                        case "openUrl": return "Open URL";
                        case "filterConfig": return "Filter Config";
                        case "reloadAssets": return "Reload Assets";
                        case "exportFeeRules": return "Export Fee Rules";
                        case "useRuleForAuto": return "Send Rule to Auto";
                        case "searchAssets": return "Search categories/rules";
                        case "categoryTree": return "Category Tree";
                        case "feeRuleTable": return "Fee Rule Table";
                        case "languageTitle": return "Interface Language";
                        case "languageDesc": return "Switch the interface language here. The page text updates immediately after applying the new language.";
                        case "languageCurrent": return "Current language";
                        case "applyLanguage": return "Apply Language";
                        case "languageNote": return "Keywords in Auto Sourcing always stay Chinese. This switch only changes the UI language.";
                        case "languageApplied": return "Interface language updated.";
                        case "languageNoChange": return "Interface language is already active.";
                        case "ruleAppliedToAuto": return "Selected rule was sent to Auto Sourcing.";
                        default: return key;
                    }
                case "ru":
                    switch (key)
                    {
                        case "subtitle": return "Рабочее место для отбора 1688 и загрузки в Ozon";
                        case "overview": return "Обзор";
                        case "config": return "Настройки";
                        case "assets": return "Категории и правила";
                        case "language": return "Язык";
                        case "browser": return "Браузер плагина";
                        case "reload": return "Перезагрузить";
                        case "openBase": return "Открыть ресурсы";
                        case "openPlugin": return "Открыть плагин 1688";
                        case "runUpdater": return "Запустить обновление";
                        case "brake": return "Стоп";
                        case "loopCount": return "Циклов";
                        case "fullAuto": return "Полный автоцикл";
                        case "categoryNodes": return "Категории";
                        case "categoryNodesDesc": return "Всего узлов из category.txt";
                        case "feeRules": return "Правила доставки";
                        case "feeRulesDesc": return "Всего правил из fee.txt";
                        case "pluginFiles": return "Файлы плагина";
                        case "pluginFilesDesc": return "Файлы в папке плагина 1688";
                        case "overviewDetails": return "Сводка рабочего места";
                        case "resourcePaths": return "Пути ресурсов";
                        case "workRoot": return "Рабочая папка: ";
                        case "baselineRoot": return "Базовая папка: ";
                        case "configFile": return "Файл настроек: ";
                        case "plugin1688": return "Плагин 1688: ";
                        case "updaterPath": return "Обновление: ";
                        case "recoveryStatus": return "Статус восстановления";
                        case "categoryCountLine": return "Категории: ";
                        case "feeCountLine": return "Правила доставки: ";
                        case "pluginCountLine": return "Файлы плагина: ";
                        case "assetLoadFailed": return "Статус категорий/правил: ошибка загрузки";
                        case "failureReason": return "Причина: ";
                        case "currentFilters": return "Текущие фильтры";
                        case "saveDir": return "Папка сохранения: ";
                        case "priceRange": return "Диапазон цены: ";
                        case "minProfit": return "Минимальная прибыль: ";
                        case "defaultShipping": return "Доставка по умолчанию: ";
                        case "enable1688": return "1688 включен: ";
                        case "autoExport": return "Автоэкспорт: ";
                        case "cloudFilter": return "Облачный фильтр: ";
                        case "yes": return "Да";
                        case "no": return "Нет";
                        case "quickStart": return "Быстрый старт";
                        case "quickGuide": return "1. Откройте браузер плагина и войдите в 1688.\r\n2. Дважды щёлкните подкатегорию в разделе категорий.\r\n3. Запустите Auto Sourcing. Браузер будет искать по китайскому ключевому слову.\r\n4. Загрузите товары в Ozon и следите за брифом SKU и остатков на обзоре.\r\n5. Ключевые слова в Auto Sourcing всегда остаются китайскими.";
                        case "reloadConfig": return "Обновить настройки";
                        case "saveConfig": return "Сохранить настройки";
                        case "initBrowser": return "Запустить браузер";
                        case "openUrl": return "Открыть сайт";
                        case "filterConfig": return "Параметры фильтра";
                        case "reloadAssets": return "Обновить ресурсы";
                        case "exportFeeRules": return "Экспорт правил";
                        case "useRuleForAuto": return "Передать правило в Auto";
                        case "searchAssets": return "Поиск категорий/правил";
                        case "categoryTree": return "Дерево категорий";
                        case "feeRuleTable": return "Таблица правил";
                        case "languageTitle": return "Язык интерфейса";
                        case "languageDesc": return "Здесь можно переключать язык интерфейса. После применения текст страницы обновляется сразу.";
                        case "languageCurrent": return "Текущий язык";
                        case "applyLanguage": return "Применить язык";
                        case "languageNote": return "Ключевые слова в Auto Sourcing всегда остаются на китайском. Переключатель меняет только интерфейс.";
                        case "languageApplied": return "Язык интерфейса обновлён.";
                        case "languageNoChange": return "Этот язык уже активен.";
                        case "ruleAppliedToAuto": return "Выбранное правило передано в Auto Sourcing.";
                        default: return key;
                    }
                default:
                    switch (key)
                    {
                        case "subtitle": return "1688 自动选品到 Ozon 上架工作台";
                        case "overview": return "总览";
                        case "config": return "配置中心";
                        case "assets": return "类目与规则";
                        case "language": return "语言切换";
                        case "browser": return "插件浏览器";
                        case "reload": return "重新载入全部";
                        case "openBase": return "打开基础资源";
                        case "openPlugin": return "打开 1688 插件";
                        case "runUpdater": return "运行更新器";
                        case "brake": return "紧急刹车";
                        case "loopCount": return "全自动循环次数";
                        case "fullAuto": return "全链路自动循环";
                        case "categoryNodes": return "类目节点";
                        case "categoryNodesDesc": return "从 category.txt 读取到的类目树节点总数";
                        case "feeRules": return "运费规则";
                        case "feeRulesDesc": return "从 fee.txt 读取到的运费规则总数";
                        case "pluginFiles": return "插件文件";
                        case "pluginFilesDesc": return "1688 插件目录中的文件总数";
                        case "overviewDetails": return "恢复资产明细";
                        case "resourcePaths": return "资源路径";
                        case "workRoot": return "工作区目录：";
                        case "baselineRoot": return "基础目录：";
                        case "configFile": return "配置文件：";
                        case "plugin1688": return "1688 插件：";
                        case "updaterPath": return "更新器程序：";
                        case "recoveryStatus": return "恢复状态";
                        case "categoryCountLine": return "类目节点：";
                        case "feeCountLine": return "运费规则：";
                        case "pluginCountLine": return "插件文件：";
                        case "assetLoadFailed": return "类目/规则状态：加载失败";
                        case "failureReason": return "失败原因：";
                        case "currentFilters": return "当前筛选配置";
                        case "saveDir": return "保存目录：";
                        case "priceRange": return "售价范围：";
                        case "minProfit": return "最低利润率：";
                        case "defaultShipping": return "默认运费：";
                        case "enable1688": return "启用 1688：";
                        case "autoExport": return "自动导出：";
                        case "cloudFilter": return "云筛选：";
                        case "yes": return "是";
                        case "no": return "否";
                        case "quickStart": return "上手说明";
                        case "quickGuide": return "1. 打开插件浏览器，先登录 1688，保持浏览器会话在线。\r\n2. 在类目与规则里双击一个二级类目，系统会把类目 ID、类型 ID 和中文关键词带入 Auto Sourcing。\r\n3. 在 Auto Sourcing 点 Run，程序会跳到插件浏览器搜索 1688，抓取商品、详情、价格、图片和属性。\r\n4. 点 Upload 或在总览页跑全链路循环，系统会生成俄文标题和文案，再按 Ozon Seller API 上传。\r\n5. SKU 创建和库存写入过程会实时回写到总览简报里，关键词始终保持中文。";
                        case "reloadConfig": return "重新读取配置";
                        case "saveConfig": return "保存当前配置";
                        case "initBrowser": return "初始化插件浏览器";
                        case "openUrl": return "打开网址";
                        case "filterConfig": return "筛选配置";
                        case "reloadAssets": return "重新读取资源";
                        case "exportFeeRules": return "导出运费规则";
                        case "useRuleForAuto": return "把规则带入 Auto";
                        case "searchAssets": return "搜索类目/规则";
                        case "categoryTree": return "类目树";
                        case "feeRuleTable": return "运费规则表";
                        case "languageTitle": return "界面语言";
                        case "languageDesc": return "这里可以切换中文、英文、俄文三套界面。应用后会立即刷新当前窗口。";
                        case "languageCurrent": return "当前界面语言";
                        case "applyLanguage": return "应用语言";
                        case "languageNote": return "注意：Auto Sourcing 里的关键词始终保持中文，不会被语言切换改成英文或俄文。";
                        case "languageApplied": return "界面语言已切换。";
                        case "languageNoChange": return "当前已经是这个界面语言。";
                        case "ruleAppliedToAuto": return "已把选中规则带入 Auto Sourcing。";
                        default: return key;
                    }
            }
        }

        private string SafeValue(string text)
        {
            return string.IsNullOrEmpty(text) ? "未找到" : text;
        }

        private string YesNo(bool value)
        {
            return value ? T("yes") : T("no");
        }

        private void SetStatus(string message)
        {
            if (_statusLabel != null)
            {
                _statusLabel.Text = message;
            }
        }
    }
}



