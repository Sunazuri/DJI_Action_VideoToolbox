using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DJI_Action_VideoToolbox;

public sealed class MainForm : Form
{
    private const string AppVersion = "1.0.8";
    private const string AppName = "DJI_Action_VideoToolbox_v1.0.8";

    private static readonly string[] EncodeVideoExtensions = { ".mp4", ".mov", ".mkv", ".m4v" };
    private static readonly string[] ConcatVideoExtensions = { ".mp4", ".mkv" };

    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly ConcurrentDictionary<string, EncodeJob> _encodeJobs = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _encodeCts;
    private CancellationTokenSource? _concatCts;
    private bool _encodeRunning;
    private bool _concatRunning;

    // 共通パス
    private TextBox txtFfmpeg = null!;
    private TextBox txtFfprobe = null!;
    private Button btnCommonSave = null!;
    private Label lblGlobalStatus = null!;

    // エンコードタブ
    private TextBox txtEncLut = null!;
    private TextBox txtEncOutput = null!;
    private TextBox txtEncSuffix = null!;
    private ListView lvEncInputs = null!;
    private RichTextBox logEnc = null!;
    private ProgressBar progressEncTotal = null!;
    private Label lblEncStatus = null!;
    private Button btnEncStart = null!;
    private Button btnEncCancel = null!;
    private Button btnEncAutoScan = null!;
    private NumericUpDown numEncParallel = null!;
    private NumericUpDown numEncCq = null!;
    private NumericUpDown numEncBFrames = null!;
    private NumericUpDown numEncLookahead = null!;
    private NumericUpDown numEncAq = null!;
    private CheckBox chkEncApplyLut = null!;
    private CheckBox chkEncHevcNvenc = null!;
    private CheckBox chkEncMain10 = null!;
    private CheckBox chkEncCopyAudio = null!;
    private CheckBox chkEncFastStart = null!;
    private CheckBox chkEncOverwrite = null!;
    private CheckBox chkEncSoftIndoor = null!;
    private CheckBox chkEncRecursive = null!;
    private CheckBox chkEncShutdownAfterComplete = null!;
    private CheckBox chkEncShutdownOnError = null!;
    private NumericUpDown numEncShutdownDelay = null!;
    private ComboBox cmbEncShutdownDelayUnit = null!;
    private ComboBox cmbEncProcessMode = null!;
    private ComboBox cmbEncLutInterpolation = null!;

    // 連結タブ
    private ListBox lstConcatInputs = null!;
    private TextBox txtConcatOutputFolder = null!;
    private TextBox txtConcatOutputFileName = null!;
    private ComboBox cmbConcatMode = null!;
    private CheckBox chkConcatOverwrite = null!;
    private CheckBox chkConcatOpenOutput = null!;
    private CheckBox chkConcatFastStart = null!;
    private CheckBox chkConcatRecursive = null!;
    private ProgressBar progressConcat = null!;
    private TextBox logConcat = null!;
    private Button btnConcatStart = null!;
    private Button btnConcatCancel = null!;
    private Button btnConcatAddFiles = null!;
    private Button btnConcatAddFolder = null!;
    private Button btnConcatRemove = null!;
    private Button btnConcatClear = null!;
    private Button btnConcatUp = null!;
    private Button btnConcatDown = null!;
    private Button btnConcatOpenOutput = null!;

    public MainForm()
    {
        _settingsService = new SettingsService();
        _settings = _settingsService.Load();

        Text = AppName;
        Width = 1220;
        Height = 820;
        MinimumSize = new Size(980, 680);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScroll = false;
        Font = new Font("Yu Gothic UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Icon = LoadWindowIcon();
        AllowDrop = true;
        Shown += (_, _) => ApplyScreenAwareWindowSize();

        BuildUi();
        LoadSettingsIntoUi();
        ApplyScreenAwareWindowSize();

        LogEncode($"{AppName} を起動しました。");
        LogEncode($"設定ファイル: {_settingsService.SettingsPath}");
        LogEncode($"CPU論理プロセッサ数: {Environment.ProcessorCount} / 推奨初期並列数: {AppSettings.ComputeRecommendedParallelJobs()}");
        LogEncode("エンコード入力、LUT、出力先、FFmpeg/ffprobe欄はD&D対応です。");
        LogEncode("v1.0.8: エンコード中でもシャットダウン関連設定を変更できるようにしました。完了直後の最新設定で判定します。");
        LogConcat("動画連結タブを初期化しました。mp4 / mkv のファイルまたはフォルダをD&Dできます。");
        UpdateGlobalStatus("待機中");
    }

    private static Icon? LoadWindowIcon()
    {
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { return null; }
    }

    private void BuildUi()
    {
        // v1.0.8: v1.0.7のシャットダウン機能を、エンコード中でも変更できるリアルタイム反映仕様へ改善。
        // 画面全体をスクロール可能なホストに入れ、作業領域に応じて自動リサイズします。
        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = SystemColors.Control,
        };
        Controls.Add(scrollHost);

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10),
            Location = new Point(0, 0),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            AutoSize = false,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        scrollHost.Controls.Add(root);

        void ResizeRootForHost()
        {
            const int minimumContentWidth = 1040;
            const int minimumContentHeight = 700;
            var width = Math.Max(minimumContentWidth, scrollHost.ClientSize.Width - 4);
            var height = Math.Max(minimumContentHeight, scrollHost.ClientSize.Height - 4);
            root.Size = new Size(width, height);
            scrollHost.AutoScrollMinSize = new Size(minimumContentWidth, minimumContentHeight);
        }

        scrollHost.ClientSizeChanged += (_, _) => ResizeRootForHost();
        ResizeRootForHost();

        root.Controls.Add(BuildCommonPathGroup(), 0, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var concatTab = new TabPage("動画連結 / VideoConcat") { AutoScroll = true };
        var encTab = new TabPage("DJI Action LUT / HEVC エンコード") { AutoScroll = true };
        concatTab.Controls.Add(BuildConcatTab());
        encTab.Controls.Add(BuildEncoderTab());
        // 要望対応：動画連結タブを左、エンコードタブを右へ配置。
        tabs.TabPages.Add(concatTab);
        tabs.TabPages.Add(encTab);
        root.Controls.Add(tabs, 0, 1);

        lblGlobalStatus = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DarkSlateGray,
            AutoEllipsis = true,
        };
        root.Controls.Add(lblGlobalStatus, 0, 2);

        void DropToCurrentTab(string[] paths)
        {
            if (tabs.SelectedTab == encTab) AddEncodeInputPaths(paths);
            else AddConcatInputPaths(paths);
        }

