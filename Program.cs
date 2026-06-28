using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Switcher;

internal static class Program
{
    private const string MutexName = "Local\\Switcher.RuEn.AutoLayout";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            return;
        }

        if (args.Length > 0)
        {
            if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            {
                Environment.ExitCode = SelfTest.Run();
                return;
            }

            if (args.Contains("--install-startup", StringComparer.OrdinalIgnoreCase))
            {
                StartupManager.SetEnabled(true);
                return;
            }

            if (args.Contains("--uninstall-startup", StringComparer.OrdinalIgnoreCase))
            {
                StartupManager.SetEnabled(false);
                return;
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new SwitcherApplicationContext());
    }
}

internal static class SelfTest
{
    public static int Run()
    {
        var failures = new List<string>();
        CheckAuto(failures, "ghbdtn", "привет");
        CheckAuto(failures, "руддщ", "hello");
        CheckAuto(failures, "ntcn", "тест");
        CheckAuto(failures, "ьн", "my");
        CheckAuto(failures, "рш", "hi");
        CheckManual(failures, "vtyz", "меня");
        CheckManual(failures, "сщву", "code");
        CheckNoAuto(failures, "test");
        CheckNoAuto(failures, "code");
        CheckInputSize(failures);

        if (failures.Count == 0)
        {
            return 0;
        }

        var logPath = Path.Combine(Path.GetTempPath(), "Switcher.selftest.log");
        File.WriteAllLines(logPath, failures, Encoding.UTF8);
        return 1;
    }

    private static void CheckAuto(List<string> failures, string input, string expected)
    {
        if (!TextHeuristics.TryAutoCorrect(input, out var correction) || correction.Text != expected)
        {
            failures.Add($"AUTO {input}: expected {expected}, actual {correction?.Text ?? "<none>"}");
        }
    }

    private static void CheckManual(List<string> failures, string input, string expected)
    {
        if (!TextHeuristics.TryConvertAny(input, out var correction) || correction.Text != expected)
        {
            failures.Add($"MANUAL {input}: expected {expected}, actual {correction?.Text ?? "<none>"}");
        }
    }

    private static void CheckNoAuto(List<string> failures, string input)
    {
        if (TextHeuristics.TryAutoCorrect(input, out var correction))
        {
            failures.Add($"NOAUTO {input}: unexpected {correction.Text}");
        }
    }

    private static void CheckInputSize(List<string> failures)
    {
        var actual = Marshal.SizeOf<NativeMethods.Input>();
        var expected = Environment.Is64BitProcess ? 40 : 28;
        if (actual != expected)
        {
            failures.Add($"INPUT size: expected {expected}, actual {actual}");
        }
    }
}

internal sealed class SwitcherApplicationContext : ApplicationContext
{
    private readonly Control _uiThread = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly KeyboardHook _keyboardHook;
    private readonly StringBuilder _currentWord = new();
    private SettingsForm? _settingsForm;
    private LastTypedSegment? _lastTypedSegment;
    private LastCorrection? _lastCorrection;
    private AppSettings _settings;

    public SwitcherApplicationContext()
    {
        _settings = SettingsStore.Load();
        _settings.StartWithWindows = StartupManager.IsEnabled();
        _uiThread.CreateControl();

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconFactory.Create(),
            Text = "Switcher: RU/EN",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _notifyIcon.DoubleClick += (_, _) => ShowSettings();

        _keyboardHook = new KeyboardHook(HandleKeyDown);
    }

    public AppSettings Settings => _settings;

    public string CurrentStatus
    {
        get
        {
            var auto = _settings.AutoSwitch ? "авто включено" : "авто выключено";
            var sound = _settings.Sound ? "звук включен" : "звук выключен";
            return $"{auto}, {sound}";
        }
    }

    public void UpdateSettings(Action<AppSettings> update)
    {
        update(_settings);
        SettingsStore.Save(_settings);
        RefreshMenu();
        _settingsForm?.RefreshFromSettings();
    }

