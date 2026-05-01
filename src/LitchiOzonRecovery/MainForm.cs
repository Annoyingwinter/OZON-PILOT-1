using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json.Linq;

namespace LitchiOzonRecovery
{
    public sealed class MainForm : Form
    {
        private readonly AppPaths _paths;
        private readonly DatabaseService _databaseService;
        private readonly UpdaterService _updaterService;
        private readonly ProductAutomationService _automationService;
        private readonly OzonPlusService _ozonPlusService;
        private readonly Random _random;
        private AssetSnapshot _snapshot;
        private SourcingResult _lastSourcingResult;
        private OzonPlusEnvironmentStatus _ozonPlusStatus;
        private string _fullAutoReport;

        private TabControl _mainTabs;
        private TextBox _overviewBox;
        private Label _cardCategoryValue;
        private Label _cardFeeValue;
        private Label _cardPluginValue;
        private Label _cardDbValue;
        private TextBox _autoLoopCountBox;
        private PropertyGrid _configGrid;
        private Label _dbSummaryLabel;
        private Label _dbBatchSummaryLabel;
        private ComboBox _dbTableSelector;
        private TextBox _dbBatchInputBox;
        private DataGridView _skuGrid;
        private DataGridView _shopGrid;
        private DataGridView _catchGrid;
        private TreeView _categoryTree;
        private DataGridView _feeGrid;
        private TextBox _assetSearchBox;
        private TextBox _browserUrlBox;
        private Label _browserStatusLabel;
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
        private TextBox _ozonClientIdBox;
        private TextBox _ozonApiKeyBox;
        private TextBox _ozonWarehouseIdBox;
        private TextBox _ozonWarehouseNameBox;
        private DataGridView _autoResultGrid;
        private TextBox _autoLogBox;
        private Label _autoEnvLabel;
        private TextBox _calcCategory1Box;
        private TextBox _calcCategory2Box;
        private TextBox _calcSourcePriceBox;
        private TextBox _calcWeightBox;
        private TextBox _calcDeliveryFeeBox;
        private TextBox _calcOtherCostBox;
        private TextBox _calcTargetProfitBox;
        private TextBox _calcManualPriceBox;
        private ComboBox _calcModeCombo;
        private TextBox _calcResultBox;
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _statusLabel;
        private string _databaseErrorMessage;
        private string _assetErrorMessage;

        public MainForm()
        {
            _paths = AppPaths.Discover();
            _databaseService = new DatabaseService(_paths.DatabaseFile);
            _updaterService = new UpdaterService(_paths);
            _automationService = new ProductAutomationService();
            _ozonPlusService = new OzonPlusService(_paths);
            _random = new Random();

            Text = "OZON-PILOT";
            Width = 1460;
            Height = 960;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1280, 820);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            BackColor = Color.FromArgb(245, 247, 250);

            InitializeControls();
            LoadAll();
            Shown += delegate { InitializeBrowser(null, EventArgs.Empty); };
        }

        private void InitializeControls()
        {
            Panel header = BuildHeaderPanel();

            _mainTabs = new TabControl();
            _mainTabs.Dock = DockStyle.Fill;
            _mainTabs.ItemSize = new Size(120, 34);
            _mainTabs.SizeMode = TabSizeMode.Fixed;
            _mainTabs.TabPages.Add(BuildOverviewTab());
            _mainTabs.TabPages.Add(BuildConfigTab());
            _mainTabs.TabPages.Add(BuildDatabaseTab());
            _mainTabs.TabPages.Add(BuildAssetsTab());
            _mainTabs.TabPages.Add(BuildCalculatorTab());
            _mainTabs.TabPages.Add(BuildAutomationTab());
            _mainTabs.TabPages.Add(BuildBrowserTab());

            _statusStrip = new StatusStrip();
            _statusStrip.SizingGrip = false;
            _statusLabel = new ToolStripStatusLabel();
            _statusLabel.Text = "准备就绪";
            _statusStrip.Items.Add(_statusLabel);

            Controls.Add(_mainTabs);
            Controls.Add(header);
            Controls.Add(_statusStrip);
        }

        private Panel BuildHeaderPanel()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Top;
            panel.Height = 84;
            panel.BackColor = Color.White;
            panel.Padding = new Padding(20, 14, 20, 12);