        RegisterInputDrop(this, DropToCurrentTab);
        RegisterInputDrop(scrollHost, DropToCurrentTab);
    }

    private void ApplyScreenAwareWindowSize()
    {
        try
        {
            var work = Screen.FromControl(this).WorkingArea;
            const int desiredWidth = 1220;
            const int desiredHeight = 820;
            const int margin = 48;

            var targetWidth = Math.Min(desiredWidth, Math.Max(980, work.Width - margin));
            var targetHeight = Math.Min(desiredHeight, Math.Max(680, work.Height - margin));

            // Full HD + 125% のように実効作業領域が低くなる環境では、縦を控えめにして下端欠けを避けます。
            if (work.Width <= 1600 && work.Height <= 900)
            {
                targetHeight = Math.Min(targetHeight, Math.Max(680, work.Height - 32));
            }

            Size = new Size(targetWidth, targetHeight);

            if (Right > work.Right || Bottom > work.Bottom || Left < work.Left || Top < work.Top)
            {
                Location = new Point(
                    Math.Max(work.Left, work.Left + (work.Width - Width) / 2),
                    Math.Max(work.Top, work.Top + (work.Height - Height) / 2));
            }
        }
        catch
        {
            // 画面情報取得に失敗しても通常起動は継続します。
        }
    }

    private Control BuildCommonPathGroup()
    {
        var group = new GroupBox { Text = "共通設定：FFmpeg / ffprobe（全欄D&D対応）", Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 2, Padding = new Padding(8) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        txtFfmpeg = CreatePathTextBox(folderOnly: false);
        txtFfprobe = CreatePathTextBox(folderOnly: false);
        var btnFfmpegBrowse = CreateButton("参照", 70, () => BrowseFile(txtFfmpeg, "ffmpeg.exe|ffmpeg.exe|実行ファイル|*.exe|すべて|*.*"));
        var btnFfmpegAuto = CreateButton("自動検出", 90, () => AutoDetectTool(txtFfmpeg, "ffmpeg.exe"));
        var btnFfprobeBrowse = CreateButton("参照", 70, () => BrowseFile(txtFfprobe, "ffprobe.exe|ffprobe.exe|実行ファイル|*.exe|すべて|*.*"));
        btnCommonSave = CreateButton("設定保存", 100, SaveAllSettingsFromUi);

        layout.Controls.Add(CreateRightLabel("ffmpeg.exe"), 0, 0);
        layout.Controls.Add(txtFfmpeg, 1, 0);
        layout.Controls.Add(btnFfmpegBrowse, 2, 0);
        layout.Controls.Add(btnFfmpegAuto, 3, 0);
        layout.Controls.Add(CreateRightLabel("ffprobe.exe"), 4, 0);
        layout.Controls.Add(txtFfprobe, 5, 0);
        layout.Controls.Add(btnFfprobeBrowse, 6, 0);

        var hint = new Label
        {
            Text = "空欄の場合は PATH 上の ffmpeg / ffprobe を使用します。確実性重視なら exe を直接指定してください。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
        };
        layout.Controls.Add(hint, 1, 1);
        layout.SetColumnSpan(hint, 5);
        layout.Controls.Add(btnCommonSave, 6, 1);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildEncoderTab()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(8),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 224));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        root.Controls.Add(BuildEncoderPathGroup(), 0, 0);
        root.Controls.Add(BuildEncoderInputGroup(), 0, 1);
        root.Controls.Add(BuildEncoderOptionsGroup(), 0, 2);
        root.Controls.Add(BuildEncoderActionPanel(), 0, 3);
        root.Controls.Add(BuildEncoderLogGroup(), 0, 4);
        root.Controls.Add(BuildEncoderStatusPanel(), 0, 5);
        return root;
    }

    private Control BuildEncoderPathGroup()
    {
        var group = new GroupBox { Text = "1. DJI Action エンコード用パス（LUT / 出力先）", Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 2, Padding = new Padding(8) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        txtEncLut = CreatePathTextBox(folderOnly: false);
        txtEncOutput = CreatePathTextBox(folderOnly: true);
        txtEncSuffix = new TextBox { Dock = DockStyle.Fill };
        RegisterInputDrop(txtEncSuffix, paths =>
        {
            var file = paths.FirstOrDefault(File.Exists);
            if (file != null) txtEncSuffix.Text = Path.GetFileNameWithoutExtension(file);
        });

        layout.Controls.Add(CreateRightLabel("LUT .cube"), 0, 0);
        layout.Controls.Add(txtEncLut, 1, 0);
        layout.Controls.Add(CreateButton("参照", 70, () => BrowseFile(txtEncLut, "CUBE LUT|*.cube|すべて|*.*")), 2, 0);
        layout.Controls.Add(CreateRightLabel("出力先"), 3, 0);
        layout.Controls.Add(txtEncOutput, 4, 0);
        layout.Controls.Add(CreateButton("参照", 70, () => BrowseFolder(txtEncOutput)), 5, 0);
        layout.Controls.Add(CreateRightLabel("出力接尾辞"), 0, 1);
        layout.Controls.Add(txtEncSuffix, 1, 1);
        layout.SetColumnSpan(txtEncSuffix, 5);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildEncoderInputGroup()
    {
        var group = new GroupBox { Text = "2. エンコード入力動画（ファイル/フォルダD&D対応・複数可）", Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(8) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));

        lvEncInputs = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HideSelection = false,
            AllowDrop = true,
        };
        lvEncInputs.Columns.Add("入力ファイル", 820);
        lvEncInputs.Columns.Add("状態", 110);
        lvEncInputs.Columns.Add("進捗", 80);
        RegisterInputDrop(lvEncInputs, AddEncodeInputPaths);
        layout.Controls.Add(lvEncInputs, 0, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        buttons.Controls.Add(CreateButton("ファイル追加", 120, AddEncodeFilesDialog));
        buttons.Controls.Add(CreateButton("フォルダ追加", 120, AddEncodeFolderDialog));
        buttons.Controls.Add(CreateButton("選択削除", 120, RemoveSelectedEncodeInputs));
        buttons.Controls.Add(CreateButton("全クリア", 120, ClearEncodeInputs));
        layout.Controls.Add(buttons, 1, 0);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildEncoderOptionsGroup()
    {
        var group = new GroupBox { Text = "3. エンコード設定 / 自動走査 / LUT適用モード", Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 7, Padding = new Padding(8) };
        for (int i = 0; i < 6; i++) layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.666F));
        for (int i = 0; i < 7; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        chkEncApplyLut = CreateCheckBox("LUT適用 ON");
        chkEncHevcNvenc = CreateCheckBox("HEVC NVENC");
        chkEncMain10 = CreateCheckBox("10-bit Main10");
        chkEncCopyAudio = CreateCheckBox("音声コピー");
        chkEncFastStart = CreateCheckBox("Web用に最適化");
        chkEncOverwrite = CreateCheckBox("既存出力を上書き");
        chkEncSoftIndoor = CreateCheckBox("室内向け微補正");
        chkEncRecursive = CreateCheckBox("フォルダ再帰追加");
        chkEncShutdownAfterComplete = CreateCheckBox("処理完了後シャットダウン");
        chkEncShutdownAfterComplete.CheckedChanged += (_, _) => UpdateEncodeShutdownUiRules();
        chkEncShutdownOnError = CreateCheckBox("エラー終了時も強制シャットダウン");
        chkEncShutdownOnError.CheckedChanged += (_, _) => UpdateSettingsFromUi();

        layout.Controls.Add(chkEncApplyLut, 0, 0);
        layout.Controls.Add(chkEncHevcNvenc, 1, 0);
        layout.Controls.Add(chkEncMain10, 2, 0);
        layout.Controls.Add(chkEncCopyAudio, 3, 0);
        layout.Controls.Add(chkEncFastStart, 4, 0);
        layout.Controls.Add(chkEncOverwrite, 5, 0);
        layout.Controls.Add(chkEncSoftIndoor, 0, 1);
        layout.Controls.Add(chkEncRecursive, 1, 1);

        numEncParallel = CreateNumeric(1, 8, AppSettings.ComputeRecommendedParallelJobs());
        numEncCq = CreateNumeric(1, 35, 20);
        numEncBFrames = CreateNumeric(0, 8, 4);
        numEncLookahead = CreateNumeric(0, 64, 32);
        numEncAq = CreateNumeric(0, 15, 10);
        numEncShutdownDelay = CreateNumeric(1, 1440, 30);
        cmbEncShutdownDelayUnit = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbEncShutdownDelayUnit.Items.Add("秒");
        cmbEncShutdownDelayUnit.Items.Add("分");
        AddLabeledControl(layout, 2, 1, "並列数", numEncParallel);
        AddLabeledControl(layout, 3, 1, "CQ", numEncCq);
        AddLabeledControl(layout, 4, 1, "Bフレーム", numEncBFrames);
        AddLabeledControl(layout, 5, 1, "Lookahead", numEncLookahead);
        AddLabeledControl(layout, 0, 2, "AQ強度", numEncAq);

        cmbEncProcessMode = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbEncProcessMode.Items.Add("標準：LUT + HEVC/NVENC エンコード");
        cmbEncProcessMode.Items.Add("LUTのみ適用：品質優先");
        cmbEncProcessMode.Items.Add("高速LUT適用：trilinear + NVENC p1");
        cmbEncProcessMode.Items.Add("最速LUT適用：nearest + NVENC p1");
        cmbEncProcessMode.Items.Add("HEVC変換のみ：LUTなし");
        cmbEncProcessMode.SelectedIndexChanged += (_, _) => ApplyEncoderProcessModeUiRules();
        AddLabeledControl(layout, 1, 2, "処理モード", cmbEncProcessMode, 5);

        cmbEncLutInterpolation = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbEncLutInterpolation.Items.Add("trilinear 推奨");
        cmbEncLutInterpolation.Items.Add("nearest 最速");
        cmbEncLutInterpolation.Items.Add("tetrahedral 高品質");
        AddLabeledControl(layout, 0, 3, "LUT補間方式", cmbEncLutInterpolation, 3);

        layout.Controls.Add(chkEncShutdownAfterComplete, 0, 4);
        layout.SetColumnSpan(chkEncShutdownAfterComplete, 2);
        AddLabeledControl(layout, 2, 4, "待機時間", numEncShutdownDelay);
        AddLabeledControl(layout, 3, 4, "単位", cmbEncShutdownDelayUnit);
        layout.Controls.Add(chkEncShutdownOnError, 4, 4);
        layout.SetColumnSpan(chkEncShutdownOnError, 2);

        var hint = new Label
        {
            Dock = DockStyle.Fill,
            Text = "重要: LUTは映像フィルタのため -c:v copy では適用できません。高速/最速モードはLUT補間とNVENC設定を速度優先へ固定します。",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DarkSlateGray,
        };
        layout.Controls.Add(hint, 0, 5);
        layout.SetColumnSpan(hint, 6);

        var hint2 = new Label
        {
            Dock = DockStyle.Fill,
            Text = "自動走査: 選択中の動画をffprobeで解析します。シャットダウン関連設定はエンコード中も変更でき、完了直後の最新状態で判定します。",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
        };
        layout.Controls.Add(hint2, 0, 6);
        layout.SetColumnSpan(hint2, 6);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildEncoderActionPanel()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(2) };
        btnEncStart = CreateButton("エンコード開始", 150, async () => await StartEncodingAsync());
        btnEncCancel = CreateButton("キャンセル", 120, CancelEncoding);
        btnEncCancel.Enabled = false;
        btnEncAutoScan = CreateButton("自動走査", 120, async () => await AutoScanAsync());
        panel.Controls.Add(btnEncStart);
        panel.Controls.Add(btnEncCancel);
        panel.Controls.Add(btnEncAutoScan);
        panel.Controls.Add(CreateButton("設定保存", 120, SaveAllSettingsFromUi));
        panel.Controls.Add(CreateButton("ログ保存", 120, SaveEncodeLogDialog));
        panel.Controls.Add(CreateButton("コマンド表示", 130, ShowFirstEncodeCommand));
        return panel;
    }

    private Control BuildEncoderLogGroup()
    {
        var group = new GroupBox { Text = "エンコードログ", Dock = DockStyle.Fill };
        logEnc = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            WordWrap = false,
            Font = new Font("Consolas", 9F),
            BackColor = Color.White,
        };
        group.Controls.Add(logEnc);
        return group;
    }

    private Control BuildEncoderStatusPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        lblEncStatus = new Label { Text = "待機中", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        progressEncTotal = new ProgressBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100 };
        panel.Controls.Add(lblEncStatus, 0, 0);
        panel.Controls.Add(progressEncTotal, 1, 0);
        return panel;
    }

    private Control BuildConcatTab()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(8),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));

        root.Controls.Add(BuildConcatInputGroup(), 0, 0);
        root.Controls.Add(BuildConcatOutputGroup(), 0, 1);
        root.Controls.Add(BuildConcatOptionsGroup(), 0, 2);
        root.Controls.Add(BuildConcatActionPanel(), 0, 3);
        root.Controls.Add(BuildConcatLogGroup(), 0, 4);
        return root;
    }

    private Control BuildConcatInputGroup()
    {
        var group = new GroupBox { Text = "1. 連結する動画ファイル（mp4 / mkv・順番は上から下）", Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(8) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));

        lstConcatInputs = new ListBox
        {
            Dock = DockStyle.Fill,
            HorizontalScrollbar = true,
            SelectionMode = SelectionMode.MultiExtended,
            AllowDrop = true,
        };
        RegisterInputDrop(lstConcatInputs, AddConcatInputPaths);
        layout.Controls.Add(lstConcatInputs, 0, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        btnConcatAddFiles = CreateButton("ファイル追加", 120, AddConcatFilesDialog);
        btnConcatAddFolder = CreateButton("フォルダ追加", 120, AddConcatFolderDialog);
        btnConcatUp = CreateButton("上へ", 120, () => MoveSelectedConcat(-1));
        btnConcatDown = CreateButton("下へ", 120, () => MoveSelectedConcat(1));
        btnConcatRemove = CreateButton("選択削除", 120, RemoveSelectedConcatInputs);
        btnConcatClear = CreateButton("全クリア", 120, () => lstConcatInputs.Items.Clear());
        buttons.Controls.AddRange(new Control[] { btnConcatAddFiles, btnConcatAddFolder, btnConcatUp, btnConcatDown, btnConcatRemove, btnConcatClear });
        layout.Controls.Add(buttons, 1, 0);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildConcatOutputGroup()
    {
        var group = new GroupBox { Text = "2. 連結出力先（フォルダ欄・ファイル名欄D&D対応）", Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Padding = new Padding(8) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        txtConcatOutputFolder = CreatePathTextBox(folderOnly: true);
        txtConcatOutputFileName = new TextBox { Dock = DockStyle.Fill, AllowDrop = true };
        RegisterInputDrop(txtConcatOutputFileName, paths =>
        {
            var file = paths.FirstOrDefault(File.Exists);
            if (file == null) return;
            txtConcatOutputFolder.Text = Path.GetDirectoryName(file) ?? txtConcatOutputFolder.Text;
            txtConcatOutputFileName.Text = Path.GetFileName(file);
        });

        layout.Controls.Add(CreateRightLabel("出力フォルダ"), 0, 0);
        layout.Controls.Add(txtConcatOutputFolder, 1, 0);
        layout.Controls.Add(CreateButton("参照", 70, () => BrowseFolder(txtConcatOutputFolder)), 2, 0);
        layout.Controls.Add(CreateRightLabel("出力ファイル名"), 0, 1);
        layout.Controls.Add(txtConcatOutputFileName, 1, 1);
        layout.SetColumnSpan(txtConcatOutputFileName, 2);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildConcatOptionsGroup()
    {
        var group = new GroupBox { Text = "3. 連結方式 / オプション", Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2, Padding = new Padding(8) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        cmbConcatMode = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbConcatMode.Items.Add("高速・無劣化連結（-c copy / 同一仕様の動画向け）");
        cmbConcatMode.Items.Add("互換重視連結（H.264 + AAC へ再エンコード）");
        chkConcatOverwrite = CreateCheckBox("既存ファイルを上書き");
        chkConcatOpenOutput = CreateCheckBox("完了後に出力フォルダを開く");
        chkConcatFastStart = CreateCheckBox(".mp4 はWeb最適化");
        chkConcatRecursive = CreateCheckBox("フォルダ再帰追加");
        layout.Controls.Add(cmbConcatMode, 0, 0);
        layout.Controls.Add(chkConcatOverwrite, 1, 0);
        layout.Controls.Add(chkConcatOpenOutput, 2, 0);
        layout.Controls.Add(chkConcatFastStart, 3, 0);
        layout.Controls.Add(chkConcatRecursive, 4, 0);
        progressConcat = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Blocks, Minimum = 0, Maximum = 100 };
        layout.Controls.Add(progressConcat, 0, 1);
        layout.SetColumnSpan(progressConcat, 5);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildConcatActionPanel()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(2) };
        btnConcatStart = CreateButton("連結開始", 140, async () => await StartConcatAsync());
        btnConcatCancel = CreateButton("キャンセル", 120, () => _concatCts?.Cancel());
        btnConcatCancel.Enabled = false;
        btnConcatOpenOutput = CreateButton("出力フォルダを開く", 150, () => OpenFolder(txtConcatOutputFolder.Text));
        panel.Controls.Add(btnConcatStart);
        panel.Controls.Add(btnConcatCancel);
        panel.Controls.Add(CreateButton("設定保存", 120, SaveAllSettingsFromUi));
        panel.Controls.Add(btnConcatOpenOutput);
        panel.Controls.Add(CreateButton("ログ保存", 120, SaveConcatLogDialog));
        panel.Controls.Add(CreateButton("コマンド表示", 130, ShowConcatCommand));
        return panel;
    }

    private Control BuildConcatLogGroup()
    {
        var group = new GroupBox { Text = "連結ログ", Dock = DockStyle.Fill };
        logConcat = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            ReadOnly = true,
            Font = new Font("Consolas", 9F),
        };
        group.Controls.Add(logConcat);
        return group;
    }

    private Label CreateRightLabel(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };

    private CheckBox CreateCheckBox(string text)
        => new()
        {
            Text = text,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };

    private Button CreateButton(string text, int width, Action action)
    {
        var button = CreateBaseButton(text, width);
        button.Click += (_, _) => action();
        return button;
    }

    private Button CreateButton(string text, int width, Func<Task> action)
    {
        var button = CreateBaseButton(text, width);
        button.Click += async (_, _) =>
        {
            try
            {
                await action().ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // ユーザー操作によるキャンセルは正常系として扱います。
            }
            catch (Exception ex)
            {
                ShowUiException(text, ex);
            }
        };
        return button;
    }

    private Button CreateBaseButton(string text, int width)
        => new()
        {
            Text = text,
            Width = width,
            Height = 28,
            MinimumSize = new Size(width, 28),
            Margin = new Padding(2),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true,
            UseVisualStyleBackColor = true,
        };

    private void ShowUiException(string actionName, Exception ex)
    {
        var detail = ex.ToString();
        var message = $"{actionName} の処理中にエラーが発生しました。" + Environment.NewLine + Environment.NewLine + $"{ex.GetType().Name}: {ex.Message}";
        try
        {
            if (logEnc != null) LogEncode("[UI例外] " + actionName + Environment.NewLine + detail);
            if (logConcat != null) LogConcat("[UI例外] " + actionName + Environment.NewLine + detail);
            UpdateGlobalStatus("エラー: " + actionName);
        }
        catch { }
        MessageBox.Show(this, message, "処理エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private NumericUpDown CreateNumeric(int min, int max, int value)
        => new() { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Dock = DockStyle.Fill };

    private void AddLabeledControl(TableLayoutPanel panel, int col, int row, string label, Control control, int span = 1)
    {
        var wrap = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        wrap.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        wrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        wrap.Controls.Add(CreateRightLabel(label), 0, 0);
        wrap.Controls.Add(control, 1, 0);
        panel.Controls.Add(wrap, col, row);
        if (span > 1) panel.SetColumnSpan(wrap, span);
    }

    private TextBox CreatePathTextBox(bool folderOnly)
    {
        var box = new TextBox { Dock = DockStyle.Fill, AllowDrop = true };
        RegisterInputDrop(box, paths =>
        {
            if (paths.Length == 0) return;
            var chosen = paths[0];
            if (folderOnly && File.Exists(chosen)) chosen = Path.GetDirectoryName(chosen) ?? chosen;
            box.Text = chosen;
        });
        return box;
    }

    private void RegisterInputDrop(Control control, Action<string[]> onDrop)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) =>
        {
            e.Effect = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        };
        control.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) onDrop(paths);
        };
    }

    private void BrowseFile(TextBox target, string filter)
    {
        using var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
    }

    private void BrowseFolder(TextBox target)
    {
        using var dialog = new FolderBrowserDialog { UseDescriptionForTitle = true, Description = "フォルダを選択してください" };
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
    }

    private void AutoDetectTool(TextBox target, string exeName)
    {
        var found = FindExecutable(exeName);
        if (found == null)
        {
            MessageBox.Show(this, $"{exeName} を自動検出できませんでした。参照ボタンまたはD&Dで指定してください。", "未検出", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        target.Text = found;
        LogEncode($"{exeName} 自動検出: {found}");
        LogConcat($"{exeName} 自動検出: {found}");
    }

    private static string? FindExecutable(string exeName)
    {
        var candidates = new List<string>();
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { candidates.Add(Path.Combine(dir.Trim(), exeName)); } catch { }
        }
        candidates.Add(Path.Combine(@"C:\ffmpeg\bin", exeName));
        candidates.Add(Path.Combine(@"C:\Program Files\ffmpeg\bin", exeName));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, exeName));
        return candidates.FirstOrDefault(File.Exists);
    }

    private void LoadSettingsIntoUi()
    {
        txtFfmpeg.Text = _settings.FfmpegPath;
        txtFfprobe.Text = _settings.FfprobePath;

        txtEncLut.Text = _settings.EncoderLutPath;
        txtEncOutput.Text = _settings.EncoderOutputDirectory;
        txtEncSuffix.Text = _settings.EncoderOutputSuffix;
        chkEncApplyLut.Checked = _settings.EncoderApplyLut;
        chkEncHevcNvenc.Checked = _settings.EncoderUseHevcNvenc;
        chkEncMain10.Checked = _settings.EncoderUseMain10;
        chkEncCopyAudio.Checked = _settings.EncoderCopyAudio;
        chkEncFastStart.Checked = _settings.EncoderWebOptimized;
        chkEncOverwrite.Checked = _settings.EncoderOverwrite;
        chkEncSoftIndoor.Checked = _settings.EncoderSoftIndoorCorrection;
        chkEncRecursive.Checked = _settings.EncoderRecursiveFolderAdd;
        numEncParallel.Value = Clamp(_settings.EncoderMaxParallelJobs, (int)numEncParallel.Minimum, (int)numEncParallel.Maximum);
        numEncCq.Value = Clamp(_settings.EncoderCq, (int)numEncCq.Minimum, (int)numEncCq.Maximum);
        numEncBFrames.Value = Clamp(_settings.EncoderBFrames, (int)numEncBFrames.Minimum, (int)numEncBFrames.Maximum);
        numEncLookahead.Value = Clamp(_settings.EncoderLookahead, (int)numEncLookahead.Minimum, (int)numEncLookahead.Maximum);
        numEncAq.Value = Clamp(_settings.EncoderAqStrength, (int)numEncAq.Minimum, (int)numEncAq.Maximum);
        cmbEncProcessMode.SelectedIndex = Math.Clamp(_settings.EncoderProcessModeIndex, 0, Math.Max(0, cmbEncProcessMode.Items.Count - 1));
        cmbEncLutInterpolation.SelectedIndex = Math.Clamp(_settings.EncoderLutInterpolationIndex, 0, Math.Max(0, cmbEncLutInterpolation.Items.Count - 1));
        chkEncShutdownAfterComplete.Checked = _settings.EncoderShutdownAfterComplete;
        chkEncShutdownOnError.Checked = _settings.EncoderShutdownOnError;
        numEncShutdownDelay.Value = Clamp(_settings.EncoderShutdownDelayValue, (int)numEncShutdownDelay.Minimum, (int)numEncShutdownDelay.Maximum);
        cmbEncShutdownDelayUnit.SelectedIndex = Math.Clamp(_settings.EncoderShutdownDelayUnitIndex, 0, Math.Max(0, cmbEncShutdownDelayUnit.Items.Count - 1));
        ApplyEncoderProcessModeUiRules(updateSuffix: false);
        UpdateEncodeShutdownUiRules();

        txtConcatOutputFolder.Text = _settings.ConcatOutputFolder;
        txtConcatOutputFileName.Text = string.IsNullOrWhiteSpace(_settings.ConcatOutputFileName) ? "joined_video.mkv" : _settings.ConcatOutputFileName;
        chkConcatOverwrite.Checked = _settings.ConcatOverwrite;
        chkConcatOpenOutput.Checked = _settings.ConcatOpenOutputFolderAfterFinish;
        chkConcatFastStart.Checked = _settings.ConcatAddFastStartForMp4;
        chkConcatRecursive.Checked = _settings.ConcatRecursiveFolderAdd;
        cmbConcatMode.SelectedIndex = Math.Clamp(_settings.ConcatModeIndex, 0, Math.Max(0, cmbConcatMode.Items.Count - 1));
    }

    private void UpdateSettingsFromUi()
    {
        _settings.FfmpegPath = txtFfmpeg.Text.Trim().Trim('"');
        _settings.FfprobePath = txtFfprobe.Text.Trim().Trim('"');

        _settings.EncoderLutPath = txtEncLut.Text.Trim().Trim('"');
        _settings.EncoderOutputDirectory = txtEncOutput.Text.Trim().Trim('"');
        _settings.EncoderOutputSuffix = txtEncSuffix.Text.Trim();
        _settings.EncoderApplyLut = chkEncApplyLut.Checked;
        _settings.EncoderUseHevcNvenc = chkEncHevcNvenc.Checked;
        _settings.EncoderUseMain10 = chkEncMain10.Checked;
        _settings.EncoderCopyAudio = chkEncCopyAudio.Checked;
        _settings.EncoderWebOptimized = chkEncFastStart.Checked;
        _settings.EncoderOverwrite = chkEncOverwrite.Checked;
        _settings.EncoderSoftIndoorCorrection = chkEncSoftIndoor.Checked;
        _settings.EncoderRecursiveFolderAdd = chkEncRecursive.Checked;
        _settings.EncoderMaxParallelJobs = (int)numEncParallel.Value;
        _settings.EncoderCq = (int)numEncCq.Value;
        _settings.EncoderBFrames = (int)numEncBFrames.Value;
        _settings.EncoderLookahead = (int)numEncLookahead.Value;
        _settings.EncoderAqStrength = (int)numEncAq.Value;
        _settings.EncoderProcessModeIndex = Math.Clamp(cmbEncProcessMode.SelectedIndex, 0, 4);
        _settings.EncoderLutInterpolationIndex = Math.Clamp(cmbEncLutInterpolation.SelectedIndex, 0, 2);
        _settings.EncoderShutdownAfterComplete = chkEncShutdownAfterComplete.Checked;
        _settings.EncoderShutdownOnError = chkEncShutdownOnError.Checked;
        _settings.EncoderShutdownDelayValue = (int)numEncShutdownDelay.Value;
        _settings.EncoderShutdownDelayUnitIndex = Math.Clamp(cmbEncShutdownDelayUnit.SelectedIndex, 0, 1);

        _settings.ConcatOutputFolder = txtConcatOutputFolder.Text.Trim().Trim('"');
        _settings.ConcatOutputFileName = txtConcatOutputFileName.Text.Trim();
        _settings.ConcatOverwrite = chkConcatOverwrite.Checked;
        _settings.ConcatOpenOutputFolderAfterFinish = chkConcatOpenOutput.Checked;
        _settings.ConcatAddFastStartForMp4 = chkConcatFastStart.Checked;
        _settings.ConcatRecursiveFolderAdd = chkConcatRecursive.Checked;
        _settings.ConcatModeIndex = Math.Max(0, cmbConcatMode.SelectedIndex);
    }

    private int CurrentEncoderProcessMode => Math.Clamp(cmbEncProcessMode?.SelectedIndex ?? _settings.EncoderProcessModeIndex, 0, 4);

    private static bool IsKnownDefaultEncoderSuffix(string suffix)
    {
        return string.Equals(suffix, "_DLogM_to_Rec709_HEVC", StringComparison.OrdinalIgnoreCase)
            || string.Equals(suffix, "_DLogM_to_Rec709_LUT_QUALITY", StringComparison.OrdinalIgnoreCase)
            || string.Equals(suffix, "_DLogM_to_Rec709_LUT_FAST", StringComparison.OrdinalIgnoreCase)
            || string.Equals(suffix, "_DLogM_to_Rec709_LUT_FASTEST", StringComparison.OrdinalIgnoreCase)
            || string.Equals(suffix, "_DLogM_to_Rec709_LUT_ONLY", StringComparison.OrdinalIgnoreCase)
            || string.Equals(suffix, "_HEVC", StringComparison.OrdinalIgnoreCase);
    }

    private string GetDefaultEncoderSuffixForMode()
    {
        return CurrentEncoderProcessMode switch
        {
            1 => "_DLogM_to_Rec709_LUT_QUALITY",
            2 => "_DLogM_to_Rec709_LUT_FAST",
            3 => "_DLogM_to_Rec709_LUT_FASTEST",
            4 => "_HEVC",
            _ => "_DLogM_to_Rec709_HEVC",
        };
    }

    private void ApplyEncoderProcessModeUiRules(bool updateSuffix = true)
    {
        if (cmbEncProcessMode == null || chkEncApplyLut == null) return;
        var mode = CurrentEncoderProcessMode;
        var isQualityLutOnly = mode == 1;
        var isFastLut = mode == 2;
        var isFastestLut = mode == 3;
        var isHevcOnly = mode == 4;

        if (isQualityLutOnly || isFastLut || isFastestLut)
        {
            chkEncApplyLut.Checked = true;
            chkEncCopyAudio.Checked = true;
            chkEncSoftIndoor.Checked = false;
            chkEncHevcNvenc.Checked = true;
        }
        else if (isHevcOnly)
        {
            chkEncApplyLut.Checked = false;
        }
        else
        {
            chkEncApplyLut.Checked = true;
        }

        if (cmbEncLutInterpolation != null)
        {
            if (isQualityLutOnly)
            {
                cmbEncLutInterpolation.Enabled = true;
                if (updateSuffix) cmbEncLutInterpolation.SelectedIndex = 2; // tetrahedral 高品質
            }
            else if (isFastLut)
            {
                cmbEncLutInterpolation.SelectedIndex = 0; // trilinear 推奨
                cmbEncLutInterpolation.Enabled = false;
            }
            else if (isFastestLut)
            {
                cmbEncLutInterpolation.SelectedIndex = 1; // nearest 最速
                cmbEncLutInterpolation.Enabled = false;
            }
            else if (isHevcOnly)
            {
                cmbEncLutInterpolation.Enabled = false;
            }
            else
            {
                cmbEncLutInterpolation.Enabled = true;
            }
        }

        if (updateSuffix && txtEncSuffix != null)
        {
            var current = txtEncSuffix.Text.Trim();
            if (string.IsNullOrWhiteSpace(current) || IsKnownDefaultEncoderSuffix(current))
            {
                txtEncSuffix.Text = GetDefaultEncoderSuffixForMode();
            }
        }
    }

    private void UpdateEncodeShutdownUiRules()
    {
        if (chkEncShutdownAfterComplete == null || numEncShutdownDelay == null || cmbEncShutdownDelayUnit == null || chkEncShutdownOnError == null) return;

        // v1.0.8: シャットダウン関連UIは、エンコード中でもリアルタイム変更を許可します。
        // OFF時だけ待機時間とエラー時強制シャットダウンを無効化します。
        var enabled = chkEncShutdownAfterComplete.Checked;
        numEncShutdownDelay.Enabled = enabled;
        cmbEncShutdownDelayUnit.Enabled = enabled;
        chkEncShutdownOnError.Enabled = enabled;

        if (!enabled && chkEncShutdownOnError.Checked)
        {
            chkEncShutdownOnError.Checked = false;
        }
    }

    private void SaveAllSettingsFromUi()
    {
        UpdateSettingsFromUi();
        _settingsService.Save(_settings);
        LogEncode("設定保存: " + _settingsService.SettingsPath);
        LogConcat("設定保存: " + _settingsService.SettingsPath);
        UpdateGlobalStatus("設定を保存しました");
    }

    private void AddEncodeFilesDialog()
    {
        using var dialog = new OpenFileDialog { Filter = "動画ファイル|*.mp4;*.mov;*.mkv;*.m4v|すべて|*.*", Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) AddEncodeInputPaths(dialog.FileNames);
    }

    private void AddEncodeFolderDialog()
    {
        using var dialog = new FolderBrowserDialog { UseDescriptionForTitle = true, Description = "動画を含むフォルダを選択してください" };
        if (dialog.ShowDialog(this) == DialogResult.OK) AddEncodeInputPaths(new[] { dialog.SelectedPath });
    }

    private void AddEncodeInputPaths(IEnumerable<string> paths)
    {
        UpdateSettingsFromUi();
        int added = 0;
        foreach (var path in ExpandVideoPaths(paths, EncodeVideoExtensions, _settings.EncoderRecursiveFolderAdd))
        {
            if (_encodeJobs.ContainsKey(path)) continue;
            var job = new EncodeJob { InputPath = path };
            _encodeJobs[path] = job;
            var item = new ListViewItem(new[] { path, "待機", "0%" }) { Tag = job };
            lvEncInputs.Items.Add(item);
            added++;
        }
        LogEncode($"入力追加: {added} 件");
    }

    private static IEnumerable<string> ExpandVideoPaths(IEnumerable<string> paths, IReadOnlyCollection<string> extensions, bool recursive)
    {
        foreach (var raw in paths.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var path = raw.Trim('"');
            if (File.Exists(path) && extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            {
                yield return Path.GetFullPath(path);
            }
            else if (Directory.Exists(path))
            {
                var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                List<string> files;
                try
                {
                    files = Directory.EnumerateFiles(path, "*.*", option)
                        .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                        .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                }
                catch
                {
                    files = new List<string>();
                }
                foreach (var file in files) yield return Path.GetFullPath(file);
            }
        }
    }

    private void RemoveSelectedEncodeInputs()
    {
        if (_encodeRunning) return;
        foreach (ListViewItem item in lvEncInputs.SelectedItems)
        {
            if (item.Tag is EncodeJob job) _encodeJobs.TryRemove(job.InputPath, out _);
            lvEncInputs.Items.Remove(item);
        }
    }

    private void ClearEncodeInputs()
    {
        if (_encodeRunning) return;
        _encodeJobs.Clear();
        lvEncInputs.Items.Clear();
        progressEncTotal.Value = 0;
        lblEncStatus.Text = "待機中";
    }

    private async Task AutoScanAsync()
    {
        if (_encodeRunning) return;
        UpdateSettingsFromUi();
        var item = GetSelectedOrFirstEncodeItem();
        if (item == null || item.Tag is not EncodeJob job)
        {
            MessageBox.Show(this, "自動走査する入力動画がありません。動画を追加してから実行してください。", "自動走査", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        btnEncAutoScan.Enabled = false;
        lblEncStatus.Text = "自動走査中...";
        UpdateGlobalStatus("自動走査中");
        LogEncode("自動走査開始: " + job.InputPath);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var info = await ProbeVideoInfoAsync(job.InputPath, cts.Token);
            var rec = CalculateRecommendedSettings(info);
            SafeUi(() =>
            {
                numEncCq.Value = rec.Cq;
                numEncBFrames.Value = rec.BFrames;
                numEncLookahead.Value = rec.Lookahead;
                numEncAq.Value = rec.AqStrength;
                lblEncStatus.Text = "自動走査完了";
            });
            LogEncode($"自動走査結果: {info.Width}x{info.Height} / {info.Fps:0.###}fps / bitrate={FormatBitrate(info.Bitrate)} / codec={info.CodecName} / pix_fmt={info.PixelFormat}");
            LogEncode($"推奨値入力: CQ={rec.Cq}, Bフレーム={rec.BFrames}, Lookahead={rec.Lookahead}, AQ強度={rec.AqStrength}");
            LogEncode("根拠: 解像度・fps・平均ビットレート・bpppf(bits/pixel/frame)から、Action 4系のD-Log M/HEVC素材向けに保守的に算出しています。");
            SaveAllSettingsFromUi();
        }
        catch (Exception ex)
        {
            SafeUi(() => lblEncStatus.Text = "自動走査失敗");
            LogEncode("自動走査失敗: " + ex.Message);
            MessageBox.Show(this, ex.Message, "自動走査失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SafeUi(() =>
            {
                btnEncAutoScan.Enabled = true;
                UpdateGlobalStatus("待機中");
            });
        }
    }

    private ListViewItem? GetSelectedOrFirstEncodeItem()
    {
        if (lvEncInputs.SelectedItems.Count > 0) return lvEncInputs.SelectedItems[0];
        return lvEncInputs.Items.Count > 0 ? lvEncInputs.Items[0] : null;
    }

    private async Task<VideoInfo> ProbeVideoInfoAsync(string input, CancellationToken token)
    {
        var psi = new ProcessStartInfo
        {
            FileName = GetFfprobeExecutable(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-select_streams"); psi.ArgumentList.Add("v:0");
        psi.ArgumentList.Add("-show_entries"); psi.ArgumentList.Add("stream=codec_name,width,height,avg_frame_rate,r_frame_rate,pix_fmt,bits_per_raw_sample,bit_rate,color_space,color_transfer,color_primaries:format=duration,bit_rate,size");
        psi.ArgumentList.Add("-of"); psi.ArgumentList.Add("json");
        psi.ArgumentList.Add(input);

        using var p = new Process { StartInfo = psi };
        try { p.Start(); }
        catch (Exception ex) { throw new InvalidOperationException("ffprobe の起動に失敗しました。ffprobe.exe のパス、または PATH 設定を確認してください。" + Environment.NewLine + ex.Message, ex); }

        var jsonTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync(token).ConfigureAwait(false);
        var json = await jsonTask.ConfigureAwait(false);
        var err = await errTask.ConfigureAwait(false);
        if (p.ExitCode != 0) throw new InvalidOperationException("ffprobe が異常終了しました。" + Environment.NewLine + err);

        var info = new VideoInfo { InputPath = input };
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array && streams.GetArrayLength() > 0)
        {
            var s = streams[0];
            info.CodecName = GetString(s, "codec_name");
            info.Width = GetInt(s, "width");
            info.Height = GetInt(s, "height");
            info.PixelFormat = GetString(s, "pix_fmt");
            info.BitsPerRawSample = GetInt(s, "bits_per_raw_sample");
            info.StreamBitrate = GetLong(s, "bit_rate");
            info.Fps = ParseFrameRate(GetString(s, "avg_frame_rate"));
            if (info.Fps <= 0) info.Fps = ParseFrameRate(GetString(s, "r_frame_rate"));
            info.ColorSpace = GetString(s, "color_space");
            info.ColorTransfer = GetString(s, "color_transfer");
            info.ColorPrimaries = GetString(s, "color_primaries");
        }
        if (doc.RootElement.TryGetProperty("format", out var f))
        {
            info.Duration = GetDouble(f, "duration");
            info.FormatBitrate = GetLong(f, "bit_rate");
            info.FileSize = GetLong(f, "size");
        }
        if (info.FileSize <= 0)
        {
            try { info.FileSize = new FileInfo(input).Length; } catch { }
        }
        if (info.Bitrate <= 0 && info.Duration > 0 && info.FileSize > 0) info.EstimatedBitrateFromFile = (long)(info.FileSize * 8.0 / info.Duration);
        return info;
    }

    private static RecommendedEncodeSettings CalculateRecommendedSettings(VideoInfo info)
    {
        int longSide = Math.Max(info.Width, info.Height);
        double fps = info.Fps > 0 ? info.Fps : 59.94;
        long bitrate = info.Bitrate;
        double bpppf = 0;
        if (info.Width > 0 && info.Height > 0 && fps > 0 && bitrate > 0) bpppf = bitrate / (info.Width * (double)info.Height * fps);

        int cq;
        int bf;
        int la;
        int aq;
        if (longSide >= 3800) { cq = 20; bf = 4; la = 32; aq = 10; }
        else if (longSide >= 2500) { cq = 20; bf = 4; la = 30; aq = 9; }
        else { cq = 21; bf = 3; la = 24; aq = 8; }

        if (fps >= 100)
        {
            cq = Math.Min(cq, 20);
            bf = Math.Min(bf, 3);
            la = Math.Max(24, la);
            aq = Math.Max(aq, 9);
        }
        else if (fps >= 55)
        {
            bf = Math.Max(bf, 4);
            la = Math.Max(32, la);
        }
        else if (fps <= 30)
        {
            bf = Math.Max(4, bf);
            la = Math.Max(32, la);
        }

        if (bpppf >= 0.22)
        {
            cq = Math.Max(18, cq - 1);
            la = Math.Max(40, la);
            aq = Math.Min(12, aq + 1);
        }
        else if (bpppf > 0 && bpppf < 0.09)
        {
            cq = Math.Min(23, cq + 1);
            aq = Math.Max(8, aq - 1);
        }

        if (longSide >= 3800 && fps >= 55)
        {
            cq = Math.Min(cq, 19);
            bf = 4;
            la = Math.Max(40, la);
            aq = Math.Max(aq, 10);
        }

        la = Math.Max(la, bf + 1);
        return new RecommendedEncodeSettings
        {
            Cq = Math.Clamp(cq, 1, 35),
            BFrames = Math.Clamp(bf, 0, 8),
            Lookahead = Math.Clamp(la, 0, 64),
            AqStrength = Math.Clamp(aq, 0, 15),
        };
    }

    private async Task StartEncodingAsync()
    {
        if (_encodeRunning) return;

        CancellationTokenSource? cts = null;
        try
        {
            UpdateSettingsFromUi();
            _settingsService.Save(_settings);

            var validation = ValidateEncoderBeforeStart();
            if (validation.Length > 0)
            {
                MessageBox.Show(this, validation.ToString(), "開始できません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var outputDirectory = _settings.EncoderOutputDirectory ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            cts = new CancellationTokenSource();
            _encodeCts = cts;
            var token = cts.Token;
            _encodeRunning = true;
            SetEncoderRunning(true);
            progressEncTotal.Value = 0;
            UpdateGlobalStatus("エンコード中");

            var jobs = lvEncInputs.Items.Cast<ListViewItem>()
                .Where(i => i.Tag is EncodeJob)
                .Select(i => (Item: i, Job: (EncodeJob)i.Tag!))
                .Where(x => !string.Equals(x.Job.Status, "完了", StringComparison.Ordinal))
                .ToList();

            if (jobs.Count == 0)
            {
                LogEncode("実行対象がありません。完了済み以外の入力を追加してください。");
                MessageBox.Show(this, "実行対象がありません。完了済み以外の入力を追加してください。", "確認", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            LogEncode("処理モード: " + GetEncoderProcessModeName(CurrentEncoderProcessMode));
            if (EncoderNeedsLut(CurrentEncoderProcessMode))
            {
                LogEncode("LUT補間方式: " + GetEffectiveLutInterpolation(CurrentEncoderProcessMode));
            }

            LogEncode($"エンコード開始: {jobs.Count} 件 / 並列数 {_settings.EncoderMaxParallelJobs}");
            int done = 0;
            using var sem = new SemaphoreSlim(Math.Max(1, _settings.EncoderMaxParallelJobs));
            var tasks = jobs.Select(async pair =>
            {
                var entered = false;
                try
                {
                    await sem.WaitAsync(token).ConfigureAwait(false);
                    entered = true;
                    await EncodeOneAsync(pair.Job, pair.Item, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    SetEncodeItemStatus(pair.Item, "取消", "-");
                }
                catch (Exception ex)
                {
                    pair.Job.Status = "失敗";
                    SetEncodeItemStatus(pair.Item, "失敗", "-");
                    LogEncode($"失敗: {pair.Job.InputPath}{Environment.NewLine}  {ex}");
                }
                finally
                {
                    int finished = Interlocked.Increment(ref done);
                    SafeUi(() => progressEncTotal.Value = Math.Min(100, (int)Math.Round(finished * 100.0 / Math.Max(1, jobs.Count))));
                    if (entered) sem.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks).ConfigureAwait(false);

            var executedJobs = jobs.Select(x => x.Job).ToList();
            var failedJobs = executedJobs.Where(j => string.Equals(j.Status, "失敗", StringComparison.Ordinal)).ToList();
            var canceledJobs = executedJobs.Where(j => string.Equals(j.Status, "取消", StringComparison.Ordinal)).ToList();
            var hadFailures = failedJobs.Count > 0;
            var wasCanceled = token.IsCancellationRequested || canceledJobs.Count > 0;

            if (wasCanceled)
            {
                LogEncode("エンコードはキャンセルされました。自動シャットダウンは実行しません。");
            }
            else if (hadFailures)
            {
                LogEncode($"エンコード完了。ただし失敗 {failedJobs.Count} 件があります。");
            }
            else
            {
                LogEncode("エンコード正常完了。");
            }

            if (!wasCanceled)
            {
                await HandlePostEncodeShutdownAsync(executedJobs, hadFailures, token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogEncode("エンコード開始処理で例外が発生しました:" + Environment.NewLine + ex);
            MessageBox.Show(this, $"エンコード開始処理でエラーが発生しました。{Environment.NewLine}{Environment.NewLine}{ex.GetType().Name}: {ex.Message}", "エンコードエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _encodeRunning = false;
            var wasCanceled = cts?.IsCancellationRequested == true;
            SafeUi(() =>
            {
                SetEncoderRunning(false);
                lblEncStatus.Text = wasCanceled ? "キャンセル済み" : "待機中";
                UpdateGlobalStatus("待機中");
            });
            if (ReferenceEquals(_encodeCts, cts)) _encodeCts = null;
            cts?.Dispose();
        }
    }

    private async Task HandlePostEncodeShutdownAsync(IReadOnlyList<EncodeJob> jobs, bool hadFailures, CancellationToken token)
    {
        // v1.0.8: エンコード開始時ではなく、完了直後の最新UI設定を採用します。
        // これにより、エンコード中にON/OFFや待機時間を変更した場合も反映されます。
        var options = GetPostEncodeShutdownOptionsSnapshot();
        PersistPostEncodeShutdownOptions(options);

        LogEncode($"完了直後の最新シャットダウン設定: {(options.Enabled ? "ON" : "OFF")} / エラー時強制: {(options.ShutdownOnError ? "ON" : "OFF")} / 待機: {options.DelayValue} {options.UnitName}");

        if (!options.Enabled)
        {
            LogEncode("完了直後の設定がOFFのため、自動シャットダウンは実行しません。");
            return;
        }

        if (hadFailures && !options.ShutdownOnError)
        {
            LogEncode("エラー終了が含まれ、完了直後の「エラー終了時も強制シャットダウン」がOFFのため、自動シャットダウンは実行しません。");
            return;
        }

        var failedJobs = jobs.Where(j => string.Equals(j.Status, "失敗", StringComparison.Ordinal)).ToList();
        var delaySeconds = options.DelaySeconds;
        var scheduledAt = DateTime.Now.AddSeconds(delaySeconds);
        var reason = hadFailures ? "エラー終了時も強制シャットダウン" : "処理完了後シャットダウン";

        LogEncode($"{reason}: {delaySeconds} 秒後（{scheduledAt:yyyy-MM-dd HH:mm:ss}）に Windows をシャットダウンします。");
        if (hadFailures)
        {
            LogEncode($"エラー終了時も強制シャットダウン ON: 失敗 {failedJobs.Count} 件。詳細ログ保存後にシャットダウンします。");
            foreach (var job in failedJobs.Take(20)) LogEncode("失敗対象: " + job.InputPath);
            if (failedJobs.Count > 20) LogEncode($"失敗対象は他に {failedJobs.Count - 20} 件あります。");
        }

        var firstLog = TrySaveEncodeLogForShutdown(jobs, hadFailures, "shutdown_scheduled");
        if (!string.IsNullOrWhiteSpace(firstLog)) LogEncode("シャットダウン前ログ保存: " + firstLog);
        SafeUi(() => lblEncStatus.Text = $"シャットダウン待機中: {delaySeconds}秒");

        try
        {
            for (var remaining = delaySeconds; remaining > 0; remaining--)
            {
                token.ThrowIfCancellationRequested();
                if (remaining <= 10 || remaining % 60 == 0 || remaining % 30 == 0)
                {
                    LogEncode($"シャットダウンまで残り {remaining} 秒。中止する場合は「キャンセル」を押してください。");
                }
                SafeUi(() => lblEncStatus.Text = $"シャットダウン待機中: 残り {remaining}秒");
                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            }

            var finalLog = TrySaveEncodeLogForShutdown(jobs, hadFailures, "shutdown_execute");
            if (!string.IsNullOrWhiteSpace(finalLog)) LogEncode("シャットダウン直前ログ保存: " + finalLog);
            LogEncode("Windows シャットダウンを実行します: shutdown.exe /s /t 0");
            ExecuteWindowsShutdown();
        }
        catch (OperationCanceledException)
        {
            LogEncode("シャットダウン待機はキャンセルされました。Windows のシャットダウンは実行しません。");
            SafeUi(() => lblEncStatus.Text = "シャットダウン待機キャンセル");
        }
    }


    private sealed class PostEncodeShutdownOptions
    {
        public bool Enabled { get; init; }
        public bool ShutdownOnError { get; init; }
        public int DelayValue { get; init; }
        public int UnitIndex { get; init; }
        public int DelaySeconds { get; init; }
        public string UnitName => UnitIndex == 1 ? "分" : "秒";
    }

    private PostEncodeShutdownOptions GetPostEncodeShutdownOptionsSnapshot()
    {
        if (IsDisposed) return GetPostEncodeShutdownOptionsFromSettings();

        if (InvokeRequired)
        {
            try
            {
                return (PostEncodeShutdownOptions)Invoke(new Func<PostEncodeShutdownOptions>(GetPostEncodeShutdownOptionsSnapshot));
            }
            catch
            {
                return GetPostEncodeShutdownOptionsFromSettings();
            }
        }

        var enabled = chkEncShutdownAfterComplete?.Checked == true;
        var shutdownOnError = enabled && chkEncShutdownOnError?.Checked == true;
        var delayValue = numEncShutdownDelay == null ? _settings.EncoderShutdownDelayValue : (int)numEncShutdownDelay.Value;
        var unitIndex = cmbEncShutdownDelayUnit == null ? _settings.EncoderShutdownDelayUnitIndex : Math.Clamp(cmbEncShutdownDelayUnit.SelectedIndex, 0, 1);
        return CreatePostEncodeShutdownOptions(enabled, shutdownOnError, delayValue, unitIndex);
    }

    private PostEncodeShutdownOptions GetPostEncodeShutdownOptionsFromSettings()
    {
        return CreatePostEncodeShutdownOptions(
            _settings.EncoderShutdownAfterComplete,
            _settings.EncoderShutdownAfterComplete && _settings.EncoderShutdownOnError,
            _settings.EncoderShutdownDelayValue,
            _settings.EncoderShutdownDelayUnitIndex);
    }

    private static PostEncodeShutdownOptions CreatePostEncodeShutdownOptions(bool enabled, bool shutdownOnError, int delayValue, int unitIndex)
    {
        var safeValue = Math.Clamp(delayValue, 1, 1440);
        var safeUnit = Math.Clamp(unitIndex, 0, 1);
        var seconds = safeUnit == 1 ? checked(safeValue * 60) : safeValue;
        return new PostEncodeShutdownOptions
        {
            Enabled = enabled,
            ShutdownOnError = enabled && shutdownOnError,
            DelayValue = safeValue,
            UnitIndex = safeUnit,
            DelaySeconds = seconds,
        };
    }

    private void PersistPostEncodeShutdownOptions(PostEncodeShutdownOptions options)
    {
        try
        {
            _settings.EncoderShutdownAfterComplete = options.Enabled;
            _settings.EncoderShutdownOnError = options.ShutdownOnError;
            _settings.EncoderShutdownDelayValue = options.DelayValue;
            _settings.EncoderShutdownDelayUnitIndex = options.UnitIndex;
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            LogEncode("完了直後のシャットダウン設定保存に失敗しました。ただし今回の判定には最新UI設定を使用します: " + ex.Message);
        }
    }

    private int GetPostEncodeShutdownDelaySeconds()
    {
        var value = Math.Clamp(_settings.EncoderShutdownDelayValue, 1, 1440);
        var unit = Math.Clamp(_settings.EncoderShutdownDelayUnitIndex, 0, 1);
        return unit == 1 ? checked(value * 60) : value;
    }

    private string? TrySaveEncodeLogForShutdown(IReadOnlyList<EncodeJob> jobs, bool hadFailures, string stage)
    {
        try
        {
            return SaveEncodeLogForShutdown(jobs, hadFailures, stage);
        }
        catch (Exception ex)
        {
            LogEncode("シャットダウン前の詳細ログ保存に失敗しました: " + ex);
            return null;
        }
    }

    private string SaveEncodeLogForShutdown(IReadOnlyList<EncodeJob> jobs, bool hadFailures, string stage)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, AppName + "_logs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"encode_{stage}_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        var sb = new StringBuilder();
        sb.AppendLine(AppName);
        sb.AppendLine("生成日時: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("段階: " + stage);
        sb.AppendLine("エラーを含む: " + (hadFailures ? "はい" : "いいえ"));
        sb.AppendLine("処理件数: " + jobs.Count);
        sb.AppendLine();
        sb.AppendLine("[ジョブ一覧]");
        foreach (var job in jobs)
        {
            sb.AppendLine($"- [{job.Status}] {job.InputPath}");
            if (!string.IsNullOrWhiteSpace(job.OutputPath)) sb.AppendLine($"  出力: {job.OutputPath}");
        }
        sb.AppendLine();
        sb.AppendLine("[画面ログ]");
        sb.AppendLine(GetEncodeLogTextSnapshot());
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        return path;
    }

    private string GetEncodeLogTextSnapshot()
    {
        try
        {
            if (logEnc == null || logEnc.IsDisposed) return string.Empty;
            if (logEnc.InvokeRequired)
            {
                return (string)(logEnc.Invoke(new Func<string>(() => logEnc.Text)) ?? string.Empty);
            }
            return logEnc.Text;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void ExecuteWindowsShutdown()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("/s");
            psi.ArgumentList.Add("/t");
            psi.ArgumentList.Add("0");
            using var process = Process.Start(psi);
            if (process == null) LogEncode("shutdown.exe の起動に失敗しました。Process.Start が null を返しました。");
        }
        catch (Exception ex)
        {
            LogEncode("shutdown.exe の実行に失敗しました: " + ex);
        }
    }

    private StringBuilder ValidateEncoderBeforeStart()
    {
        var sb = new StringBuilder();
        if (lvEncInputs.Items.Count == 0) sb.AppendLine("入力動画がありません。");
        // 出力先フォルダが空欄の場合は、各入力動画と同じフォルダへ出力します。
        var mode = Math.Clamp(_settings.EncoderProcessModeIndex, 0, 4);
        var needsLut = EncoderNeedsLut(mode);
        if (needsLut && !File.Exists(_settings.EncoderLutPath ?? string.Empty)) sb.AppendLine("LUT適用ONですが、.cubeファイルが見つかりません。");
        if (!string.IsNullOrWhiteSpace(_settings.FfmpegPath) && !File.Exists(_settings.FfmpegPath)) sb.AppendLine("ffmpeg.exe の指定パスが見つかりません。空欄なら PATH 上の ffmpeg を使用します。");
        if (!string.IsNullOrWhiteSpace(_settings.FfprobePath) && !File.Exists(_settings.FfprobePath)) sb.AppendLine("ffprobe.exe の指定パスが見つかりません。空欄なら PATH 上の ffprobe を使用します。");
        return sb;
    }

    private async Task EncodeOneAsync(EncodeJob job, ListViewItem item, CancellationToken token)
    {
        job.Status = "処理中";
        SetEncodeItemStatus(item, "処理中", "0%");
        var output = BuildEncodeOutputPath(job.InputPath);
        job.OutputPath = output;
        if (File.Exists(output) && !_settings.EncoderOverwrite)
        {
            job.Status = "スキップ";
            SetEncodeItemStatus(item, "スキップ", "-");
            LogEncode($"スキップ（出力済み）: {output}");
            return;
        }

        double duration = await TryGetDurationSecondsAsync(job.InputPath, token).ConfigureAwait(false);
        var args = BuildEncodeFfmpegArguments(job.InputPath, output);
        LogEncode("----");
        LogEncode($"入力: {job.InputPath}");
        LogEncode($"出力: {output}");
        LogEncode("FFmpeg引数: " + string.Join(" ", args.Select(QuoteArgForLog)));

        var psi = new ProcessStartInfo
        {
            FileName = GetFfmpegExecutable(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        try { proc.Start(); }
        catch (Exception ex)
        {
            throw new InvalidOperationException("ffmpeg の起動に失敗しました。ffmpeg.exe のパス、または PATH 設定を確認してください。" + Environment.NewLine + ex.Message, ex);
        }

        using var reg = token.Register(() => TryKill(proc));
        var stdoutTask = ReadProgressAsync(proc.StandardOutput, duration, item, token);
        var stderrTask = ReadEncodeLogStreamAsync(proc.StandardError, token);
        await proc.WaitForExitAsync(token).ConfigureAwait(false);
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

        if (proc.ExitCode != 0)
        {
            job.Status = "失敗";
            SetEncodeItemStatus(item, "失敗", "-");
            throw new InvalidOperationException($"ffmpeg が異常終了しました。ExitCode={proc.ExitCode}");
        }

        job.Status = "完了";
        SetEncodeItemStatus(item, "完了", "100%");
        LogEncode($"完了: {output}");
    }

    private string GetFfmpegExecutable() => string.IsNullOrWhiteSpace(_settings.FfmpegPath) ? "ffmpeg" : _settings.FfmpegPath;
    private string GetFfprobeExecutable() => string.IsNullOrWhiteSpace(_settings.FfprobePath) ? "ffprobe" : _settings.FfprobePath;

    private async Task<double> TryGetDurationSecondsAsync(string input, CancellationToken token)
    {
        try
        {
            var info = await ProbeVideoInfoAsync(input, token).ConfigureAwait(false);
            return info.Duration;
        }
        catch
        {
            return 0;
        }
    }

    private List<string> BuildEncodeFfmpegArguments(string input, string output)
    {
        var args = new List<string> { "-hide_banner" };
        args.Add(_settings.EncoderOverwrite ? "-y" : "-n");
        args.Add("-i"); args.Add(input);

        var filter = BuildEncodeVideoFilter();
        if (!string.IsNullOrWhiteSpace(filter)) { args.Add("-vf"); args.Add(filter); }

        var mode = Math.Clamp(_settings.EncoderProcessModeIndex, 0, 4);
        var isLutOnlyFamily = mode == 1 || mode == 2 || mode == 3;
        var isFastFamily = mode == 2 || mode == 3;
        var useHevcNvenc = _settings.EncoderUseHevcNvenc || isLutOnlyFamily;
        if (useHevcNvenc)
        {
            if (isFastFamily)
            {
                args.AddRange(new[] { "-c:v", "hevc_nvenc", "-preset", "p1", "-rc", "vbr", "-cq", _settings.EncoderCq.ToString(CultureInfo.InvariantCulture), "-b:v", "0" });
            }
            else
            {
                args.AddRange(new[] { "-c:v", "hevc_nvenc", "-preset", "p7", "-tune", "hq", "-rc", "vbr", "-cq", _settings.EncoderCq.ToString(CultureInfo.InvariantCulture), "-b:v", "0" });
            }

            if (_settings.EncoderUseMain10) args.AddRange(new[] { "-profile:v", "main10", "-pix_fmt", "p010le" });

            if (isFastFamily)
            {
                args.AddRange(new[] { "-spatial_aq", "0", "-temporal_aq", "0", "-rc-lookahead", "0", "-bf", "0", "-b_ref_mode", "disabled" });
            }
            else
            {
                args.AddRange(new[] { "-spatial_aq", "1", "-temporal_aq", "1", "-aq-strength", _settings.EncoderAqStrength.ToString(CultureInfo.InvariantCulture) });
                args.AddRange(new[] { "-rc-lookahead", _settings.EncoderLookahead.ToString(CultureInfo.InvariantCulture) });
                args.AddRange(new[] { "-bf", _settings.EncoderBFrames.ToString(CultureInfo.InvariantCulture), "-b_ref_mode", _settings.EncoderBFrames > 0 ? "middle" : "disabled" });
            }
        }
        else
        {
            args.AddRange(new[] { "-c:v", "libx265", "-preset", "medium", "-crf", _settings.EncoderCq.ToString(CultureInfo.InvariantCulture) });
            if (_settings.EncoderUseMain10) args.AddRange(new[] { "-pix_fmt", "yuv420p10le" });
            args.AddRange(new[] { "-x265-params", $"bframes={_settings.EncoderBFrames}" });
        }

        if (_settings.EncoderCopyAudio || isLutOnlyFamily) args.AddRange(new[] { "-c:a", "copy" });
        else args.AddRange(new[] { "-c:a", "aac", "-b:a", "192k" });
        args.AddRange(new[] { "-map", "0:v:0", "-map", "0:a?", "-sn", "-dn" });
        if (_settings.EncoderWebOptimized) args.AddRange(new[] { "-movflags", "+faststart" });
        args.AddRange(new[] { "-progress", "pipe:1", "-nostats" });
        args.Add(output);
        return args;
    }

    private string BuildEncodeVideoFilter()
    {
        var filters = new List<string>();
        var mode = Math.Clamp(_settings.EncoderProcessModeIndex, 0, 4);
        var applyLut = EncoderNeedsLut(mode);
        var applySoftCorrection = mode == 0 && _settings.EncoderSoftIndoorCorrection;

        if (applyLut)
        {
            var escapedLut = EscapeFilterFilePath(_settings.EncoderLutPath ?? string.Empty);
            var interpolation = GetEffectiveLutInterpolation(mode);
            filters.Add($"lut3d=file='{escapedLut}':interp={interpolation}");
        }
        if (applySoftCorrection)
        {
            filters.Add("eq=gamma=1.04:contrast=0.98:saturation=1.00:brightness=0.00");
        }
        return string.Join(",", filters);
    }

    private bool EncoderNeedsLut(int mode)
    {
        return mode == 1 || mode == 2 || mode == 3 || (mode == 0 && _settings.EncoderApplyLut);
    }

    private string GetEffectiveLutInterpolation(int mode)
    {
        if (mode == 2) return "trilinear";
        if (mode == 3) return "nearest";
        return Math.Clamp(_settings.EncoderLutInterpolationIndex, 0, 2) switch
        {
            1 => "nearest",
            2 => "tetrahedral",
            _ => "trilinear",
        };
    }

    private static string GetEncoderProcessModeName(int mode)
    {
        return Math.Clamp(mode, 0, 4) switch
        {
            1 => "LUTのみ適用：品質優先",
            2 => "高速LUT適用：trilinear + NVENC p1",
            3 => "最速LUT適用：nearest + NVENC p1",
            4 => "HEVC変換のみ：LUTなし",
            _ => "標準：LUT + HEVC/NVENC エンコード",
        };
    }

    private string BuildEncodeOutputPath(string input)
    {
        var outDir = string.IsNullOrWhiteSpace(_settings.EncoderOutputDirectory)
            ? Path.GetDirectoryName(input) ?? Environment.CurrentDirectory
            : _settings.EncoderOutputDirectory;
        Directory.CreateDirectory(outDir);
        var suffix = _settings.EncoderOutputSuffix?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(suffix) || IsKnownDefaultEncoderSuffix(suffix)) suffix = GetDefaultEncoderSuffixForMode();
        var name = Path.GetFileNameWithoutExtension(input) + suffix + ".mp4";
        return Path.Combine(outDir, SanitizeFileName(name));
    }

    private static string EscapeFilterFilePath(string path)
    {
        var p = path.Replace('\\', '/');
        p = p.Replace(":", "\\:");
        p = p.Replace("'", "\\'");
        return p;
    }

    private async Task ReadProgressAsync(StreamReader reader, double duration, ListViewItem item, CancellationToken token)
    {
        while (!reader.EndOfStream && !token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line == null) break;
            if (duration <= 0) continue;
            if (line.StartsWith("out_time_ms=", StringComparison.OrdinalIgnoreCase))
            {
                var raw = line[12..].Trim();
                if (long.TryParse(raw, out var us))
                {
                    var sec = us / 1_000_000.0;
                    var pct = Math.Clamp((int)Math.Round(sec * 100.0 / duration), 0, 100);
                    SetEncodeItemStatus(item, "処理中", pct + "%");
                    SafeUi(() =>
                    {
                        var inputName = item.Tag is EncodeJob taggedJob ? Path.GetFileName(taggedJob.InputPath) : "入力動画";
                        lblEncStatus.Text = $"処理中: {inputName} / {pct}%";
                    });
                }
            }
        }
    }

    private async Task ReadEncodeLogStreamAsync(StreamReader reader, CancellationToken token)
    {
        while (!reader.EndOfStream && !token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line == null) break;
            if (line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("warning", StringComparison.OrdinalIgnoreCase)) LogEncode(line);
        }
    }

    private void CancelEncoding()
    {
        _encodeCts?.Cancel();
        LogEncode("キャンセル要求を送信しました。実行中の ffmpeg またはシャットダウン待機を停止します。");
    }

    private void SetEncoderRunning(bool running)
    {
        btnEncStart.Enabled = !running;
        btnEncCancel.Enabled = running;
        btnEncAutoScan.Enabled = !running;
        btnCommonSave.Enabled = !running && !_concatRunning;

        // v1.0.8: シャットダウン関連UIだけは、エンコード中でも操作可能にします。
        // 完了直後に最新のUI状態を読み取るため、開始時固定にはしません。
        if (chkEncShutdownAfterComplete != null) chkEncShutdownAfterComplete.Enabled = true;
        UpdateEncodeShutdownUiRules();
    }

    private void ShowFirstEncodeCommand()
    {
        UpdateSettingsFromUi();
        var first = lvEncInputs.Items.Cast<ListViewItem>().Select(i => i.Tag as EncodeJob).FirstOrDefault(j => j != null);
        if (first == null)
        {
            MessageBox.Show(this, "入力動画がありません。", "確認", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var output = BuildEncodeOutputPath(first.InputPath);
        var args = BuildEncodeFfmpegArguments(first.InputPath, output);
        var cmd = QuoteArgForLog(GetFfmpegExecutable()) + " " + string.Join(" ", args.Select(QuoteArgForLog));
        Clipboard.SetText(cmd);
        LogEncode("コマンドをクリップボードへコピーしました:");
        LogEncode(cmd);
    }

    private void SetEncodeItemStatus(ListViewItem item, string status, string percent)
    {
        SafeUi(() =>
        {
            if (item.SubItems.Count >= 3)
            {
                item.SubItems[1].Text = status;
                item.SubItems[2].Text = percent;
            }
        });
    }

    private void AddConcatFilesDialog()
    {
        using var dialog = new OpenFileDialog { Filter = "動画ファイル (*.mp4;*.mkv)|*.mp4;*.mkv|すべて|*.*", Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) AddConcatInputPaths(dialog.FileNames);
    }

    private void AddConcatFolderDialog()
    {
        using var dialog = new FolderBrowserDialog { UseDescriptionForTitle = true, Description = "mp4 / mkv を検索するフォルダを選択してください" };
        if (dialog.ShowDialog(this) == DialogResult.OK) AddConcatInputPaths(new[] { dialog.SelectedPath });
    }

    private void AddConcatInputPaths(IEnumerable<string> paths)
    {
        UpdateSettingsFromUi();
        var added = 0;
        foreach (var path in ExpandVideoPaths(paths, ConcatVideoExtensions, _settings.ConcatRecursiveFolderAdd))
        {
            if (!lstConcatInputs.Items.Cast<string>().Any(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)))
            {
                lstConcatInputs.Items.Add(path);
                added++;
            }
        }
        if (added > 0)
        {
            LogConcat($"入力ファイル追加: {added} 件");
            AutoAdjustConcatOutputExtension(showMixedWarning: true);
        }
    }

    private void AutoAdjustConcatOutputExtension(bool showMixedWarning)
    {
        var files = lstConcatInputs.Items.Cast<string>().Where(File.Exists).Where(IsConcatSupportedVideo).ToList();
        if (files.Count == 0) return;
        var extensions = files.Select(x => Path.GetExtension(x).ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (extensions.Count == 1)
        {
            var targetExt = extensions[0];
            var name = SanitizeFileName(txtConcatOutputFileName.Text.Trim());
            if (string.IsNullOrWhiteSpace(name)) name = "joined_video" + targetExt;
            else
            {
                var currentExt = Path.GetExtension(name);
                name = string.IsNullOrWhiteSpace(currentExt) ? name + targetExt : Path.ChangeExtension(name, targetExt);
            }
            txtConcatOutputFileName.Text = name;
            _settings.ConcatOutputFileName = name;
            LogConcat($"入力ファイルの拡張子に合わせて、出力ファイル名を {targetExt} に自動調整しました。");
            return;
        }

        if (extensions.Count >= 2 && showMixedWarning)
        {
            MessageBox.Show(this,
                ".mp4 と .mkv が混在しています。\n\n高速・無劣化連結では、同じコンテナ形式・同じ映像/音声仕様のファイルだけを連結するのが安全です。\n混在させる場合は、出力拡張子を手動で指定し、必要に応じて『互換重視連結』を使用してください。",
                "入力形式の混在確認",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static bool IsConcatSupportedVideo(string path) => ConcatVideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private void RemoveSelectedConcatInputs()
    {
        var selected = lstConcatInputs.SelectedIndices.Cast<int>().OrderByDescending(i => i).ToList();
        foreach (var index in selected) lstConcatInputs.Items.RemoveAt(index);
    }

    private void MoveSelectedConcat(int direction)
    {
        if (lstConcatInputs.SelectedIndices.Count == 0) return;
        var selected = lstConcatInputs.SelectedIndices.Cast<int>().OrderBy(i => direction > 0 ? -i : i).ToList();
        foreach (var index in selected)
        {
            var newIndex = index + direction;
            if (newIndex < 0 || newIndex >= lstConcatInputs.Items.Count) continue;
            var item = lstConcatInputs.Items[index];
            lstConcatInputs.Items.RemoveAt(index);
            lstConcatInputs.Items.Insert(newIndex, item);
            lstConcatInputs.SetSelected(newIndex, true);
        }
    }

    private async Task StartConcatAsync()
    {
        if (_concatRunning) return;
        UpdateSettingsFromUi();
        if (!ValidateConcatInputs(out var ffmpeg, out var outputFile, out var inputFiles)) return;
        _settingsService.Save(_settings);

        _concatCts = new CancellationTokenSource();
        _concatRunning = true;
        SetConcatRunning(true);
        progressConcat.Style = ProgressBarStyle.Marquee;
        UpdateGlobalStatus("連結中");

        var workDir = Path.Combine(Path.GetTempPath(), "DJI_Action_VideoToolbox_Concat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var listFile = Path.Combine(workDir, "concat_list.txt");

        try
        {
            File.WriteAllText(listFile, BuildConcatList(inputFiles), new UTF8Encoding(false));
            var args = BuildConcatFfmpegArguments(listFile, outputFile);
            LogConcat("----");
            LogConcat("連結開始");
            LogConcat("出力: " + outputFile);
            LogConcat("FFmpeg引数: " + string.Join(" ", args.Select(QuoteArgForLog)));
            var exit = await RunProcessAsync(ffmpeg, args, _concatCts.Token);
            if (exit == 0 && File.Exists(outputFile))
            {
                LogConcat("正常終了しました。");
                if (_settings.ConcatOpenOutputFolderAfterFinish) OpenFolder(Path.GetDirectoryName(outputFile) ?? "");
            }
            else
            {
                LogConcat($"FFmpegが異常終了しました。ExitCode={exit}");
                MessageBox.Show(this, "連結処理が正常終了しませんでした。ログを確認してください。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (OperationCanceledException)
        {
            LogConcat("キャンセルされました。出力途中のファイルは必要に応じて削除してください。");
        }
        catch (Exception ex)
        {
            LogConcat("例外: " + ex.Message);
            MessageBox.Show(this, ex.Message, "例外", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try { Directory.Delete(workDir, true); } catch { }
            SafeUi(() =>
            {
                progressConcat.Style = ProgressBarStyle.Blocks;
                progressConcat.Value = 0;
                SetConcatRunning(false);
                UpdateGlobalStatus("待機中");
            });
            _concatRunning = false;
            _concatCts.Dispose();
            _concatCts = null;
        }
    }

    private bool ValidateConcatInputs(out string ffmpeg, out string outputFile, out List<string> inputFiles)
    {
        ffmpeg = GetFfmpegExecutable();
        outputFile = "";
        inputFiles = lstConcatInputs.Items.Cast<string>().Where(File.Exists).Where(IsConcatSupportedVideo).ToList();

        if (!string.IsNullOrWhiteSpace(_settings.FfmpegPath) && (!File.Exists(_settings.FfmpegPath) || !string.Equals(Path.GetFileName(_settings.FfmpegPath), "ffmpeg.exe", StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "ffmpeg.exe の位置が正しくありません。空欄なら PATH 上の ffmpeg を使用します。", "確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (inputFiles.Count < 2)
        {
            MessageBox.Show(this, "連結する .mp4 / .mkv ファイルを2件以上指定してください。", "確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        var outDir = _settings.ConcatOutputFolder;
        if (string.IsNullOrWhiteSpace(outDir))
        {
            MessageBox.Show(this, "出力フォルダを指定してください。", "確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        Directory.CreateDirectory(outDir);
        AutoAdjustConcatOutputExtension(showMixedWarning: false);
        var name = SanitizeFileName(txtConcatOutputFileName.Text.Trim());
        var inputExtensions = inputFiles.Select(x => Path.GetExtension(x).ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (inputExtensions.Count == 1)
        {
            var targetExt = inputExtensions[0];
            if (string.IsNullOrWhiteSpace(name)) name = "joined_video" + targetExt;
            else
            {
                var currentExt = Path.GetExtension(name);
                name = string.IsNullOrWhiteSpace(currentExt) ? name + targetExt : Path.ChangeExtension(name, targetExt);
            }
            txtConcatOutputFileName.Text = name;
            _settings.ConcatOutputFileName = name;
        }
        else if (inputExtensions.Count >= 2)
        {
            MessageBox.Show(this,
                ".mp4 と .mkv が混在しています。\n\n混在入力では出力拡張子を自動判定できません。\n出力ファイル名を .mp4 または .mkv まで手動指定してください。",
                "入力形式の混在確認",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        if (string.IsNullOrWhiteSpace(name)) name = "joined_video.mkv";
        var ext = Path.GetExtension(name);
        if (!ConcatVideoExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "出力ファイル名の拡張子は .mp4 または .mkv にしてください。", "確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        outputFile = Path.Combine(outDir, name);
        if (File.Exists(outputFile) && !_settings.ConcatOverwrite)
        {
            MessageBox.Show(this, "同名の出力ファイルが既に存在します。上書きを有効にするか、別名を指定してください。", "確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        return true;
    }

    private List<string> BuildConcatFfmpegArguments(string listFile, string outputFile)
    {
        var args = new List<string>();
        args.Add(_settings.ConcatOverwrite ? "-y" : "-n");
        args.AddRange(new[] { "-hide_banner", "-ignore_unknown", "-f", "concat", "-safe", "0", "-i", listFile });
        var ext = Path.GetExtension(outputFile).ToLowerInvariant();
        if (_settings.ConcatModeIndex == 1)
        {
            args.AddRange(new[] { "-map", "0:v:0", "-map", "0:a?", "-dn", "-sn", "-c:v", "libx264", "-preset", "veryfast", "-crf", "18", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k" });
        }
        else
        {
            args.AddRange(new[] { "-map", "0:v:0", "-map", "0:a?", "-dn", "-sn", "-c", "copy" });
        }
        if (ext == ".mp4" && _settings.ConcatAddFastStartForMp4) args.AddRange(new[] { "-movflags", "+faststart" });
        args.Add(outputFile);
        return args;
    }

    private static string BuildConcatList(IEnumerable<string> files)
    {
        var sb = new StringBuilder();
        foreach (var file in files)
        {
            var normalized = Path.GetFullPath(file).Replace("\\", "/").Replace("'", "'\\''");
            sb.Append("file '").Append(normalized).AppendLine("'");
        }
        return sb.ToString();
    }

    private async Task<int> RunProcessAsync(string exe, IReadOnlyList<string> args, CancellationToken token)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) LogConcat(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) LogConcat(e.Data); };
        try { p.Start(); }
        catch (Exception ex) { throw new InvalidOperationException("ffmpeg の起動に失敗しました。ffmpeg.exe のパス、または PATH 設定を確認してください。" + Environment.NewLine + ex.Message, ex); }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try
        {
            await p.WaitForExitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(p);
            throw;
        }
        return p.ExitCode;
    }

    private void SetConcatRunning(bool running)
    {
        btnConcatStart.Enabled = !running;
        btnConcatCancel.Enabled = running;
        btnConcatAddFiles.Enabled = !running;
        btnConcatAddFolder.Enabled = !running;
        btnConcatRemove.Enabled = !running;
        btnConcatClear.Enabled = !running;
        btnConcatUp.Enabled = !running;
        btnConcatDown.Enabled = !running;
        cmbConcatMode.Enabled = !running;
        btnCommonSave.Enabled = !running && !_encodeRunning;
    }

    private void ShowConcatCommand()
    {
        UpdateSettingsFromUi();
        if (!ValidateConcatInputs(out var ffmpeg, out var outputFile, out var inputFiles)) return;
        var previewList = Path.Combine(Path.GetTempPath(), "concat_list_preview.txt");
        var args = BuildConcatFfmpegArguments(previewList, outputFile);
        var cmd = QuoteArgForLog(ffmpeg) + " " + string.Join(" ", args.Select(QuoteArgForLog));
        Clipboard.SetText(cmd);
        LogConcat("コマンド例をクリップボードへコピーしました。実行時は一時フォルダ内の concat_list.txt を使用します。");
        LogConcat(cmd);
        LogConcat("concat_list.txt 内容例:");
        LogConcat(BuildConcatList(inputFiles));
    }

    private static string SanitizeFileName(string name)
    {
        name = Path.GetFileName(name.Trim().Trim('"'));
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    private static string QuoteArgForLog(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        char[] needQuoteChars = { ' ', '　', '&', '(', ')', '[', ']', ';' };
        return s.IndexOfAny(needQuoteChars) >= 0 ? "\"" + s.Replace("\"", "\\\"") + "\"" : s;
    }

    private static string GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null ? p.ToString() : "";

    private static int GetInt(JsonElement e, string name)
    {
        var s = GetString(e, name);
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static long GetLong(JsonElement e, string name)
    {
        var s = GetString(e, name);
        return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static double GetDouble(JsonElement e, string name)
    {
        var s = GetString(e, name);
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static double ParseFrameRate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        if (raw.Contains('/'))
        {
            var parts = raw.Split('/');
            if (parts.Length == 2 &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d != 0)
            {
                return n / d;
            }
        }
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static int Clamp(int value, int min, int max) => Math.Min(max, Math.Max(min, value));

    private static string FormatBitrate(long bitrate)
    {
        if (bitrate <= 0) return "unknown";
        return (bitrate / 1_000_000.0).ToString("0.###", CultureInfo.InvariantCulture) + " Mbps";
    }

    private void TryKill(Process proc)
    {
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
    }

    private void LogEncode(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        SafeUi(() =>
        {
            logEnc.AppendText(line + Environment.NewLine);
            logEnc.SelectionStart = logEnc.TextLength;
            logEnc.ScrollToCaret();
        });
    }

    private void LogConcat(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        SafeUi(() =>
        {
            logConcat.AppendText(line + Environment.NewLine);
            logConcat.SelectionStart = logConcat.TextLength;
            logConcat.ScrollToCaret();
        });
    }

    private void UpdateGlobalStatus(string text)
    {
        SafeUi(() => lblGlobalStatus.Text = $"状態: {text}    |    {AppName}    |    設定: {_settingsService.SettingsPath}");
    }

    private void SafeUi(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(action); } catch { }
        }
        else action();
    }

    private void SaveEncodeLogDialog()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "ログファイル|*.log;*.txt|すべて|*.*",
            FileName = $"DJI_Action_VideoToolbox_Encode_{DateTime.Now:yyyyMMdd_HHmmss}.log",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dialog.FileName, logEnc.Text, new UTF8Encoding(false));
        LogEncode("ログ保存: " + dialog.FileName);
    }

    private void SaveConcatLogDialog()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "ログファイル|*.log;*.txt|すべて|*.*",
            FileName = $"DJI_Action_VideoToolbox_Concat_{DateTime.Now:yyyyMMdd_HHmmss}.log",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dialog.FileName, logConcat.Text, new UTF8Encoding(false));
        LogConcat("ログ保存: " + dialog.FileName);
    }

    private static void OpenFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_encodeRunning || _concatRunning)
        {
            var result = MessageBox.Show(this, "処理実行中です。終了すると処理をキャンセルします。終了しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _encodeCts?.Cancel();
            _concatCts?.Cancel();
        }
        SaveAllSettingsFromUi();
        base.OnFormClosing(e);
    }
}

public sealed class EncodeJob
{
    public string InputPath { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public string Status { get; set; } = "待機";
}

public sealed class VideoInfo
{
    public string InputPath { get; set; } = "";
    public string CodecName { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public double Fps { get; set; }
    public string PixelFormat { get; set; } = "";
    public int BitsPerRawSample { get; set; }
    public long StreamBitrate { get; set; }
    public long FormatBitrate { get; set; }
    public long EstimatedBitrateFromFile { get; set; }
    public long Bitrate => StreamBitrate > 0 ? StreamBitrate : (FormatBitrate > 0 ? FormatBitrate : EstimatedBitrateFromFile);
    public double Duration { get; set; }
    public long FileSize { get; set; }
    public string ColorSpace { get; set; } = "";
    public string ColorTransfer { get; set; } = "";
    public string ColorPrimaries { get; set; } = "";
}

public sealed class RecommendedEncodeSettings
{
    public int Cq { get; set; }
    public int BFrames { get; set; }
    public int Lookahead { get; set; }
    public int AqStrength { get; set; }
}

public sealed class AppSettings
{
    public string FfmpegPath { get; set; } = "";
    public string FfprobePath { get; set; } = "";

    public string EncoderLutPath { get; set; } = @"E:\DJI OSMO Action 4 D-Log M to Rec.709 vivid LUT\DJI OSMO Action 4 D-Log M to Rec.709 V1.cube";
    public string EncoderOutputDirectory { get; set; } = "";
    public string EncoderOutputSuffix { get; set; } = "_DLogM_to_Rec709_HEVC";
    public bool EncoderApplyLut { get; set; } = true;
    public bool EncoderUseHevcNvenc { get; set; } = true;
    public bool EncoderUseMain10 { get; set; } = true;
    public bool EncoderCopyAudio { get; set; } = true;
    public bool EncoderWebOptimized { get; set; } = true;
    public bool EncoderOverwrite { get; set; } = true;
    public bool EncoderSoftIndoorCorrection { get; set; } = false;
    public bool EncoderRecursiveFolderAdd { get; set; } = true;
    public int EncoderMaxParallelJobs { get; set; } = ComputeRecommendedParallelJobs();
    public int EncoderCq { get; set; } = 20;
    public int EncoderBFrames { get; set; } = 4;
    public int EncoderLookahead { get; set; } = 32;
    public int EncoderAqStrength { get; set; } = 10;
    public int EncoderProcessModeIndex { get; set; } = 0;
    public int EncoderLutInterpolationIndex { get; set; } = 0;
    public bool EncoderShutdownAfterComplete { get; set; } = false;
    public bool EncoderShutdownOnError { get; set; } = false;
    public int EncoderShutdownDelayValue { get; set; } = 30;
    public int EncoderShutdownDelayUnitIndex { get; set; } = 0;

    public string ConcatOutputFolder { get; set; } = "";
    public string ConcatOutputFileName { get; set; } = "joined_video.mkv";
    public bool ConcatOverwrite { get; set; } = false;
    public bool ConcatOpenOutputFolderAfterFinish { get; set; } = true;
    public bool ConcatAddFastStartForMp4 { get; set; } = true;
    public bool ConcatRecursiveFolderAdd { get; set; } = false;
    public int ConcatModeIndex { get; set; } = 0;

    public static int ComputeRecommendedParallelJobs()
    {
        var threads = Environment.ProcessorCount;
        if (threads >= 24) return 3;   // AMD Ryzen AI 9 HX 370: 12C/24T class。NVENC併用を前提に安全側。
        if (threads >= 16) return 2;
        return 1;
    }
}

public sealed class SettingsService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string SettingsPath { get; } = Path.Combine(AppContext.BaseDirectory, "DJI_Action_VideoToolbox_v1.0.8_settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                var settings = new AppSettings();
                Save(settings);
                return settings;
            }
            var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(SettingsPath, json, new UTF8Encoding(false));
    }
}