    protected override void ExitThreadCore()
    {
        SettingsStore.Save(_settings);
        _keyboardHook.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _uiThread.Dispose();
        base.ExitThreadCore();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("Открыть Switcher", null, (_, _) => ShowSettings());
        var autoItem = new ToolStripMenuItem("Автопереключение", null, (_, _) =>
        {
            UpdateSettings(s => s.AutoSwitch = !s.AutoSwitch);
        })
        {
            Name = "AutoSwitch",
        };
        var soundItem = new ToolStripMenuItem("Звук при смене", null, (_, _) =>
        {
            UpdateSettings(s => s.Sound = !s.Sound);
        })
        {
            Name = "Sound",
        };
        var startupItem = new ToolStripMenuItem("Запускать с Windows", null, (_, _) =>
        {
            var enabled = !StartupManager.IsEnabled();
            StartupManager.SetEnabled(enabled);
            UpdateSettings(s => s.StartWithWindows = enabled);
        })
        {
            Name = "Startup",
        };
        var convertItem = new ToolStripMenuItem("Конвертировать последнее слово: Ctrl+Alt+Space");
        convertItem.Enabled = false;
        var undoItem = new ToolStripMenuItem("Откатить автоисправление: Ctrl+Alt+Backspace");
        undoItem.Enabled = false;
        var exitItem = new ToolStripMenuItem("Выход", null, (_, _) => ExitThread());

        menu.Items.AddRange([
            openItem,
            new ToolStripSeparator(),
            autoItem,
            soundItem,
            startupItem,
            new ToolStripSeparator(),
            convertItem,
            undoItem,
            new ToolStripSeparator(),
            exitItem,
        ]);

        menu.Opening += (_, _) => RefreshMenu(menu);
        RefreshMenu(menu);
        return menu;
    }

    private void RefreshMenu()
    {
        if (_notifyIcon.ContextMenuStrip is not null)
        {
            RefreshMenu(_notifyIcon.ContextMenuStrip);
        }
    }

    private void RefreshMenu(ContextMenuStrip menu)
    {
        if (menu.Items["AutoSwitch"] is ToolStripMenuItem autoItem)
        {
            autoItem.Checked = _settings.AutoSwitch;
            autoItem.Text = _settings.AutoSwitch ? "Автопереключение: включено" : "Автопереключение: выключено";
        }

        if (menu.Items["Sound"] is ToolStripMenuItem soundItem)
        {
            soundItem.Checked = _settings.Sound;
            soundItem.Text = _settings.Sound ? "Звук при смене: включен" : "Звук при смене: выключен";
        }

        if (menu.Items["Startup"] is ToolStripMenuItem startupItem)
        {
            _settings.StartWithWindows = StartupManager.IsEnabled();
            startupItem.Checked = _settings.StartWithWindows;
            startupItem.Text = _settings.StartWithWindows ? "Запускать с Windows: да" : "Запускать с Windows: нет";
        }
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(this);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
    }

    private bool HandleKeyDown(Keys key, int scanCode)
    {
        if (KeyboardState.IsCtrlAltDown() && key == Keys.Back)
        {
            PostToUi(UndoLastCorrection);
            return true;
        }

        if (KeyboardState.IsCtrlAltDown() && key == Keys.Space)
        {
            PostToUi(ConvertRecentWordManually);
            return true;
        }

        if (KeyboardState.HasCommandModifierDown())
        {
            return false;
        }

        if (key == Keys.Back)
        {
            InvalidateRecentActions();
            if (_currentWord.Length > 0)
            {
                _currentWord.Length--;
            }

            return false;
        }

        if (KeyboardState.IsNavigationOrEditingKey(key))
        {
            ResetTypingState();
            return false;
        }

        var text = NativeMethods.TryTranslateKey((int)key, scanCode);
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var ch = text[0];
        if (char.IsControl(ch))
        {
            ResetTypingState();
            return false;
        }

        if (TextHeuristics.IsWordChar(ch))
        {
            InvalidateRecentActions();
            _currentWord.Append(ch);
            return false;
        }

        if (!TextHeuristics.IsDelimiter(ch))
        {
            ResetTypingState();
            return false;
        }

        var word = _currentWord.ToString();
        _currentWord.Clear();
        var delimiter = ch.ToString();

        if (_settings.AutoSwitch && TextHeuristics.TryAutoCorrect(word, out var correction))
        {
            PostToUi(() => ApplyCorrection(word, delimiter, correction));
            return true;
        }

        if (TextHeuristics.CanConvert(word))
        {
            _lastTypedSegment = new LastTypedSegment(word, delimiter);
        }
        else
        {
            _lastTypedSegment = null;
        }

        _lastCorrection = null;
        return false;
    }