            Label title = new Label();
            title.Text = "OZON-PILOT";
            title.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold, GraphicsUnit.Point, 134);
            title.AutoSize = true;
            title.Location = new Point(18, 10);

            Label subtitle = new Label();
            subtitle.Text = "已接回配置、类目树、运费规则、插件与更新器。数据库支持批量粘贴导入，并会明确区分空库与加载失败。";
            subtitle.ForeColor = Color.FromArgb(96, 98, 102);
            subtitle.AutoSize = true;
            subtitle.Location = new Point(20, 48);

            panel.Controls.Add(title);
            panel.Controls.Add(subtitle);
            return panel;
        }

        private TabPage BuildOverviewTab()
        {
            TabPage tab = CreateTabPage("总览");

            FlowLayoutPanel actions = CreateActionBar();
            actions.Controls.Add(CreateButton("重新载入全部", delegate { LoadAll(); }, true));
            actions.Controls.Add(CreateButton("打开基线资源", delegate { _paths.OpenPath(_paths.BaselineRoot); }, false));
            actions.Controls.Add(CreateButton("打开 1688 插件", delegate { _paths.OpenPath(_paths.Plugin1688Folder); }, false));
            actions.Controls.Add(CreateButton("打开数据库文件", delegate { _paths.OpenPath(_paths.DatabaseFile); }, false));
            actions.Controls.Add(CreateButton("运行更新器", LaunchUpdater, false));

            Label loopLabel = new Label();
            loopLabel.Text = "全自动循环次数";
            loopLabel.AutoSize = true;
            loopLabel.Margin = new Padding(20, 9, 4, 0);
            actions.Controls.Add(loopLabel);
            _autoLoopCountBox = new TextBox();
            _autoLoopCountBox.Width = 54;
            _autoLoopCountBox.Text = "1";
            actions.Controls.Add(_autoLoopCountBox);
            actions.Controls.Add(CreateButton("全链路自动循环", RunFullAutoLoop, true));

            TableLayoutPanel cards = new TableLayoutPanel();
            cards.Dock = DockStyle.Top;
            cards.Height = 124;
            cards.ColumnCount = 4;
            cards.RowCount = 1;
            cards.Padding = new Padding(12, 0, 12, 0);
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));

            cards.Controls.Add(CreateStatCard("类目节点", "0", "从 category.txt 读取到的类目树节点总数", Color.FromArgb(64, 158, 255), out _cardCategoryValue), 0, 0);
            cards.Controls.Add(CreateStatCard("运费规则", "0", "从 fee.txt 读取到的运费规则总数", Color.FromArgb(103, 194, 58), out _cardFeeValue), 1, 0);
            cards.Controls.Add(CreateStatCard("插件文件", "0", "1688 插件目录中的文件总数", Color.FromArgb(230, 162, 60), out _cardPluginValue), 2, 0);
            cards.Controls.Add(CreateStatCard("数据库记录", "0", "三张本地表的记录合计", Color.FromArgb(245, 108, 108), out _cardDbValue), 3, 0);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.SplitterDistance = 800;
            split.Panel1.Padding = new Padding(12, 8, 6, 12);
            split.Panel2.Padding = new Padding(6, 8, 12, 12);

            _overviewBox = new TextBox();
            _overviewBox.Multiline = true;
            _overviewBox.ReadOnly = true;
            _overviewBox.ScrollBars = ScrollBars.Vertical;
            _overviewBox.BackColor = Color.White;
            _overviewBox.BorderStyle = BorderStyle.FixedSingle;
            _overviewBox.Font = new Font("Microsoft YaHei UI", 9F);

            TextBox quickGuide = new TextBox();
            quickGuide.Multiline = true;
            quickGuide.ReadOnly = true;
            quickGuide.ScrollBars = ScrollBars.Vertical;
            quickGuide.BackColor = Color.White;
            quickGuide.BorderStyle = BorderStyle.FixedSingle;
            quickGuide.Font = new Font("Microsoft YaHei UI", 9F);
            quickGuide.Text =
                "当前这版你能做的事情：" + Environment.NewLine +
                "1. 在“配置中心”核对原有筛选参数和默认运费。" + Environment.NewLine +
                "2. 在“数据库管理”批量导入 SKU / 店铺 ID，并做去重、清空、导出。" + Environment.NewLine +
                "3. 在“类目与规则”里查看原包带回来的类目树和 519 条运费规则。" + Environment.NewLine +
                "4. 在“利润测算”里按类目、重量、成本快速估算售价和利润率。" + Environment.NewLine +
                "5. 在“插件浏览器”里继续联调 1688 插件环境。" + Environment.NewLine +
                Environment.NewLine +
                "当前还不能诚实地说已经恢复完成的功能：" + Environment.NewLine +
                "1. OZON 自动选品主流程。" + Environment.NewLine +
                "2. OZON 自动上架 / 自动刊登。" + Environment.NewLine +
                "3. 原程序里的完整业务编排和任务自动化。" + Environment.NewLine +
                Environment.NewLine +
                "原因是这几个核心能力不在现有 1688 插件目录里，更多逻辑大概率还封在原始主程序二进制中，仍需要继续反推和重建。";

            split.Panel1.Controls.Add(WrapWithGroup("恢复资产明细", _overviewBox));
            split.Panel2.Controls.Add(WrapWithGroup("上手说明", quickGuide));

            tab.Controls.Add(split);
            tab.Controls.Add(cards);
            tab.Controls.Add(actions);
            return tab;
        }

        private TabPage BuildConfigTab()
        {
            TabPage tab = CreateTabPage("配置中心");

            FlowLayoutPanel actions = CreateActionBar();
            actions.Controls.Add(CreateButton("重新读取配置", delegate { LoadConfig(); UpdateOverview(); }, true));
            actions.Controls.Add(CreateButton("保存当前配置", SaveConfig, false));

            _configGrid = new PropertyGrid();
            _configGrid.Dock = DockStyle.Fill;
            _configGrid.HelpVisible = true;
            _configGrid.ToolbarVisible = false;
            _configGrid.PropertySort = PropertySort.Categorized;
            _configGrid.BackColor = Color.White;
            _configGrid.ViewBackColor = Color.White;

            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.Padding = new Padding(12);
            body.Controls.Add(WrapWithGroup("筛选配置", _configGrid));

            tab.Controls.Add(body);
            tab.Controls.Add(actions);
            return tab;
        }

        private TabPage BuildDatabaseTab()
        {
            TabPage tab = CreateTabPage("数据库管理");

            FlowLayoutPanel actions = CreateActionBar();
            actions.Controls.Add(CreateButton("刷新数据库", delegate { LoadDatabase(); }, true));
            actions.Controls.Add(CreateButton("追加导入文件", AppendIdsIntoTable, false));
            actions.Controls.Add(CreateButton("覆盖导入文件", ReplaceIdsIntoTable, false));
            actions.Controls.Add(CreateButton("导出当前表", ExportIdsFromTable, false));
            actions.Controls.Add(CreateButton("批量粘贴入库", ImportPastedIdsIntoTable, false));
            actions.Controls.Add(CreateButton("清空当前表", ClearCurrentTable, false));

            Label selectorLabel = new Label();
            selectorLabel.Text = "当前操作表：";
            selectorLabel.AutoSize = true;
            selectorLabel.Margin = new Padding(20, 9, 4, 0);
            actions.Controls.Add(selectorLabel);

            _dbTableSelector = new ComboBox();
            _dbTableSelector.DropDownStyle = ComboBoxStyle.DropDownList;
            _dbTableSelector.Width = 160;
            _dbTableSelector.Items.AddRange(new object[] { "SkuTable", "ShopTable", "tb_catch_shop" });
            _dbTableSelector.SelectedIndex = 0;
            actions.Controls.Add(_dbTableSelector);

            Panel summary = new Panel();
            summary.Dock = DockStyle.Top;
            summary.Height = 74;
            summary.Padding = new Padding(16, 10, 16, 0);
            summary.BackColor = Color.White;

            _dbSummaryLabel = new Label();
            _dbSummaryLabel.Dock = DockStyle.Top;
            _dbSummaryLabel.Height = 24;
            _dbSummaryLabel.ForeColor = Color.FromArgb(96, 98, 102);

            _dbBatchSummaryLabel = new Label();
            _dbBatchSummaryLabel.Dock = DockStyle.Top;
            _dbBatchSummaryLabel.Height = 24;
            _dbBatchSummaryLabel.ForeColor = Color.FromArgb(144, 147, 153);

            summary.Controls.Add(_dbBatchSummaryLabel);
            summary.Controls.Add(_dbSummaryLabel);

            SplitContainer layout = new SplitContainer();
            layout.Dock = DockStyle.Fill;
            layout.SplitterDistance = 940;
            layout.Panel1.Padding = new Padding(12, 10, 6, 12);
            layout.Panel2.Padding = new Padding(6, 10, 12, 12);

            TableLayoutPanel left = new TableLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.ColumnCount = 1;
            left.RowCount = 3;
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 34F));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 33F));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 33F));

            _skuGrid = CreateGrid();
            _shopGrid = CreateGrid();
            _catchGrid = CreateGrid();

            left.Controls.Add(WrapWithGroup("SKU 表预览", _skuGrid), 0, 0);
            left.Controls.Add(WrapWithGroup("店铺表预览", _shopGrid), 0, 1);
            left.Controls.Add(WrapWithGroup("抓取店铺表预览", _catchGrid), 0, 2);

            Panel editor = new Panel();
            editor.Dock = DockStyle.Fill;
            editor.BackColor = Color.White;
            editor.Padding = new Padding(12);

            Label hint = new Label();
            hint.Text = "批量导入区支持一行一个 ID，也支持逗号、空格、制表符混排。适合从表格、网页和聊天记录里直接粘贴。";
            hint.Dock = DockStyle.Top;
            hint.Height = 38;
            hint.ForeColor = Color.FromArgb(96, 98, 102);

            _dbBatchInputBox = new TextBox();
            _dbBatchInputBox.Dock = DockStyle.Fill;
            _dbBatchInputBox.Multiline = true;
            _dbBatchInputBox.ScrollBars = ScrollBars.Vertical;
            _dbBatchInputBox.BorderStyle = BorderStyle.FixedSingle;
            _dbBatchInputBox.Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);

            FlowLayoutPanel editorActions = new FlowLayoutPanel();
            editorActions.Dock = DockStyle.Bottom;
            editorActions.Height = 80;
            editorActions.WrapContents = true;
            editorActions.Padding = new Padding(0, 10, 0, 0);
            editorActions.Controls.Add(CreateButton("从剪贴板粘贴", PasteFromClipboard, true));
            editorActions.Controls.Add(CreateButton("统计待导入数量", AnalyzePastedIds, false));
            editorActions.Controls.Add(CreateButton("去重整理", NormalizePastedIds, false));
            editorActions.Controls.Add(CreateButton("清空粘贴区", ClearPastedIds, false));
            editorActions.Controls.Add(CreateButton("追加入当前表", ImportPastedIdsIntoTable, false));

            editor.Controls.Add(_dbBatchInputBox);
            editor.Controls.Add(editorActions);
            editor.Controls.Add(hint);

            layout.Panel1.Controls.Add(left);
            layout.Panel2.Controls.Add(WrapWithGroup("批量导入工作区", editor));

            tab.Controls.Add(layout);
            tab.Controls.Add(summary);
            tab.Controls.Add(actions);
            return tab;
        }

        private TabPage BuildAssetsTab()
        {
            TabPage tab = CreateTabPage("类目与规则");

            FlowLayoutPanel actions = CreateActionBar();
            actions.Controls.Add(CreateButton("重新读取资源", delegate { LoadAssets(); }, true));
            actions.Controls.Add(CreateButton("导出运费规则 Excel", ExportFeeRules, false));
            actions.Controls.Add(CreateButton("把选中规则带入测算", UseSelectedFeeRule, false));

            Label searchLabel = new Label();
            searchLabel.Text = "搜索类目/规则：";
            searchLabel.AutoSize = true;
            searchLabel.Margin = new Padding(20, 9, 4, 0);
            actions.Controls.Add(searchLabel);

            _assetSearchBox = new TextBox();
            _assetSearchBox.Width = 260;
            _assetSearchBox.TextChanged += FilterAssetsChanged;
            actions.Controls.Add(_assetSearchBox);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.SplitterDistance = 460;
            split.Panel1.Padding = new Padding(12, 10, 6, 12);
            split.Panel2.Padding = new Padding(6, 10, 12, 12);

            _categoryTree = new TreeView();
            _categoryTree.Dock = DockStyle.Fill;
            _categoryTree.BackColor = Color.White;
            _categoryTree.NodeMouseDoubleClick += UseSelectedCategoryForAutomation;

            _feeGrid = CreateGrid();
            _feeGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _feeGrid.MultiSelect = false;
            _feeGrid.CellDoubleClick += UseSelectedFeeRule;

            split.Panel1.Controls.Add(WrapWithGroup("类目树", _categoryTree));
            split.Panel2.Controls.Add(WrapWithGroup("运费规则表", _feeGrid));

            tab.Controls.Add(split);
            tab.Controls.Add(actions);
            return tab;
        }

        private TabPage BuildCalculatorTab()
        {
            TabPage tab = CreateTabPage("利润测算");

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.SplitterDistance = 470;
            split.Panel1.Padding = new Padding(12);
            split.Panel2.Padding = new Padding(0, 12, 12, 12);

            Panel editor = new Panel();
            editor.Dock = DockStyle.Fill;
            editor.BackColor = Color.White;
            editor.Padding = new Padding(16);

            int rowTop = 18;
            int labelLeft = 16;
            int inputLeft = 150;
            int labelWidth = 120;
            int inputWidth = 250;

            editor.Controls.Add(CreateFormLabel("一级类目 ID", labelLeft, rowTop, labelWidth));
            _calcCategory1Box = CreateTextBox(inputLeft, rowTop, inputWidth, "0");
            editor.Controls.Add(_calcCategory1Box);
            rowTop += 38;

            editor.Controls.Add(CreateFormLabel("二级类目 ID", labelLeft, rowTop, labelWidth));
            _calcCategory2Box = CreateTextBox(inputLeft, rowTop, inputWidth, "0");
            editor.Controls.Add(_calcCategory2Box);
            rowTop += 38;

            editor.Controls.Add(CreateFormLabel("采购成本", labelLeft, rowTop, labelWidth));
            _calcSourcePriceBox = CreateTextBox(inputLeft, rowTop, inputWidth, "0");
            editor.Controls.Add(_calcSourcePriceBox);
            rowTop += 38;

            editor.Controls.Add(CreateFormLabel("重量(g)", labelLeft, rowTop, labelWidth));
            _calcWeightBox = CreateTextBox(inputLeft, rowTop, inputWidth, "500");
            editor.Controls.Add(_calcWeightBox);
            rowTop += 38;

            editor.Controls.Add(CreateFormLabel("运费", labelLeft, rowTop, labelWidth));
            _calcDeliveryFeeBox = CreateTextBox(inputLeft, rowTop, inputWidth, "0");
            editor.Controls.Add(_calcDeliveryFeeBox);
            rowTop += 38;

            editor.Controls.Add(CreateFormLabel("其他成本", labelLeft, rowTop, labelWidth));
            _calcOtherCostBox = CreateTextBox(inputLeft, rowTop, inputWidth, "0");
            editor.Controls.Add(_calcOtherCostBox);
            rowTop += 38;

            editor.Controls.Add(CreateFormLabel("目标利润率(%)", labelLeft, rowTop, labelWidth));
            _calcTargetProfitBox = CreateTextBox(inputLeft, rowTop, inputWidth, "0");
            editor.Controls.Add(_calcTargetProfitBox);
            rowTop += 38;

            editor.Controls.Add(CreateFormLabel("手动售价", labelLeft, rowTop, labelWidth));
            _calcManualPriceBox = CreateTextBox(inputLeft, rowTop, inputWidth, "0");
            editor.Controls.Add(_calcManualPriceBox);
            rowTop += 38;

            editor.Controls.Add(CreateFormLabel("履约模式", labelLeft, rowTop, labelWidth));
            _calcModeCombo = new ComboBox();
            _calcModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _calcModeCombo.Left = inputLeft;
            _calcModeCombo.Top = rowTop;
            _calcModeCombo.Width = inputWidth;
            _calcModeCombo.Items.AddRange(new object[] { "FBS", "FBP", "FBO" });
            _calcModeCombo.SelectedIndex = 0;
            editor.Controls.Add(_calcModeCombo);
            rowTop += 56;

            Button applyConfigButton = CreateButton("套用配置默认值", ApplyConfigDefaultsToCalculator, true);
            applyConfigButton.Left = inputLeft;
            applyConfigButton.Top = rowTop;
            editor.Controls.Add(applyConfigButton);

            Button calculateButton = CreateButton("开始测算", CalculateProfit, false);
            calculateButton.Left = inputLeft + 150;
            calculateButton.Top = rowTop;
            editor.Controls.Add(calculateButton);

            Label hint = new Label();
            hint.Text = "提示：在“类目与规则”里双击一条运费规则，会自动把类目 ID 带入这里。";
            hint.ForeColor = Color.FromArgb(144, 147, 153);
            hint.AutoSize = true;
            hint.Left = 18;
            hint.Top = rowTop + 44;
            editor.Controls.Add(hint);

            _calcResultBox = new TextBox();
            _calcResultBox.Multiline = true;
            _calcResultBox.ReadOnly = true;
            _calcResultBox.ScrollBars = ScrollBars.Vertical;
            _calcResultBox.BackColor = Color.White;
            _calcResultBox.BorderStyle = BorderStyle.FixedSingle;

            split.Panel1.Controls.Add(WrapWithGroup("测算输入", editor));
            split.Panel2.Controls.Add(WrapWithGroup("测算结果", _calcResultBox));

            tab.Controls.Add(split);
            return tab;
        }

        private TabPage BuildAutomationTab()
        {
            TabPage tab = CreateTabPage("Auto Sourcing");

            FlowLayoutPanel actions = CreateActionBar();
            actions.Controls.Add(CreateButton("Run 1688 selection", RunAutoSourcing, true));
            actions.Controls.Add(CreateButton("Upload selected to Ozon", UploadSelectedToOzon, false));
            actions.Controls.Add(CreateButton("Check Ozon task", CheckOzonTask, false));
            actions.Controls.Add(CreateButton("Export candidates", ExportAutoCandidates, false));

            SplitContainer main = new SplitContainer();
            main.Dock = DockStyle.Fill;
            main.SplitterDistance = 420;
            main.Panel1.Padding = new Padding(12);
            main.Panel2.Padding = new Padding(0, 12, 12, 12);

            Panel editor = new Panel();
            editor.Dock = DockStyle.Fill;
            editor.BackColor = Color.White;
            editor.Padding = new Padding(16);

            int top = 18;
            int labelLeft = 16;
            int inputLeft = 150;
            int labelWidth = 120;
            int inputWidth = 230;

            editor.Controls.Add(CreateFormLabel("Keywords", labelLeft, top, labelWidth));
            _autoKeywordsBox = CreateTextBox(inputLeft, top, inputWidth, "car storage organizer\r\npet slow feeder\r\nkitchen spice rack");
            _autoKeywordsBox.Multiline = true;
            _autoKeywordsBox.Height = 88;
            editor.Controls.Add(_autoKeywordsBox);
            top += 100;

            Label browserMode = new Label();
            browserMode.Text = "1688 Source";
            browserMode.Left = labelLeft;
            browserMode.Top = top + 6;
            browserMode.Width = labelWidth;
            editor.Controls.Add(browserMode);

            Label browserModeValue = new Label();
            browserModeValue.Text = "Plugin browser session";
            browserModeValue.Left = inputLeft;
            browserModeValue.Top = top + 6;
            browserModeValue.Width = inputWidth;
            browserModeValue.ForeColor = Color.FromArgb(64, 158, 255);
            editor.Controls.Add(browserModeValue);

            _autoProviderBox = new ComboBox();
            _autoProviderBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _autoProviderBox.Left = inputLeft;
            _autoProviderBox.Top = top;
            _autoProviderBox.Width = inputWidth;
            _autoProviderBox.Items.AddRange(new object[] { "browser" });
            _autoProviderBox.SelectedIndex = 0;
            top += 34;

            FlowLayoutPanel quickActions = new FlowLayoutPanel();
            quickActions.Left = inputLeft;
            quickActions.Top = top;
            quickActions.Width = 260;
            quickActions.Height = 74;
            quickActions.WrapContents = true;
            quickActions.Controls.Add(CreateButton("Run", RunAutoSourcing, true));
            quickActions.Controls.Add(CreateButton("Upload", UploadSelectedToOzon, false));
            quickActions.Controls.Add(CreateButton("Export", ExportAutoCandidates, false));
            editor.Controls.Add(quickActions);
            top += 78;

            _autoApiKeyBox = CreateTextBox(inputLeft, top, inputWidth, string.Empty);
            _autoApiKeyBox.Visible = false;

            _autoApiSecretBox = CreateTextBox(inputLeft, top, inputWidth, string.Empty);
            _autoApiSecretBox.Visible = false;

            editor.Controls.Add(CreateFormLabel("Per keyword", labelLeft, top, labelWidth));
            _autoPerKeywordBox = CreateTextBox(inputLeft, top, inputWidth, "5");
            editor.Controls.Add(_autoPerKeywordBox);
            top += 34;

            editor.Controls.Add(CreateFormLabel("Detail limit", labelLeft, top, labelWidth));
            _autoDetailLimitBox = CreateTextBox(inputLeft, top, inputWidth, "12");
            editor.Controls.Add(_autoDetailLimitBox);
            top += 34;

            editor.Controls.Add(CreateFormLabel("RUB/CNY", labelLeft, top, labelWidth));
            _autoRubRateBox = CreateTextBox(inputLeft, top, inputWidth, "12.5");
            editor.Controls.Add(_autoRubRateBox);
            top += 34;

            editor.Controls.Add(CreateFormLabel("Ozon Category", labelLeft, top, labelWidth));
            _autoCategoryIdBox = CreateTextBox(inputLeft, top, inputWidth, "0");
            editor.Controls.Add(_autoCategoryIdBox);
            top += 34;

            editor.Controls.Add(CreateFormLabel("Ozon Type", labelLeft, top, labelWidth));
            _autoTypeIdBox = CreateTextBox(inputLeft, top, inputWidth, "0");
            editor.Controls.Add(_autoTypeIdBox);
            top += 34;

            editor.Controls.Add(CreateFormLabel("Price x", labelLeft, top, labelWidth));
            _autoPriceMultiplierBox = CreateTextBox(inputLeft, top, inputWidth, "2.2");
            editor.Controls.Add(_autoPriceMultiplierBox);
            top += 34;

            editor.Controls.Add(CreateFormLabel("Ozon Client-Id", labelLeft, top, labelWidth));
            _ozonClientIdBox = CreateTextBox(inputLeft, top, inputWidth, string.Empty);
            editor.Controls.Add(_ozonClientIdBox);
            top += 34;

            editor.Controls.Add(CreateFormLabel("Ozon Api-Key", labelLeft, top, labelWidth));
            _ozonApiKeyBox = CreateTextBox(inputLeft, top, inputWidth, string.Empty);
            editor.Controls.Add(_ozonApiKeyBox);
            top += 42;

            Label hint = new Label();
            hint.Text = "先在插件浏览器登录 1688，再点 Run。程序会用同一个浏览器会话搜索并抓详情。Ozon 字段只在上传时需要。";
            hint.Left = labelLeft;
            hint.Top = top;
            hint.Width = 360;
            hint.Height = 60;
            hint.ForeColor = Color.FromArgb(96, 98, 102);
            editor.Controls.Add(hint);

            SplitContainer right = new SplitContainer();
            right.Dock = DockStyle.Fill;
            right.Orientation = Orientation.Horizontal;
            right.SplitterDistance = 430;

            _autoResultGrid = CreateGrid();
            _autoResultGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _autoResultGrid.MultiSelect = true;

            _autoLogBox = new TextBox();
            _autoLogBox.Multiline = true;
            _autoLogBox.ReadOnly = true;
            _autoLogBox.ScrollBars = ScrollBars.Vertical;
            _autoLogBox.BackColor = Color.White;
            _autoLogBox.BorderStyle = BorderStyle.FixedSingle;
            _autoLogBox.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);

            right.Panel1.Controls.Add(WrapWithGroup("Candidates", _autoResultGrid));
            right.Panel2.Controls.Add(WrapWithGroup("Automation log", _autoLogBox));
            main.Panel1.Controls.Add(WrapWithGroup("Automation settings", editor));
            main.Panel2.Controls.Add(right);

            tab.Controls.Add(main);
            tab.Controls.Add(actions);
            return tab;
        }

        private TabPage BuildBrowserTab()
        {
            TabPage tab = CreateTabPage("插件浏览器");

            FlowLayoutPanel actions = CreateActionBar();
            actions.Controls.Add(CreateButton("初始化插件浏览器", InitializeBrowser, true));
            actions.Controls.Add(CreateButton("打开网址", NavigateBrowser, false));

            Label urlLabel = new Label();
            urlLabel.Text = "网址：";
            urlLabel.AutoSize = true;
            urlLabel.Margin = new Padding(20, 9, 4, 0);
            actions.Controls.Add(urlLabel);

            _browserUrlBox = new TextBox();
            _browserUrlBox.Width = 420;
            _browserUrlBox.Text = "https://www.1688.com/";
            actions.Controls.Add(_browserUrlBox);

            Panel summary = new Panel();
            summary.Dock = DockStyle.Top;
            summary.Height = 54;
            summary.Padding = new Padding(16, 12, 16, 0);
            summary.BackColor = Color.White;

            _browserStatusLabel = new Label();
            _browserStatusLabel.Dock = DockStyle.Top;
            _browserStatusLabel.Height = 24;
            _browserStatusLabel.ForeColor = Color.FromArgb(96, 98, 102);
            _browserStatusLabel.Text = "浏览器尚未初始化。";
            summary.Controls.Add(_browserStatusLabel);

            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.Padding = new Padding(12);

            _browser = new WebView2();
            body.Controls.Add(WrapWithGroup("1688 插件运行区", _browser));

            tab.Controls.Add(body);
            tab.Controls.Add(summary);
            tab.Controls.Add(actions);
            return tab;
        }

        private void LoadAll()
        {
            _databaseErrorMessage = null;
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

            try
            {
                LoadDatabase();
            }
            catch (Exception ex)
            {
                _databaseErrorMessage = ex.Message;
                ClearDatabaseViews();
            }

            ApplyConfigDefaultsToCalculator(null, EventArgs.Empty);
            UpdateOverview();

            if (!string.IsNullOrEmpty(_databaseErrorMessage))
            {
                SetStatus("类目和规则已加载，但数据库暂不可用：" + _databaseErrorMessage);
                return;
            }

            if (!string.IsNullOrEmpty(_assetErrorMessage))
            {
                SetStatus("数据库已加载，但类目/规则读取失败：" + _assetErrorMessage);
                return;
            }

            SetStatus("恢复工作台加载完成。");
        }

        private void LoadConfig()
        {
            EnsureSnapshot();
            _snapshot.Config = ConfigService.Load(_paths.ConfigFile);
            _configGrid.SelectedObject = _snapshot.Config;
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

                ConfigService.Save(_paths.ConfigFile, config);
                UpdateOverview();
                ApplyConfigDefaultsToCalculator(null, EventArgs.Empty);
                SetStatus("配置已保存。");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadDatabase()
        {
            EnsureSnapshot();
            _snapshot.TableCounts = _databaseService.GetTableCounts();
            _databaseErrorMessage = null;

            long skuCount = _snapshot.TableCounts["SkuTable"];
            long shopCount = _snapshot.TableCounts["ShopTable"];
            long catchCount = _snapshot.TableCounts["tb_catch_shop"];
            long total = skuCount + shopCount + catchCount;

            _dbSummaryLabel.Text = string.Format("当前记录数：SKU 表 {0} 条，店铺表 {1} 条，抓取店铺表 {2} 条。", skuCount, shopCount, catchCount);
            _dbBatchSummaryLabel.Text = total == 0
                ? "当前基线数据库为空，可以直接在右侧粘贴 ID 批量入库。"
                : "可在右侧批量整理 ID，再导入当前选中的表。";

            _skuGrid.DataSource = _databaseService.GetPreview("SkuTable", 200);
            _shopGrid.DataSource = _databaseService.GetPreview("ShopTable", 200);
            _catchGrid.DataSource = _databaseService.GetPreview("tb_catch_shop", 200);
            UpdateOverview();
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
            long dbTotal = GetCount("SkuTable") + GetCount("ShopTable") + GetCount("tb_catch_shop");

            if (_cardCategoryValue != null) _cardCategoryValue.Text = categoryCount.ToString();
            if (_cardFeeValue != null) _cardFeeValue.Text = feeCount.ToString();
            if (_cardPluginValue != null) _cardPluginValue.Text = pluginFileCount.ToString();
            if (_cardDbValue != null) _cardDbValue.Text = dbTotal.ToString();

            string updaterPath = _paths.FindUpdaterExecutable();
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("资源路径");
            builder.AppendLine("--------");
            builder.AppendLine("工作区目录：" + _paths.WorkRoot);
            builder.AppendLine("基线目录：" + _paths.BaselineRoot);
            builder.AppendLine("配置文件：" + _paths.ConfigFile);
            builder.AppendLine("数据库文件：" + _paths.DatabaseFile);
            builder.AppendLine("1688 插件：" + _paths.Plugin1688Folder);
            builder.AppendLine("更新器程序：" + SafeValue(updaterPath));
            builder.AppendLine();
            builder.AppendLine("恢复状态");
            builder.AppendLine("--------");
            builder.AppendLine("类目节点：" + categoryCount + " 个");
            builder.AppendLine("运费规则：" + feeCount + " 条");
            builder.AppendLine("插件文件：" + pluginFileCount + " 个");
            builder.AppendLine("数据库总量：" + dbTotal + " 条");
            builder.AppendLine(dbTotal == 0
                ? "数据库说明：当前发布包内的基线库本身为空，没有找到额外业务数据备份。"
                : "数据库说明：当前已读取到可用记录，可在数据库页继续批量管理。");

            if (!string.IsNullOrEmpty(_databaseErrorMessage))
            {
                builder.AppendLine("数据库状态：加载失败");
                builder.AppendLine("失败原因：" + _databaseErrorMessage);
            }

            if (!string.IsNullOrEmpty(_assetErrorMessage))
            {
                builder.AppendLine("类目/规则状态：加载失败");
                builder.AppendLine("失败原因：" + _assetErrorMessage);
            }

            builder.AppendLine();
            builder.AppendLine("当前筛选配置");
            builder.AppendLine("------------");
            builder.AppendLine("保存目录：" + SafeValue(_snapshot.Config == null ? null : _snapshot.Config.SaveUrl));
            builder.AppendLine("售价范围：" + (_snapshot.Config == null ? 0 : _snapshot.Config.MinPirce) + " ~ " + (_snapshot.Config == null ? 0 : _snapshot.Config.MaxPrice));
            builder.AppendLine("最低利润率：" + (_snapshot.Config == null ? 0 : _snapshot.Config.MinProfitPer) + "%");
            builder.AppendLine("默认运费：" + (_snapshot.Config == null ? 0 : _snapshot.Config.DeliveryFee));
            builder.AppendLine("启用 1688：" + YesNo(_snapshot.Config != null && _snapshot.Config.Is1688));
            builder.AppendLine("自动导出：" + YesNo(_snapshot.Config != null && _snapshot.Config.IsAutoExport));
            builder.AppendLine("云筛选：" + YesNo(_snapshot.Config != null && _snapshot.Config.IsCloudFilter));

            if (!string.IsNullOrEmpty(_fullAutoReport))
            {
                builder.AppendLine();
                builder.AppendLine("全链路自动循环简报");
                builder.AppendLine("----------------");
                builder.AppendLine(_fullAutoReport);
            }

            _overviewBox.Text = builder.ToString();
        }

        private void LaunchUpdater(object sender, EventArgs e)
        {
            string url = PromptDialog.Show(this, "运行更新器", "请输入更新接口地址：", string.Empty);
            if (string.IsNullOrEmpty(url))
            {
                return;
            }

            try
            {
                _updaterService.Launch(url);
                SetStatus("更新器已启动。");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "更新器启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            _feeGrid.DataSource = rules;
        }

        private void UseSelectedFeeRule(object sender, EventArgs e)
        {
            FeeRule rule = GetSelectedFeeRule();
            if (rule == null)
            {
                SetStatus("请先在运费规则表中选中一条规则。");
                return;
            }

            _calcCategory1Box.Text = rule.CategoryId1.ToString();
            _calcCategory2Box.Text = rule.CategoryId2.ToString();
            FillAutomationCategory(rule.CategoryId1.ToString(), rule.CategoryId2.ToString(), rule.Category2);
            _mainTabs.SelectedIndex = 4;
            SetStatus("已把选中规则对应的类目带入利润测算。");
            CalculateProfit(null, EventArgs.Empty);
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

            string categoryId = string.IsNullOrEmpty(node.DescriptionCategoryId) ? "0" : node.DescriptionCategoryId;
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
                _autoKeywordsBox.Text = BuildEnglishKeyword(keyword);
            }
            else if (_autoKeywordsBox != null)
            {
                _autoKeywordsBox.Text = string.Empty;
            }
        }

        private string BuildEnglishKeyword(string keyword)
        {
            string text = string.IsNullOrEmpty(keyword) ? string.Empty : keyword.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return "general merchandise";
            }

            if (IsAsciiKeyword(text))
            {
                return text;
            }

            if (ContainsAny(text, "\u60c5\u8da3", "\u6210\u4eba", "\u6027\u7231", "\u907f\u5b55", "\u5b89\u5168\u5957", "\u79c1\u5904"))
            {
                return "adult wellness product";
            }

            string[,] map = new string[,]
            {
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

            return "general merchandise";
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

        private void ApplyConfigDefaultsToCalculator(object sender, EventArgs e)
        {
            EnsureSnapshot();
            AppConfig config = _snapshot.Config ?? AppConfig.CreateDefault();
            _calcDeliveryFeeBox.Text = config.DeliveryFee.ToString();
            _calcTargetProfitBox.Text = config.MinProfitPer.ToString();
        }

        private void CalculateProfit(object sender, EventArgs e)
        {
            if (_snapshot == null || _snapshot.Config == null || _snapshot.FeeRules == null)
            {
                return;
            }

            ProfitInput input = new ProfitInput();
            input.CategoryId1 = ParseLong(_calcCategory1Box.Text);
            input.CategoryId2 = ParseLong(_calcCategory2Box.Text);
            input.SourcePrice = ParseDecimal(_calcSourcePriceBox.Text);
            input.WeightGrams = ParseDecimal(_calcWeightBox.Text);
            input.DeliveryFee = ParseDecimal(_calcDeliveryFeeBox.Text);
            input.OtherCost = ParseDecimal(_calcOtherCostBox.Text);
            input.TargetProfitPercent = ParseDecimal(_calcTargetProfitBox.Text);
            input.ManualSellingPrice = ParseDecimal(_calcManualPriceBox.Text);
            input.FulfillmentMode = Convert.ToString(_calcModeCombo.SelectedItem);

            ProfitEstimate estimate = ProfitCalculatorService.Calculate(_snapshot.Config, _snapshot.FeeRules, input);
            _calcResultBox.Text = BuildProfitResultText(input, estimate);
            SetStatus("利润测算已更新。");
        }

        private string BuildProfitResultText(ProfitInput input, ProfitEstimate estimate)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("测算输入");
            builder.AppendLine("--------");
            builder.AppendLine("一级类目 ID：" + input.CategoryId1);
            builder.AppendLine("二级类目 ID：" + input.CategoryId2);
            builder.AppendLine("履约模式：" + input.FulfillmentMode);
            builder.AppendLine("采购成本：" + input.SourcePrice);
            builder.AppendLine("重量(g)：" + input.WeightGrams);
            builder.AppendLine();
            builder.AppendLine("测算结果");
            builder.AppendLine("--------");
            builder.AppendLine("匹配规则：" + (estimate.MatchedRule == null ? "未找到" : estimate.MatchedRule.ToString()));
            builder.AppendLine("物流费用：" + estimate.LogisticsFee);
            builder.AppendLine("总成本：" + estimate.EstimatedCost);
            builder.AppendLine("建议售价：" + estimate.SuggestedSellingPrice);
            builder.AppendLine("实际售价：" + estimate.ActualSellingPrice);
            builder.AppendLine("利润金额：" + estimate.ProfitAmount);
            builder.AppendLine("利润率：" + estimate.ProfitPercent + "%");
            builder.AppendLine("满足价格筛选：" + YesNo(estimate.MeetsPriceFilter));
            builder.AppendLine("满足重量筛选：" + YesNo(estimate.MeetsWeightFilter));
            builder.AppendLine("满足利润筛选：" + YesNo(estimate.MeetsProfitFilter));
            builder.AppendLine();
            builder.AppendLine("说明");
            builder.AppendLine("----");
            builder.AppendLine(estimate.Notes);
            return builder.ToString();
        }

        private void AppendIdsIntoTable(object sender, EventArgs e)
        {
            ImportIds(false);
        }

        private void ReplaceIdsIntoTable(object sender, EventArgs e)
        {
            ImportIds(true);
        }

        private void ImportIds(bool replaceAll)
        {
            string tableName = Convert.ToString(_dbTableSelector.SelectedItem);
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "支持文件|*.txt;*.csv;*.tsv;*.xlsx|文本文件|*.txt;*.csv;*.tsv|Excel|*.xlsx|所有文件|*.*";

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                List<string> ids = TableFileService.ReadIds(dialog.FileName);
                int saved = replaceAll
                    ? _databaseService.ReplaceIds(tableName, ids)
                    : _databaseService.AppendIds(tableName, ids);
                LoadDatabase();
                SetStatus((replaceAll ? "已覆盖导入 " : "已追加导入 ") + saved + " 条到 " + tableName + "。");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ImportPastedIdsIntoTable(object sender, EventArgs e)
        {
            string tableName = Convert.ToString(_dbTableSelector.SelectedItem);
            List<string> ids = ParseIdsFromBatchInput();
            if (ids.Count == 0)
            {
                SetStatus("批量粘贴区为空，没有可导入的 ID。");
                return;
            }

            try
            {
                int saved = _databaseService.AppendIds(tableName, ids);
                LoadDatabase();
                _dbBatchSummaryLabel.Text = "已从粘贴区向 " + tableName + " 导入 " + saved + " 条。";
                SetStatus("批量粘贴导入完成。");
            }
            catch (Exception ex)
            {
                _databaseErrorMessage = ex.Message;
                _dbBatchSummaryLabel.Text = "导入失败：" + ex.Message;
                SetStatus("批量导入失败：" + ex.Message);
            }
        }

        private void ClearCurrentTable(object sender, EventArgs e)
        {
            string tableName = Convert.ToString(_dbTableSelector.SelectedItem);
            DialogResult confirm = MessageBox.Show(
                this,
                "确认清空当前表“" + tableName + "”吗？此操作不可撤销。",
                "确认清空",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            try
            {
                _databaseService.ReplaceIds(tableName, new List<string>());
                LoadDatabase();
                _dbBatchSummaryLabel.Text = tableName + " 已清空。";
                SetStatus("当前表已清空。");
            }
            catch (Exception ex)
            {
                _databaseErrorMessage = ex.Message;
                SetStatus("清空失败：" + ex.Message);
            }
        }

        private void AnalyzePastedIds(object sender, EventArgs e)
        {
            List<string> ids = ParseIdsFromBatchInput();
            _dbBatchSummaryLabel.Text = "粘贴区已识别 " + ids.Count + " 条唯一 ID。";
            SetStatus("已统计批量粘贴区。");
        }

        private void ClearPastedIds(object sender, EventArgs e)
        {
            _dbBatchInputBox.Text = string.Empty;
            _dbBatchSummaryLabel.Text = "批量粘贴区已清空。";
            SetStatus("批量粘贴区已清空。");
        }

        private void NormalizePastedIds(object sender, EventArgs e)
        {
            List<string> ids = ParseIdsFromBatchInput();
            StringBuilder builder = new StringBuilder();
            int i;
            for (i = 0; i < ids.Count; i++)
            {
                builder.AppendLine(ids[i]);
            }

            _dbBatchInputBox.Text = builder.ToString();
            _dbBatchSummaryLabel.Text = "已整理并去重，共 " + ids.Count + " 条。";
            SetStatus("批量粘贴区已整理。");
        }

        private void PasteFromClipboard(object sender, EventArgs e)
        {
            try
            {
                if (!Clipboard.ContainsText())
                {
                    SetStatus("剪贴板里没有文本内容。");
                    return;
                }

                string text = Clipboard.GetText();
                if (string.IsNullOrEmpty(text))
                {
                    SetStatus("剪贴板里没有文本内容。");
                    return;
                }

                if (!string.IsNullOrEmpty(_dbBatchInputBox.Text))
                {
                    _dbBatchInputBox.AppendText(Environment.NewLine);
                }

                _dbBatchInputBox.AppendText(text);
                AnalyzePastedIds(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                SetStatus("读取剪贴板失败：" + ex.Message);
            }
        }

        private void ExportIdsFromTable(object sender, EventArgs e)
        {
            string tableName = Convert.ToString(_dbTableSelector.SelectedItem);
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.FileName = tableName + "-导出.txt";
            dialog.Filter = "文本文件|*.txt|Excel|*.xlsx|所有文件|*.*";

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                List<string> ids = _databaseService.GetAllIds(tableName);
                TableFileService.WriteIds(dialog.FileName, ids);
                _paths.OpenPath(dialog.FileName);
                SetStatus("已从 " + tableName + " 导出 " + ids.Count + " 条。");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private List<string> ParseIdsFromBatchInput()
        {
            string content = _dbBatchInputBox == null ? string.Empty : _dbBatchInputBox.Text;
            string[] raw = content.Replace("\r", "\n").Split(new char[] { '\n', ',', ';', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Dictionary<string, bool> unique = new Dictionary<string, bool>();
            List<string> ids = new List<string>();

            int i;
            for (i = 0; i < raw.Length; i++)
            {
                string value = raw[i].Trim();
                if (string.IsNullOrEmpty(value) || unique.ContainsKey(value))
                {
                    continue;
                }

                unique[value] = true;
                ids.Add(value);
            }

            return ids;
        }

        private async void RunAutoSourcing(object sender, EventArgs e)
        {
            try
            {
                EnsureSnapshot();
                SourcingOptions options = ReadSourcingOptions();
                List<SourcingSeed> seeds = ReadSourcingSeeds();
                if (_browser == null || _browser.CoreWebView2 == null)
                {
                    SetStatus("Please initialize the plugin browser and login to 1688 first.");
                    _mainTabs.SelectedTab = _mainTabs.TabPages[_mainTabs.TabPages.Count - 1];
                    return;
                }

                SetStatus("Running 1688 selection...");
                _lastSourcingResult = await Collect1688CandidatesFromBrowser(seeds, _snapshot.Config, options);
                _autoResultGrid.DataSource = _lastSourcingResult.Products;
                WriteAutomationLog(_lastSourcingResult.Logs);
                SetStatus("1688 selection finished: " + _lastSourcingResult.Products.Count + " candidates.");
            }
            catch (Exception ex)
            {
                AppendAutomationLog("ERROR: " + ex.Message);
                MessageBox.Show(this, ex.ToString(), "Auto sourcing failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Auto sourcing failed: " + ex.Message);
            }
        }

        private async void RunFullAutoLoop(object sender, EventArgs e)
        {
            StringBuilder report = new StringBuilder();
            try
            {
                EnsureSnapshot();
                if (_browser == null || _browser.CoreWebView2 == null)
                {
                    _mainTabs.SelectedIndex = 6;
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
                    CategoryNode category = PickRandomOzonLeafCategory();
                    if (category == null)
                    {
                        report.AppendLine("Round " + (i + 1) + ": no usable Ozon leaf category; stopped.");
                        break;
                    }

                    string categoryKeyword = !string.IsNullOrEmpty(category.DescriptionTypeName) ? category.DescriptionTypeName : category.DescriptionCategoryName;
                    _mainTabs.SelectedIndex = 3;
                    FillAutomationCategory(category.DescriptionCategoryId, category.DescriptionTypeId, categoryKeyword);
                    await Task.Delay(300);

                    report.AppendLine();
                    report.AppendLine("Round " + (i + 1));
                    report.AppendLine("Ozon category: " + category.DescriptionCategoryName + " / " + category.DescriptionTypeName + " [" + category.DescriptionCategoryId + "/" + category.DescriptionTypeId + "]");
                    report.AppendLine("Keyword: " + categoryKeyword);

                    _mainTabs.SelectedIndex = 6;
                    SourcingOptions options = ReadSourcingOptions();
                    List<SourcingSeed> seeds = ReadSourcingSeeds();
                    _lastSourcingResult = await Collect1688CandidatesFromBrowser(seeds, _snapshot.Config, options);
                    _autoResultGrid.DataSource = _lastSourcingResult.Products;
                    WriteAutomationLog(_lastSourcingResult.Logs);
                    report.AppendLine("选品候选：" + _lastSourcingResult.Products.Count + " 个");

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
                            AppendAutomationLog);

                        if (uploadResult.Success)
                        {
                            report.AppendLine("Ozon task submitted: " + uploadProducts.Count + " items, task_id=" + SafeValue(uploadResult.TaskId));

                            OzonImportResult importResult = await Task.Run(() => _automationService.WaitForOzonImportInfo(
                                uploadResult.TaskId,
                                _ozonClientIdBox.Text.Trim(),
                                _ozonApiKeyBox.Text.Trim(),
                                12,
                                10000));

                            report.AppendLine("Ozon import result:");
                            report.AppendLine(importResult.ImportSummary);
                            if (!importResult.Success)
                            {
                                report.AppendLine("Ozon import was not accepted. Check missing attributes/category requirements above.");
                            }
                        }
                        else
                        {
                            report.AppendLine("Ozon upload failed: " + uploadResult.ErrorMessage);
                        }
                    }
                    catch (Exception uploadEx)
                    {
                        report.AppendLine("Ozon upload exception: " + uploadEx.Message);
                    }

                    _fullAutoReport = report.ToString();
                    UpdateOverview();
                }

                report.AppendLine();
                report.AppendLine("结束时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                _fullAutoReport = report.ToString();
                _mainTabs.SelectedIndex = 0;
                UpdateOverview();
                SetStatus("全链路自动循环完成。");
            }
            catch (Exception ex)
            {
                report.AppendLine();
                report.AppendLine("异常：" + ex.Message);
                _fullAutoReport = report.ToString();
                _mainTabs.SelectedIndex = 0;
                UpdateOverview();
                MessageBox.Show(this, ex.ToString(), "全链路自动循环失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("全链路自动循环失败：" + ex.Message);
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
                if (product != null && string.Equals(product.Decision, "Go", StringComparison.OrdinalIgnoreCase))
                {
                    selected.Add(product);
                }
            }

            if (selected.Count == 0 && products.Count > 0 && products[0] != null)
            {
                selected.Add(products[0]);
            }

            return selected;
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
                string url = "https://s.1688.com/selloffer/offer_search.htm?keywords=" + Uri.EscapeDataString(seed.Keyword);
                await NavigateAndWait(url, 6500);
                List<SourceProduct> cards = await ScrapeSearchPage(seed.Keyword, perKeywordLimit);
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
  var imgs = Array.prototype.slice.call(document.querySelectorAll('img'))
    .map(function(img){ return abs(img.currentSrc || img.src || attr(img,'data-src')); })
    .filter(function(url){ return /alicdn|cbu|1688/i.test(url) && !/avatar|logo|icon/i.test(url); });
  var unique = [];
  imgs.forEach(function(url){ if(url && unique.indexOf(url) < 0) unique.push(url.split('?')[0]); });
  var attrs = {};
  Array.prototype.slice.call(document.querySelectorAll('tr, li, dl, .offer-attr, .mod-detail-attributes')).slice(0,80).forEach(function(row){
    var t = text(row);
    var m = t.match(/^(.{2,20})[:：]\s*(.{1,80})$/);
    if(m && Object.keys(attrs).length < 20) attrs[m[1]] = m[2];
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
            if (config != null && config.MinSaleNum > 0 && product.SalesCount > 0 && product.SalesCount < config.MinSaleNum) reasons.Add("sales below config");
            product.Score = Math.Round(score, 2);
            product.Decision = reasons.Count == 0 && score >= 45m ? "Go" : (score >= 30m ? "Watch" : "No-Go");
            product.Reason = reasons.Count == 0 ? "browser scrape signals look usable" : string.Join("; ", reasons.ToArray());
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

        private async void UploadSelectedToOzon(object sender, EventArgs e)
        {
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
                    AppendAutomationLog);

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

        private void CheckOzonTask(object sender, EventArgs e)
        {
            string taskId = PromptDialog.Show(this, "Ozon task", "Input task_id:", Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty);
            if (string.IsNullOrEmpty(taskId))
            {
                return;
            }

            try
            {
                string response = _automationService.GetOzonImportInfo(taskId.Trim(), _ozonClientIdBox.Text.Trim(), _ozonApiKeyBox.Text.Trim());
                AppendAutomationLog("Ozon task info: " + response);
                SetStatus("Ozon task info loaded.");
            }
            catch (Exception ex)
            {
                AppendAutomationLog("OZON TASK ERROR: " + ex.Message);
                MessageBox.Show(this, ex.ToString(), "Ozon task failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportAutoCandidates(object sender, EventArgs e)
        {
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
            return new SourcingOptions
            {
                Provider = Convert.ToString(_autoProviderBox.SelectedItem),
                ApiKey = _autoApiKeyBox.Text.Trim(),
                ApiSecret = _autoApiSecretBox.Text.Trim(),
                PerKeywordLimit = (int)ParseLong(_autoPerKeywordBox.Text),
                DetailLimit = (int)ParseLong(_autoDetailLimitBox.Text),
                RubPerCny = ParseDecimal(_autoRubRateBox.Text),
                OzonCategoryId = ParseLong(_autoCategoryIdBox.Text),
                OzonTypeId = ParseLong(_autoTypeIdBox.Text),
                PriceMultiplier = ParseDecimal(_autoPriceMultiplierBox.Text),
                CurrencyCode = "RUB",
                Vat = "0"
            };
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

                _browserStatusLabel.Text = "浏览器已就绪，1688 插件已挂载。";
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
            }
            catch (Exception ex)
            {
                _browserStatusLabel.Text = ex.Message;
            }
        }

        private void EnsureSnapshot()
        {
            if (_snapshot == null)
            {
                _snapshot = new AssetSnapshot();
            }
        }

        private void ClearDatabaseViews()
        {
            EnsureSnapshot();
            _snapshot.TableCounts = new Dictionary<string, long>();
            _snapshot.TableCounts["SkuTable"] = 0;
            _snapshot.TableCounts["ShopTable"] = 0;
            _snapshot.TableCounts["tb_catch_shop"] = 0;
            _skuGrid.DataSource = null;
            _shopGrid.DataSource = null;
            _catchGrid.DataSource = null;
            _dbSummaryLabel.Text = "数据库未加载：" + _databaseErrorMessage;
            _dbBatchSummaryLabel.Text = "请先修复 SQLite 依赖，再继续导入或查看本地数据库。";
        }

        private void ClearAssetViews()
        {
            EnsureSnapshot();
            _snapshot.Categories = new List<CategoryNode>();
            _snapshot.FeeRules = new List<FeeRule>();
            _categoryTree.Nodes.Clear();
            _feeGrid.DataSource = null;
        }

        private long GetCount(string name)
        {
            if (_snapshot == null || _snapshot.TableCounts == null || !_snapshot.TableCounts.ContainsKey(name))
            {
                return 0;
            }

            return _snapshot.TableCounts[name];
        }

        private FeeRule GetSelectedFeeRule()
        {
            if (_feeGrid.CurrentRow == null)
            {
                return null;
            }

            return _feeGrid.CurrentRow.DataBoundItem as FeeRule;
        }

        private TreeNode BuildTreeNode(CategoryNode source)
        {
            string text = source.DescriptionCategoryName;
            if (!string.IsNullOrEmpty(source.DescriptionTypeName))
            {
                text += " / " + source.DescriptionTypeName;
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
            tab.BackColor = Color.FromArgb(245, 247, 250);
            return tab;
        }

        private FlowLayoutPanel CreateActionBar()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Top;
            panel.Height = 56;
            panel.WrapContents = false;
            panel.AutoScroll = true;
            panel.Padding = new Padding(12, 10, 12, 0);
            panel.BackColor = Color.White;
            return panel;
        }

        private Control CreateStatCard(string title, string value, string description, Color accent, out Label valueLabel)
        {
            Panel card = new Panel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(8);
            card.BackColor = Color.White;
            card.Padding = new Padding(14, 12, 14, 12);

            Panel line = new Panel();
            line.BackColor = accent;
            line.Width = 6;
            line.Dock = DockStyle.Left;

            Label titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.AutoSize = true;
            titleLabel.ForeColor = Color.FromArgb(96, 98, 102);
            titleLabel.Location = new Point(18, 14);

            valueLabel = new Label();
            valueLabel.Text = value;
            valueLabel.AutoSize = true;
            valueLabel.Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold, GraphicsUnit.Point, 134);
            valueLabel.Location = new Point(18, 36);

            Label descriptionLabel = new Label();
            descriptionLabel.Text = description;
            descriptionLabel.AutoSize = false;
            descriptionLabel.Width = 240;
            descriptionLabel.Height = 36;
            descriptionLabel.ForeColor = Color.FromArgb(144, 147, 153);
            descriptionLabel.Location = new Point(18, 78);

            card.Controls.Add(descriptionLabel);
            card.Controls.Add(valueLabel);
            card.Controls.Add(titleLabel);
            card.Controls.Add(line);
            return card;
        }

        private static Control WrapWithGroup(string title, Control inner)
        {
            GroupBox group = new GroupBox();
            group.Text = title;
            group.Dock = DockStyle.Fill;
            group.Padding = new Padding(8, 24, 8, 8);
            group.BackColor = Color.White;
            inner.Dock = DockStyle.Fill;
            group.Controls.Add(inner);
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
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(247, 248, 250);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(48, 49, 51);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(236, 245, 255);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(48, 49, 51);
            return grid;
        }

        private Button CreateButton(string text, EventHandler handler, bool primary)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.Height = 32;
            button.Padding = new Padding(10, 4, 10, 4);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = primary ? Color.FromArgb(64, 158, 255) : Color.FromArgb(220, 223, 230);
            button.BackColor = primary ? Color.FromArgb(64, 158, 255) : Color.White;
            button.ForeColor = primary ? Color.White : Color.FromArgb(48, 49, 51);
            button.Click += handler;
            return button;
        }

        private Label CreateFormLabel(string text, int left, int top, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.Left = left;
            label.Top = top + 6;
            label.Width = width;
            return label;
        }

        private TextBox CreateTextBox(int left, int top, int width, string value)
        {
            TextBox textBox = new TextBox();
            textBox.Left = left;
            textBox.Top = top;
            textBox.Width = width;
            textBox.Text = value;
            return textBox;
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

        private string SafeValue(string text)
        {
            return string.IsNullOrEmpty(text) ? "未找到" : text;
        }

        private string YesNo(bool value)
        {
            return value ? "是" : "否";
        }

        private void SetStatus(string message)
        {
            _statusLabel.Text = message;
        }
    }
}
