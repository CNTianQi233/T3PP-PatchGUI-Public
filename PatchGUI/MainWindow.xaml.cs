// 该文件仅保留 MainWindow 的字段/构造函数；业务逻辑已拆分到其它 MainWindow.*.cs partial 文件中。
using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PatchGUI.Core;

namespace PatchGUI
{
    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        private static bool VerboseUiLog
        {
            get
            {
#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }

        // 规则加密使用的两个开发者密钥（发布版建议移到 C++ / DLL）
        private const string RuleKey1 = "CHANGE_ME_KEY1";
        private const string RuleKey2 = "CHANGE_ME_KEY2";
        private const string DirectoryModeTag = "dir";
        private const string FileModeTag = "file";
        private const string DirectoryPatchTag = "PATCH_MODE:DIRECTORY";
        private const string FilePatchTag = "PATCH_MODE:FILE";
        private const string ManualGameId = "__manual__";   // 特殊 Tag，表示手动模式

        private string? _currentGameId;
        private string? _currentGameName;
        private bool _isManualMode;
        private bool _useDirectoryMode = true;
        private string? _selectedPatchPath;
        private string? _crc32;
        private string? _md5;
        private string? _sha1;
        private static readonly uint[] Crc32Table = BuildCrc32Table();
        private TextBlock? _hashCrcText;
        private TextBlock? _hashMd5Text;
        private TextBlock? _hashSha1Text;
        private static readonly string ErrorLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
        private CancellationTokenSource? _patchCts;
        private bool _uiReady;
        private int _uiLockDepth;
        private bool _taskbarProgressActive;
        private DispatcherTimer? _taskbarProgressTimer;
        private double _taskbarProgressTarget;

        partial void InitializeDebugSettings();
        partial void ApplyDebugSettingsLocalization();
        partial void InitializeKeysPage();
        partial void ApplyKeysLocalization();
        /// <summary>
        /// 
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            Core.SessionLog.Initialize();
            AppendConsoleLine($"[INFO] {GetLogPathHint()}");

            PatchGUI.Core.T3ppDiff.DebugLog = msg => AppendConsoleLine($"[INFO] [NATIVE] {msg}");
            InitModeMenu();

            InitMode();               // DEBUG/RELEASE 导航控制
            LocalizationManager.LoadLanguage("zh_CN");
            ApplyLocalization();
            LanguageSelector.SelectedIndex = 0;
            InitializeDebugSettings();
            InitializeKeysPage();

            // 窗口加载完成后初始化导航指示条位置
            Loaded += (_, _) => InitializeNavIndicator();

            _uiReady = true;
        }
    }
}