    private void ApplyCorrection(string originalWord, string delimiter, CorrectionResult correction)
    {
        var originalText = originalWord + delimiter;
        var correctedText = correction.Text + delimiter;

        NativeMethods.SwitchForegroundLayout(correction.TargetLayout);
        if (!InputSender.ReplaceText(originalWord.Length, correctedText))
        {
            PlayErrorSound();
            UpdateBalloon("Ошибка ввода", "Windows заблокировала замену текста");
            return;
        }

        _lastCorrection = new LastCorrection(originalText, correctedText, correction.SourceLayout, correction.TargetLayout);
        _lastTypedSegment = null;

        PlaySwitchSound(correction.Direction);
        UpdateBalloon("Автозамена", $"{originalText} -> {correctedText}");
    }

    private void ConvertRecentWordManually()
    {
        if (_currentWord.Length > 0)
        {
            var word = _currentWord.ToString();
            if (!TextHeuristics.TryConvertAny(word, out var correction))
            {
                PlayErrorSound();
                return;
            }

            NativeMethods.SwitchForegroundLayout(correction.TargetLayout);
            if (!InputSender.ReplaceText(word.Length, correction.Text))
            {
                PlayErrorSound();
                UpdateBalloon("Ошибка ввода", "Windows заблокировала замену текста");
                return;
            }

            _currentWord.Clear();
            _currentWord.Append(correction.Text);
            _lastCorrection = null;
            _lastTypedSegment = null;
            PlaySwitchSound(correction.Direction);
            UpdateBalloon("Ручная конвертация", $"{word} -> {correction.Text}");
            return;
        }

        if (_lastTypedSegment is null)
        {
            PlayErrorSound();
            return;
        }

        var segment = _lastTypedSegment;
        if (!TextHeuristics.TryConvertAny(segment.Word, out var segmentCorrection))
        {
            PlayErrorSound();
            return;
        }

        NativeMethods.SwitchForegroundLayout(segmentCorrection.TargetLayout);
        if (!InputSender.ReplaceText(segment.Word.Length + segment.Delimiter.Length, segmentCorrection.Text + segment.Delimiter))
        {
            PlayErrorSound();
            UpdateBalloon("Ошибка ввода", "Windows заблокировала замену текста");
            return;
        }

        _lastCorrection = null;
        _lastTypedSegment = null;
        PlaySwitchSound(segmentCorrection.Direction);
        UpdateBalloon("Ручная конвертация", $"{segment.Word} -> {segmentCorrection.Text}");
    }

    private void UndoLastCorrection()
    {
        if (_lastCorrection is null)
        {
            PlayErrorSound();
            return;
        }

        var correction = _lastCorrection;
        NativeMethods.SwitchForegroundLayout(correction.SourceLayout);
        if (!InputSender.ReplaceText(correction.CorrectedText.Length, correction.OriginalText))
        {
            PlayErrorSound();
            UpdateBalloon("Ошибка ввода", "Windows заблокировала замену текста");
            return;
        }

        _lastCorrection = null;
        _lastTypedSegment = null;
        _currentWord.Clear();
        PlayErrorSound();
        UpdateBalloon("Откат", correction.OriginalText.TrimEnd());
    }

    private void PostToUi(Action action)
    {
        if (_uiThread.IsDisposed)
        {
            return;
        }

        _uiThread.BeginInvoke(action);
    }

    private void ResetTypingState()
    {
        _currentWord.Clear();
        InvalidateRecentActions();
    }

    private void InvalidateRecentActions()
    {
        _lastCorrection = null;
        _lastTypedSegment = null;
    }

    private void PlaySwitchSound(LayoutDirection direction)
    {
        if (!_settings.Sound)
        {
            return;
        }

        if (direction == LayoutDirection.LatinToCyrillic)
        {
            SystemSounds.Asterisk.Play();
        }
        else
        {
            SystemSounds.Question.Play();
        }
    }

    private void PlayErrorSound()
    {
        if (_settings.Sound)
        {
            SystemSounds.Exclamation.Play();
        }
    }

    private void UpdateBalloon(string title, string text)
    {
        _notifyIcon.Text = "Switcher: " + CurrentStatus;
        _settingsForm?.SetLastAction($"{title}: {text}");
    }
}

internal sealed class SettingsForm : Form
{
    private readonly SwitcherApplicationContext _context;
    private readonly Label _statusLabel = new();
    private readonly Label _lastActionLabel = new();
    private readonly CheckBox _autoSwitch = new();
    private readonly CheckBox _sound = new();
    private readonly CheckBox _startup = new();

    public SettingsForm(SwitcherApplicationContext context)
    {
        _context = context;
        Text = "Switcher";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 360);
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(247, 249, 252);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            RowCount = 9,
            ColumnCount = 1,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var title = new Label
        {
            Text = "Switcher",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 20F),
            ForeColor = Color.FromArgb(24, 32, 43),
        };
        root.Controls.Add(title, 0, 0);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.ForeColor = Color.FromArgb(71, 85, 105);
        root.Controls.Add(_statusLabel, 0, 1);

        ConfigureCheckBox(_autoSwitch, "Автоматически исправлять слова после пробела или знака");
        ConfigureCheckBox(_sound, "Проигрывать звук при смене раскладки");
        ConfigureCheckBox(_startup, "Запускать вместе с Windows");
        root.Controls.Add(_autoSwitch, 0, 2);
        root.Controls.Add(_sound, 0, 3);
        root.Controls.Add(_startup, 0, 4);

        var hotkeysTitle = new Label
        {
            Text = "Горячие клавиши",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 10F),
            ForeColor = Color.FromArgb(24, 32, 43),
        };
        root.Controls.Add(hotkeysTitle, 0, 5);

        var hotkeys = new Label
        {
            Text = "Ctrl+Alt+Space - конвертировать текущее/последнее слово\r\nCtrl+Alt+Backspace - откатить последнее автоисправление",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(51, 65, 85),
        };
        root.Controls.Add(hotkeys, 0, 6);

        _lastActionLabel.Dock = DockStyle.Fill;
        _lastActionLabel.ForeColor = Color.FromArgb(71, 85, 105);
        root.Controls.Add(_lastActionLabel, 0, 7);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
        };
        var close = new Button
        {
            Text = "Свернуть",
            Width = 110,
            Height = 34,
        };
        close.Click += (_, _) => Close();
        var testSound = new Button
        {
            Text = "Проверить звук",
            Width = 140,
            Height = 34,
        };
        testSound.Click += (_, _) => SystemSounds.Asterisk.Play();
        buttons.Controls.Add(close);
        buttons.Controls.Add(testSound);
        root.Controls.Add(buttons, 0, 8);

        _autoSwitch.CheckedChanged += (_, _) =>
        {
            if (_autoSwitch.Focused)
            {
                _context.UpdateSettings(s => s.AutoSwitch = _autoSwitch.Checked);
            }
        };
        _sound.CheckedChanged += (_, _) =>
        {
            if (_sound.Focused)
            {
                _context.UpdateSettings(s => s.Sound = _sound.Checked);
            }
        };
        _startup.CheckedChanged += (_, _) =>
        {
            if (!_startup.Focused)
            {
                return;
            }

            StartupManager.SetEnabled(_startup.Checked);
            _context.UpdateSettings(s => s.StartWithWindows = _startup.Checked);
        };

        RefreshFromSettings();
    }

    public void RefreshFromSettings()
    {
        _autoSwitch.Checked = _context.Settings.AutoSwitch;
        _sound.Checked = _context.Settings.Sound;
        _startup.Checked = StartupManager.IsEnabled();
        _statusLabel.Text = "Состояние: " + _context.CurrentStatus;
        _lastActionLabel.Text = "Последнее действие: нет";
    }

    public void SetLastAction(string text)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => SetLastAction(text));
            return;
        }

        _lastActionLabel.Text = "Последнее действие: " + text;
        _statusLabel.Text = "Состояние: " + _context.CurrentStatus;
    }

    private static void ConfigureCheckBox(CheckBox checkBox, string text)
    {
        checkBox.Text = text;
        checkBox.Dock = DockStyle.Fill;
        checkBox.ForeColor = Color.FromArgb(24, 32, 43);
        checkBox.AutoSize = false;
    }
}

internal sealed class KeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmSyskeydown = 0x0104;
    private const int LlkInjected = 0x10;

    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private readonly Func<Keys, int, bool> _handler;
    private IntPtr _hookId;

    public KeyboardHook(Func<Keys, int, bool> handler)
    {
        _handler = handler;
        _proc = HookCallback;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = NativeMethods.GetModuleHandle(module?.ModuleName);
        _hookId = NativeMethods.SetWindowsHookEx(WhKeyboardLl, _proc, moduleHandle, 0);

        if (_hookId == IntPtr.Zero)
        {
            throw new InvalidOperationException("Не удалось установить глобальный keyboard hook.");
        }
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WmKeydown || wParam == WmSyskeydown))
        {
            var data = Marshal.PtrToStructure<NativeMethods.Kbdllhookstruct>(lParam);
            if ((data.Flags & LlkInjected) == 0)
            {
                var handled = _handler((Keys)data.VkCode, data.ScanCode);
                if (handled)
                {
                    return 1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
}

internal sealed record AppSettings
{
    public bool AutoSwitch { get; set; } = true;
    public bool Sound { get; set; } = true;
    public bool StartWithWindows { get; set; }
}

internal static class SettingsStore
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Switcher");

    private static readonly string SettingsPath = Path.Combine(DirectoryPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json, Encoding.UTF8);
    }
}

internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Switcher";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) is string value
            && value.Contains(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            key.SetValue(ValueName, $"\"{Application.ExecutablePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }
}

internal static class TrayIconFactory
{
    public static Icon Create()
    {
        var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new LinearGradientBrush(
            new Rectangle(0, 0, 32, 32),
            Color.FromArgb(24, 32, 43),
            Color.FromArgb(15, 118, 110),
            LinearGradientMode.ForwardDiagonal);
        graphics.FillEllipse(background, 1, 1, 30, 30);
        using var pen = new Pen(Color.FromArgb(226, 232, 240), 2.2F);
        graphics.DrawLine(pen, 9, 12, 21, 12);
        graphics.DrawLine(pen, 21, 12, 17, 8);
        graphics.DrawLine(pen, 21, 12, 17, 16);
        graphics.DrawLine(pen, 23, 20, 11, 20);
        graphics.DrawLine(pen, 11, 20, 15, 16);
        graphics.DrawLine(pen, 11, 20, 15, 24);
        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
            bitmap.Dispose();
        }
    }
}

internal sealed record LastTypedSegment(string Word, string Delimiter);

internal sealed record LastCorrection(
    string OriginalText,
    string CorrectedText,
    KeyboardLayout SourceLayout,
    KeyboardLayout TargetLayout);

internal sealed record CorrectionResult(
    string Text,
    LayoutDirection Direction,
    KeyboardLayout SourceLayout,
    KeyboardLayout TargetLayout);

internal enum LayoutDirection
{
    LatinToCyrillic,
    CyrillicToLatin,
}

internal enum KeyboardLayout
{
    English,
    Russian,
}

internal static class TextHeuristics
{
    private static readonly Dictionary<char, char> LatinToCyrillic = BuildLatinToCyrillic();
    private static readonly Dictionary<char, char> CyrillicToLatin = LatinToCyrillic.ToDictionary(pair => pair.Value, pair => pair.Key);

    private static readonly HashSet<string> RussianWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "привет", "пока", "спасибо", "пожалуйста", "да", "нет", "как", "что", "это", "этот",
        "эта", "эти", "там", "тут", "здесь", "меня", "тебя", "если", "или", "для",
        "без", "при", "про", "надо", "нужно", "можно", "будет", "было", "есть", "сегодня",
        "завтра", "вчера", "работа", "проект", "код", "тест", "текст", "окно", "сайт", "почта",
        "файл", "папка", "пароль", "логин", "настройка", "настройки", "ошибка", "исправить",
        "сделать", "отправить", "сообщение", "документ", "таблица", "календарь", "встреча",
        "созвон", "письмо", "задача", "список", "данные", "сервер", "клиент", "браузер",
        "страница", "кнопка", "форма", "поиск", "ответ", "вопрос", "пример", "команда",
        "сборка", "приложение", "переключатель", "раскладка", "русский", "английский",
        "система", "авто", "автоматически", "слово", "слова", "замена", "звук", "назад",
        "вперед", "отмена", "готово", "начать", "закрыть", "открыть", "сохранить", "удалить",
        "обновить", "скопировать", "вставить", "директор", "менеджер", "админ", "адрес",
        "телефон", "номер", "город", "москва", "россия", "время", "день", "ночь", "утро",
        "вечер", "хорошо", "плохо", "важно", "срочно", "проверка", "решение", "проблема",
        "версия", "релиз", "коммит", "ветка", "репозиторий",
    };

    private static readonly HashSet<string> EnglishWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "hello", "thanks", "thank", "please", "yes", "no", "the", "this", "that", "there",
        "here", "what", "when", "where", "why", "with", "without", "for", "from", "about",
        "into", "will", "would", "could", "should", "work", "project", "code", "test", "text",
        "window", "site", "email", "file", "folder", "password", "login", "settings", "error",
        "fix", "make", "send", "message", "document", "table", "calendar", "meeting", "call",
        "letter", "task", "list", "data", "server", "client", "browser", "page", "button",
        "form", "search", "answer", "question", "example", "command", "build", "application",
        "app", "switcher", "layout", "russian", "english", "system", "auto", "automatic",
        "word", "words", "replace", "sound", "back", "forward", "undo", "ready", "start",
        "close", "open", "save", "delete", "update", "copy", "paste", "admin", "address",
        "phone", "number", "city", "time", "day", "night", "morning", "evening", "good",
        "bad", "important", "urgent", "check", "solution", "problem", "version", "release",
        "commit", "branch", "repository", "user", "name", "value", "key", "token", "api",
        "my", "me", "we", "us", "it", "is", "am", "hi", "in", "on", "of", "to", "as", "at",
        "if", "do", "go", "he", "she", "you", "not", "and", "but", "can", "may",
        "github", "windows", "keyboard", "shortcut", "hotkey", "design",
    };

    private static readonly string[] RussianFragments =
    [
        "пр", "ст", "но", "то", "на", "ен", "ов", "ни", "ра", "ко", "по", "ре", "ть",
        "ого", "его", "ать", "ить", "ный", "ая", "ое", "ые", "ение", "ция",
    ];

    private static readonly string[] EnglishFragments =
    [
        "th", "he", "in", "er", "an", "re", "on", "at", "en", "nd", "st", "es", "or",
        "te", "ing", "ion", "tion", "ed", "ly", "ent", "ment", "ous",
    ];

    public static bool TryAutoCorrect(string word, out CorrectionResult correction)
    {
        correction = default!;
        if (word.Length < 2 || !TryConvertAny(word, out var converted))
        {
            return false;
        }

        var sourceScore = converted.Direction == LayoutDirection.LatinToCyrillic
            ? ScoreEnglish(word)
            : ScoreRussian(word);
        var targetScore = converted.Direction == LayoutDirection.LatinToCyrillic
            ? ScoreRussian(converted.Text)
            : ScoreEnglish(converted.Text);

        var sourceIsKnownWord = converted.Direction == LayoutDirection.LatinToCyrillic
            ? EnglishWords.Contains(word.ToLowerInvariant())
            : RussianWords.Contains(word.ToLowerInvariant());
        if (sourceIsKnownWord)
        {
            return false;
        }

        var targetIsKnownWord = converted.Direction == LayoutDirection.LatinToCyrillic
            ? RussianWords.Contains(converted.Text.ToLowerInvariant())
            : EnglishWords.Contains(converted.Text.ToLowerInvariant());

        if (word.Length < 4 && !targetIsKnownWord)
        {
            return false;
        }

        if (targetIsKnownWord && targetScore - sourceScore >= 8)
        {
            correction = converted;
            return true;
        }

        if (targetScore >= 22 && targetScore - sourceScore >= 14)
        {
            correction = converted;
            return true;
        }

        return false;
    }

    public static bool TryConvertAny(string word, out CorrectionResult correction)
    {
        correction = default!;
        if (!CanConvert(word))
        {
            return false;
        }

        if (word.All(IsLatin))
        {
            correction = new CorrectionResult(
                ConvertWithMap(word, LatinToCyrillic),
                LayoutDirection.LatinToCyrillic,
                KeyboardLayout.English,
                KeyboardLayout.Russian);
            return true;
        }

        if (word.All(IsCyrillic))
        {
            correction = new CorrectionResult(
                ConvertWithMap(word, CyrillicToLatin),
                LayoutDirection.CyrillicToLatin,
                KeyboardLayout.Russian,
                KeyboardLayout.English);
            return true;
        }

        return false;
    }

    public static bool CanConvert(string word)
    {
        return word.Length > 0 && (word.All(IsLatin) || word.All(IsCyrillic));
    }

    public static bool IsWordChar(char ch)
    {
        return IsLatin(ch) || IsCyrillic(ch);
    }

    public static bool IsDelimiter(char ch)
    {
        return char.IsWhiteSpace(ch)
            || ch is '.' or ',' or ';' or ':' or '!' or '?' or ')' or ']' or '}' or '"' or '\'';
    }

    private static int ScoreRussian(string word)
    {
        var lower = word.ToLowerInvariant();
        var score = 0;
        if (RussianWords.Contains(lower))
        {
            score += 30;
        }

        score += FragmentScore(lower, RussianFragments);
        score += VowelScore(lower, "аеёиоуыэюя");

        if (lower.Length >= 5 && !lower.Any(ch => "аеёиоуыэюя".Contains(ch)))
        {
            score -= 10;
        }

        if (lower.Contains("йй") || lower.Contains("щщ") || lower.Contains("ъъ"))
        {
            score -= 8;
        }

        return score;
    }

    private static int ScoreEnglish(string word)
    {
        var lower = word.ToLowerInvariant();
        var score = 0;
        if (EnglishWords.Contains(lower))
        {
            score += 30;
        }

        score += FragmentScore(lower, EnglishFragments);
        score += VowelScore(lower, "aeiouy");

        if (lower.Length >= 5 && !lower.Any(ch => "aeiouy".Contains(ch)))
        {
            score -= 10;
        }

        if (lower.Contains("qq") || lower.Contains("ww") || lower.Contains("jj") || lower.Contains("zx"))
        {
            score -= 7;
        }

        return score;
    }

    private static int FragmentScore(string lower, string[] fragments)
    {
        var score = 0;
        foreach (var fragment in fragments)
        {
            if (lower.Contains(fragment, StringComparison.Ordinal))
            {
                score += fragment.Length >= 3 ? 4 : 2;
            }
        }

        return Math.Min(score, 14);
    }

    private static int VowelScore(string lower, string vowels)
    {
        var vowelCount = lower.Count(vowels.Contains);
        if (vowelCount == 0)
        {
            return -4;
        }

        var ratio = (double)vowelCount / lower.Length;
        if (ratio is > 0.18 and < 0.7)
        {
            return 4;
        }

        return 0;
    }

    private static string ConvertWithMap(string text, IReadOnlyDictionary<char, char> map)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(map.TryGetValue(ch, out var converted) ? converted : ch);
        }

        return builder.ToString();
    }

    private static bool IsLatin(char ch)
    {
        return (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z');
    }

    private static bool IsCyrillic(char ch)
    {
        return (ch >= 'а' && ch <= 'я') || (ch >= 'А' && ch <= 'Я') || ch is 'ё' or 'Ё';
    }

    private static Dictionary<char, char> BuildLatinToCyrillic()
    {
        var map = new Dictionary<char, char>();
        AddPair(map, '`', 'ё');
        AddPair(map, 'q', 'й');
        AddPair(map, 'w', 'ц');
        AddPair(map, 'e', 'у');
        AddPair(map, 'r', 'к');
        AddPair(map, 't', 'е');
        AddPair(map, 'y', 'н');
        AddPair(map, 'u', 'г');
        AddPair(map, 'i', 'ш');
        AddPair(map, 'o', 'щ');
        AddPair(map, 'p', 'з');
        AddPair(map, '[', 'х');
        AddPair(map, ']', 'ъ');
        AddPair(map, 'a', 'ф');
        AddPair(map, 's', 'ы');
        AddPair(map, 'd', 'в');
        AddPair(map, 'f', 'а');
        AddPair(map, 'g', 'п');
        AddPair(map, 'h', 'р');
        AddPair(map, 'j', 'о');
        AddPair(map, 'k', 'л');
        AddPair(map, 'l', 'д');
        AddPair(map, ';', 'ж');
        AddPair(map, '\'', 'э');
        AddPair(map, 'z', 'я');
        AddPair(map, 'x', 'ч');
        AddPair(map, 'c', 'с');
        AddPair(map, 'v', 'м');
        AddPair(map, 'b', 'и');
        AddPair(map, 'n', 'т');
        AddPair(map, 'm', 'ь');
        AddPair(map, ',', 'б');
        AddPair(map, '.', 'ю');
        AddPair(map, '/', '.');
        AddPair(map, '~', 'Ё');
        AddPair(map, '{', 'Х');
        AddPair(map, '}', 'Ъ');
        AddPair(map, ':', 'Ж');
        AddPair(map, '"', 'Э');
        AddPair(map, '<', 'Б');
        AddPair(map, '>', 'Ю');
        AddPair(map, '?', ',');
        return map;
    }

    private static void AddPair(Dictionary<char, char> map, char latin, char cyrillic)
    {
        map[latin] = cyrillic;
        if (char.IsLetter(latin))
        {
            map[char.ToUpperInvariant(latin)] = char.ToUpperInvariant(cyrillic);
        }
    }
}

internal static class KeyboardState
{
    public static bool IsCtrlAltDown()
    {
        return IsKeyDown(Keys.ControlKey) && IsKeyDown(Keys.Menu);
    }

    public static bool HasCommandModifierDown()
    {
        return IsKeyDown(Keys.ControlKey) || IsKeyDown(Keys.Menu) || IsKeyDown(Keys.LWin) || IsKeyDown(Keys.RWin);
    }

    public static bool IsNavigationOrEditingKey(Keys key)
    {
        return key is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End
            or Keys.Delete or Keys.PageDown or Keys.PageUp or Keys.Escape or Keys.Tab or Keys.Enter;
    }

    private static bool IsKeyDown(Keys key)
    {
        return (NativeMethods.GetKeyState((int)key) & 0x8000) != 0;
    }
}

internal static class InputSender
{
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfUnicode = 0x0004;

    public static bool ReplaceText(int backspaceCount, string text)
    {
        if (backspaceCount <= 0 && string.IsNullOrEmpty(text))
        {
            return true;
        }

        var inputs = new List<NativeMethods.Input>((backspaceCount * 2) + (text.Length * 2));
        for (var i = 0; i < backspaceCount; i++)
        {
            inputs.Add(KeyboardInput((ushort)Keys.Back, 0, 0));
            inputs.Add(KeyboardInput((ushort)Keys.Back, 0, KeyeventfKeyup));
        }

        foreach (var ch in text)
        {
            inputs.Add(KeyboardInput(0, ch, KeyeventfUnicode));
            inputs.Add(KeyboardInput(0, ch, KeyeventfUnicode | KeyeventfKeyup));
        }

        return NativeMethods.SendKeyboardInput(inputs.ToArray());
    }

    private static NativeMethods.Input KeyboardInput(ushort virtualKey, ushort scanCode, uint flags)
    {
        return new NativeMethods.Input
        {
            Type = InputKeyboard,
            U = new NativeMethods.InputUnion
            {
                Ki = new NativeMethods.Keybdinput
                {
                    WVk = virtualKey,
                    WScan = scanCode,
                    DwFlags = flags,
                    Time = 0,
                    DwExtraInfo = UIntPtr.Zero,
                },
            },
        };
    }
}

internal static class NativeMethods
{
    private const uint KlfActivate = 0x00000001;
    private const int WmInputLangChangeRequest = 0x0050;
    private const string EnglishLayoutId = "00000409";
    private const string RussianLayoutId = "00000419";

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct Kbdllhookstruct
    {
        public int VkCode;
        public int ScanCode;
        public int Flags;
        public int Time;
        public IntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Input
    {
        public uint Type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public Keybdinput Ki;

        [FieldOffset(0)]
        public Mouseinput Mi;

        [FieldOffset(0)]
        public Hardwareinput Hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Keybdinput
    {
        public ushort WVk;
        public ushort WScan;
        public uint DwFlags;
        public uint Time;
        public UIntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Mouseinput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint DwFlags;
        public uint Time;
        public UIntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Hardwareinput
    {
        public uint UMsg;
        public ushort WParamL;
        public ushort WParamH;
    }

    public static bool SendKeyboardInput(Input[] inputs)
    {
        if (inputs.Length == 0)
        {
            return true;
        }

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        return sent == inputs.Length;
    }

    public static string TryTranslateKey(int vkCode, int scanCode)
    {
        var keyboardState = new byte[256];
        if (!GetKeyboardState(keyboardState))
        {
            return string.Empty;
        }

        if (vkCode is >= 0 and < 256)
        {
            keyboardState[vkCode] = 0x80;
        }

        var layout = GetForegroundKeyboardLayout();
        var buffer = new StringBuilder(8);
        var result = ToUnicodeEx((uint)vkCode, (uint)scanCode, keyboardState, buffer, buffer.Capacity, 0, layout);
        return result > 0 ? buffer.ToString(0, result) : string.Empty;
    }

    public static void SwitchForegroundLayout(KeyboardLayout layout)
    {
        var layoutId = layout == KeyboardLayout.Russian ? RussianLayoutId : EnglishLayoutId;
        var hkl = LoadKeyboardLayout(layoutId, KlfActivate);
        if (hkl == IntPtr.Zero)
        {
            return;
        }

        var foreground = GetForegroundWindow();
        if (foreground != IntPtr.Zero)
        {
            PostMessage(foreground, WmInputLangChangeRequest, IntPtr.Zero, hkl);
        }

        ActivateKeyboardLayout(hkl, 0);
    }

    private static IntPtr GetForegroundKeyboardLayout()
    {
        var foreground = GetForegroundWindow();
        var threadId = GetWindowThreadProcessId(foreground, out _);
        return GetKeyboardLayout(threadId);
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState(byte[] lpKeyState);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(
        uint wVirtKey,
        uint wScanCode,
        byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff,
        int cchBuff,
        uint wFlags,
        IntPtr dwhkl);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);

    [DllImport("user32.dll")]
    private static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
