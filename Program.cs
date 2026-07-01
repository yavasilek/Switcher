using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Switcher;

internal static class Program
{
    private const string MutexName = "Local\\Switcher.RuEn.AutoLayout";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0)
        {
            if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            {
                Environment.ExitCode = SelfTest.Run();
                return;
            }

            if (args.Contains("--install-startup", StringComparer.OrdinalIgnoreCase))
            {
                StartupManager.SetEnabled(true, Application.ExecutablePath);
                return;
            }

            if (args.Contains("--uninstall-startup", StringComparer.OrdinalIgnoreCase))
            {
                StartupManager.SetEnabled(false);
                return;
            }

            if (args.Contains("--install", StringComparer.OrdinalIgnoreCase))
            {
                InstallManager.InstallCurrentExecutable();
                return;
            }

            if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
            {
                InstallManager.RemoveInstalledCopy();
                return;
            }
        }

        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        if (!args.Contains("--portable", StringComparer.OrdinalIgnoreCase)
            && !args.Contains("--updated", StringComparer.OrdinalIgnoreCase)
            && InstallManager.ShouldPromptForPortableLaunch)
        {
            var choice = MessageBox.Show(
                "Switcher запущен как portable-версия.\r\n\r\nДа - установить в систему и запустить установленную копию.\r\nНет - продолжить portable-запуск.\r\nОтмена - закрыть.",
                "Switcher",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (choice == DialogResult.Cancel)
            {
                return;
            }

            if (choice == DialogResult.Yes)
            {
                var installedPath = InstallManager.InstallCurrentExecutable();
                mutex.ReleaseMutex();
                Process.Start(new ProcessStartInfo
                {
                    FileName = installedPath,
                    UseShellExecute = true,
                });
                return;
            }
        }

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
        CheckCustomAuto(failures);
        CheckExactAutoReplacement(failures);
        CheckTextConversion(failures, "ghbdtn vbh", "привет мир");
        CheckTextConversion(failures, "руддщ цщкдв", "hello world");
        CheckManual(failures, "vtyz", "меня");
        CheckManual(failures, "сщву", "code");
        CheckNoAuto(failures, "test");
        CheckNoAuto(failures, "code");
        CheckNeverCorrect(failures);
        CheckSettingsRoundTrip(failures);
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
        if (!TextHeuristics.TryAutoCorrect(input, new AppSettings(), out var correction) || correction.Text != expected)
        {
            failures.Add($"AUTO {input}: expected {expected}, actual {correction?.Text ?? "<none>"}");
        }
    }

    private static void CheckCustomAuto(List<string> failures)
    {
        var settings = new AppSettings
        {
            CustomEnglishWords = ["codex"],
        };

        if (!TextHeuristics.TryAutoCorrect("сщвуч", settings, out var correction) || correction.Text != "codex")
        {
            failures.Add($"CUSTOM сщвуч: expected codex, actual {correction?.Text ?? "<none>"}");
        }
    }

    private static void CheckExactAutoReplacement(List<string> failures)
    {
        var settings = new AppSettings
        {
            CustomAutoReplacements =
            [
                new AutoReplacementRule { Original = "test", Corrected = "тест" },
                new AutoReplacementRule { Original = "зкщпкфь", Corrected = "program" },
            ],
        };

        if (!TextHeuristics.TryAutoCorrect("test", settings, out var latinCorrection) || latinCorrection.Text != "тест")
        {
            failures.Add($"EXACT test: expected тест, actual {latinCorrection?.Text ?? "<none>"}");
        }

        if (!TextHeuristics.TryAutoCorrect("зкщпкфь", settings, out var cyrillicCorrection) || cyrillicCorrection.Text != "program")
        {
            failures.Add($"EXACT зкщпкфь: expected program, actual {cyrillicCorrection?.Text ?? "<none>"}");
        }
    }

    private static void CheckTextConversion(List<string> failures, string input, string expected)
    {
        if (!TextHeuristics.TryConvertText(input, out var correction) || correction.Text != expected)
        {
            failures.Add($"TEXT {input}: expected {expected}, actual {correction?.Text ?? "<none>"}");
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
        if (TextHeuristics.TryAutoCorrect(input, new AppSettings(), out var correction))
        {
            failures.Add($"NOAUTO {input}: unexpected {correction.Text}");
        }
    }

    private static void CheckNeverCorrect(List<string> failures)
    {
        var settings = new AppSettings
        {
            CustomNeverCorrectWords = ["ghbdtn"],
        };

        if (TextHeuristics.TryAutoCorrect("ghbdtn", settings, out var correction))
        {
            failures.Add($"NEVER ghbdtn: unexpected {correction.Text}");
        }
    }

    private static void CheckSettingsRoundTrip(List<string> failures)
    {
        var path = Path.Combine(Path.GetTempPath(), $"Switcher.settings.{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings
            {
                AutoSwitch = false,
                FirstRunHintShown = true,
                CustomEnglishWords = ["codex"],
                CustomRussianWords = ["пример"],
                CustomNeverCorrectWords = ["steam"],
                AggressiveShortWords = true,
                ClipboardFallback = true,
                AutoHealKeyboardHook = true,
                LearnFromManualCorrections = true,
                CustomAutoReplacements = [new AutoReplacementRule { Original = "ghbdtn", Corrected = "привет" }],
                ConvertWordHotkey = new HotkeyBinding { Ctrl = true, Alt = false, Key = Keys.F8 },
            };

            SettingsStore.SaveToFile(settings, path);
            var loaded = SettingsStore.LoadFromFile(path);
            if (loaded.AutoSwitch || !loaded.FirstRunHintShown)
            {
                failures.Add("SETTINGS roundtrip: boolean settings were not preserved");
            }

            if (!loaded.CustomEnglishWords.Contains("codex") || !loaded.CustomRussianWords.Contains("пример"))
            {
                failures.Add("SETTINGS roundtrip: custom dictionaries were not preserved");
            }

            if (!loaded.CustomNeverCorrectWords.Contains("steam"))
            {
                failures.Add("SETTINGS roundtrip: never-correct words were not preserved");
            }

            if (!loaded.AggressiveShortWords || !loaded.ClipboardFallback || !loaded.AutoHealKeyboardHook)
            {
                failures.Add("SETTINGS roundtrip: reliability toggles were not preserved");
            }

            if (!loaded.LearnFromManualCorrections || loaded.CustomAutoReplacements.Count != 1 || loaded.CustomAutoReplacements[0].Corrected != "привет")
            {
                failures.Add("SETTINGS roundtrip: auto replacement rules were not preserved");
            }

            if (loaded.ConvertWordHotkey.Key != Keys.F8 || !loaded.ConvertWordHotkey.Ctrl || loaded.ConvertWordHotkey.Alt)
            {
                failures.Add($"SETTINGS roundtrip: hotkey was not preserved, actual {loaded.ConvertWordHotkey}");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"SETTINGS roundtrip: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Best effort cleanup for self-test temp file.
            }
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
    private static readonly TimeSpan BackgroundUpdateDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(12);
    private static readonly TimeSpan HookStaleAfter = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan HookRepairCooldown = TimeSpan.FromMinutes(10);

    private readonly Control _uiThread = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly KeyboardHook _keyboardHook;
    private readonly System.Windows.Forms.Timer _healthTimer = new();
    private readonly StringBuilder _currentWord = new();
    private readonly DiagnosticState _diagnostics = new();
    private SettingsForm? _settingsForm;
    private LastTypedSegment? _lastTypedSegment;
    private LastCorrection? _lastCorrection;
    private UpdateInfo? _availableUpdate;
    private AppSettings _settings;

    public SwitcherApplicationContext()
    {
        _settings = SettingsStore.Load();
        _settings.StartWithWindows = StartupManager.IsEnabledForPath(InstallManager.PreferredStartupPath);
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
        _healthTimer.Interval = 60000;
        _healthTimer.Tick += (_, _) => CheckHookHealth();
        _healthTimer.Start();
        _ = CheckForUpdatesInBackgroundAsync();
        if (!_settings.FirstRunHintShown)
        {
            _ = ShowFirstRunHintLaterAsync();
        }
    }

    public AppSettings Settings => _settings;

    public string CurrentStatus
    {
        get
        {
            if (_settings.Paused)
            {
                return "пауза включена";
            }

            if (IsForegroundProcessExcluded(out var processName))
            {
                return $"пауза для {processName}";
            }

            var auto = _settings.AutoSwitch ? "авто включено" : "авто выключено";
            var sound = _settings.Sound ? "звук включен" : "звук выключен";
            return $"{auto}, {sound}";
        }
    }

    public IReadOnlyList<CorrectionHistoryItem> History => _settings.History;

    public UpdateInfo? AvailableUpdate => _availableUpdate;

    public string DiagnosticsText => BuildDiagnosticsText();

    public string InstallationStatus
    {
        get
        {
            if (InstallManager.IsCurrentInstanceInstalled)
            {
                return $"Установлено: {InstallManager.InstalledExePath}";
            }

            if (InstallManager.IsInstalled)
            {
                return $"Запущена portable-версия. Установленная копия: {InstallManager.InstalledExePath}";
            }

            return "Запущена portable-версия. В систему не установлено.";
        }
    }

    public void UpdateSettings(Action<AppSettings> update)
    {
        update(_settings);
        SettingsStore.Save(_settings);
        RefreshMenu();
        _settingsForm?.RefreshFromSettings();
    }

    public void ReplaceSettings(AppSettings settings)
    {
        _settings = settings;
        StartupManager.SetEnabled(_settings.StartWithWindows, InstallManager.PreferredStartupPath);
        _settings.StartWithWindows = StartupManager.IsEnabledForPath(InstallManager.PreferredStartupPath);
        SettingsStore.Save(_settings);
        ResetTypingState();
        RefreshMenu();
        _settingsForm?.RefreshFromSettings();
    }

    public void ExportSettings(string filePath)
    {
        SettingsStore.SaveToFile(_settings, filePath);
    }

    public void ImportSettings(string filePath)
    {
        ReplaceSettings(SettingsStore.LoadFromFile(filePath));
    }

    protected override void ExitThreadCore()
    {
        SettingsStore.Save(_settings);
        _healthTimer.Stop();
        _healthTimer.Dispose();
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
        var pauseItem = new ToolStripMenuItem("Пауза", null, (_, _) => TogglePause())
        {
            Name = "Paused",
        };
        var startupItem = new ToolStripMenuItem("Запускать с Windows", null, (_, _) =>
        {
            var enabled = !StartupManager.IsEnabledForPath(InstallManager.PreferredStartupPath);
            StartupManager.SetEnabled(enabled, InstallManager.PreferredStartupPath);
            UpdateSettings(s => s.StartWithWindows = enabled);
        })
        {
            Name = "Startup",
        };
        var installItem = new ToolStripMenuItem("Установить в систему", null, (_, _) => InstallToSystem())
        {
            Name = "Install",
        };
        var checkUpdatesItem = new ToolStripMenuItem("Проверить обновления", null, async (_, _) =>
        {
            if (_availableUpdate?.IsNewer == true)
            {
                await InstallUpdateAsync(_availableUpdate);
                return;
            }

            var checkedUpdate = await CheckForUpdatesAsync(showResult: true);
            if (checkedUpdate?.IsNewer == true)
            {
                await InstallUpdateAsync(checkedUpdate);
            }
        })
        {
            Name = "CheckUpdates",
        };
        var convertItem = new ToolStripMenuItem
        {
            Name = "ConvertWordHotkey",
            Enabled = false,
        };
        var convertSelectionItem = new ToolStripMenuItem
        {
            Name = "ConvertSelectionHotkey",
            Enabled = false,
        };
        var undoItem = new ToolStripMenuItem
        {
            Name = "UndoHotkey",
            Enabled = false,
        };
        var pauseHotkeyItem = new ToolStripMenuItem
        {
            Name = "PauseHotkey",
            Enabled = false,
        };
        var exitItem = new ToolStripMenuItem("Выход", null, (_, _) => ExitThread());

        menu.Items.AddRange([
            openItem,
            new ToolStripSeparator(),
            autoItem,
            soundItem,
            pauseItem,
            startupItem,
            installItem,
            checkUpdatesItem,
            new ToolStripSeparator(),
            convertItem,
            convertSelectionItem,
            undoItem,
            pauseHotkeyItem,
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

        if (menu.Items["Paused"] is ToolStripMenuItem pausedItem)
        {
            pausedItem.Checked = _settings.Paused;
            pausedItem.Text = _settings.Paused ? "Пауза: включена" : "Пауза: выключена";
        }

        if (menu.Items["Startup"] is ToolStripMenuItem startupItem)
        {
            _settings.StartWithWindows = StartupManager.IsEnabledForPath(InstallManager.PreferredStartupPath);
            startupItem.Checked = _settings.StartWithWindows;
            startupItem.Text = _settings.StartWithWindows ? "Запускать с Windows: да" : "Запускать с Windows: нет";
        }

        if (menu.Items["Install"] is ToolStripMenuItem installItem)
        {
            installItem.Enabled = !InstallManager.IsCurrentInstanceInstalled;
            installItem.Text = InstallManager.IsCurrentInstanceInstalled ? "Уже установлено в систему" : "Установить в систему";
        }

        if (menu.Items["CheckUpdates"] is ToolStripMenuItem checkUpdatesItem)
        {
            checkUpdatesItem.Text = _availableUpdate?.IsNewer == true
                ? $"Установить обновление {_availableUpdate.TagName}"
                : "Проверить обновления";
        }

        if (menu.Items["ConvertWordHotkey"] is ToolStripMenuItem convertWordItem)
        {
            convertWordItem.Text = $"Конвертировать последнее слово: {_settings.ConvertWordHotkey}";
        }

        if (menu.Items["ConvertSelectionHotkey"] is ToolStripMenuItem convertSelectionItem)
        {
            convertSelectionItem.Text = $"Конвертировать выделенный текст: {_settings.ConvertSelectionHotkey}";
        }

        if (menu.Items["UndoHotkey"] is ToolStripMenuItem undoItem)
        {
            undoItem.Text = $"Откатить автоисправление: {_settings.UndoHotkey}";
        }

        if (menu.Items["PauseHotkey"] is ToolStripMenuItem pauseHotkeyItem)
        {
            pauseHotkeyItem.Text = $"Пауза: {_settings.PauseHotkey}";
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
        _diagnostics.LastHookEventUtc = DateTime.UtcNow;
        _diagnostics.LastKey = $"{key} / scan {scanCode}";
        _diagnostics.CurrentWord = _currentWord.ToString();

        if (KeyboardState.Matches(_settings.PauseHotkey, key))
        {
            SetDiagnosticDecision($"Горячая клавиша паузы: {key}");
            PostToUi(TogglePause);
            return true;
        }

        if (_settings.Paused)
        {
            SetDiagnosticDecision("Пропуск: включена пауза");
            ResetTypingState();
            return false;
        }

        if (IsForegroundProcessExcluded(out var excludedProcess))
        {
            SetDiagnosticDecision($"Пропуск: процесс в исключениях ({excludedProcess})");
            ResetTypingState();
            return false;
        }

        if (KeyboardState.Matches(_settings.UndoHotkey, key))
        {
            SetDiagnosticDecision($"Горячая клавиша отката: {key}");
            PostToUi(UndoLastCorrection);
            return true;
        }

        if (KeyboardState.Matches(_settings.ConvertWordHotkey, key))
        {
            SetDiagnosticDecision($"Горячая клавиша конвертации слова: {key}");
            PostToUi(ConvertRecentWordManually);
            return true;
        }

        if (KeyboardState.Matches(_settings.ConvertSelectionHotkey, key))
        {
            SetDiagnosticDecision($"Горячая клавиша выделенного текста: {key}");
            PostToUi(ConvertSelectedTextManually);
            return true;
        }

        if (KeyboardState.HasCommandModifierDown())
        {
            SetDiagnosticDecision("Пропуск: нажата командная клавиша");
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
            SetDiagnosticDecision($"Сброс слова: навигационная клавиша {key}");
            ResetTypingState();
            return false;
        }

        var text = NativeMethods.TryTranslateKey((int)key, scanCode);
        if (string.IsNullOrEmpty(text))
        {
            SetDiagnosticDecision($"Пропуск: клавиша {key} не дала символ");
            return false;
        }

        var ch = text[0];
        if (char.IsControl(ch))
        {
            SetDiagnosticDecision("Сброс слова: управляющий символ");
            ResetTypingState();
            return false;
        }

        if (TextHeuristics.IsWordChar(ch))
        {
            InvalidateRecentActions();
            _currentWord.Append(ch);
            _diagnostics.CurrentWord = _currentWord.ToString();
            SetDiagnosticDecision($"Набор слова: {_diagnostics.CurrentWord}");
            return false;
        }

        if (!TextHeuristics.IsDelimiter(ch))
        {
            SetDiagnosticDecision($"Сброс слова: символ-разделитель не поддержан ({ch})");
            ResetTypingState();
            return false;
        }

        var word = _currentWord.ToString();
        _currentWord.Clear();
        var delimiter = ch.ToString();
        _diagnostics.LastWord = word;
        _diagnostics.CurrentWord = "";

        if (!_settings.AutoSwitch)
        {
            SetDiagnosticDecision($"Авто выключено, слово: {word}");
        }
        else if (TextHeuristics.TryAutoCorrect(word, _settings, out var correction, out var reason))
        {
            SetDiagnosticDecision($"Автозамена: {word} -> {correction.Text}. {reason}");
            PostToUi(() => ApplyCorrection(word, delimiter, correction));
            return true;
        }
        else
        {
            SetDiagnosticDecision($"Автозамены нет для '{word}': {reason}");
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

        if (!SwitchLayoutAndReplace(correction.TargetLayout, originalWord.Length, correctedText, out var inputMethod))
        {
            PlayErrorSound();
            UpdateBalloon("Ошибка ввода", "Windows заблокировала замену текста");
            return;
        }

        _lastCorrection = new LastCorrection(originalText, correctedText, correction.SourceLayout, correction.TargetLayout);
        _lastTypedSegment = null;

        AddHistory("Авто", originalText, correctedText);
        PlaySwitchSound(correction.Direction);
        UpdateBalloon("Автозамена", $"{originalText} -> {correctedText} ({inputMethod})");
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

            if (!SwitchLayoutAndReplace(correction.TargetLayout, word.Length, correction.Text, out var inputMethod))
            {
                PlayErrorSound();
                UpdateBalloon("Ошибка ввода", "Windows заблокировала замену текста");
                return;
            }

            _currentWord.Clear();
            _currentWord.Append(correction.Text);
            _lastCorrection = null;
            _lastTypedSegment = null;
            AddHistory("Ручная", word, correction.Text);
            MaybeLearnAutoReplacement(word, correction.Text);
            PlaySwitchSound(correction.Direction);
            UpdateBalloon("Ручная конвертация", $"{word} -> {correction.Text} ({inputMethod})");
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

        if (!SwitchLayoutAndReplace(segmentCorrection.TargetLayout, segment.Word.Length + segment.Delimiter.Length, segmentCorrection.Text + segment.Delimiter, out var segmentInputMethod))
        {
            PlayErrorSound();
            UpdateBalloon("Ошибка ввода", "Windows заблокировала замену текста");
            return;
        }

        _lastCorrection = null;
        _lastTypedSegment = null;
        AddHistory("Ручная", segment.Word + segment.Delimiter, segmentCorrection.Text + segment.Delimiter);
        MaybeLearnAutoReplacement(segment.Word, segmentCorrection.Text);
        PlaySwitchSound(segmentCorrection.Direction);
        UpdateBalloon("Ручная конвертация", $"{segment.Word} -> {segmentCorrection.Text} ({segmentInputMethod})");
    }

    private void ConvertSelectedTextManually()
    {
        using var clipboard = ClipboardBackup.Capture();

        Clipboard.Clear();
        InputSender.SendChord(Keys.ControlKey, Keys.C);
        var selectedText = ClipboardBackup.WaitForText(TimeSpan.FromMilliseconds(350));
        if (string.IsNullOrEmpty(selectedText))
        {
            clipboard.Restore();
            PlayErrorSound();
            UpdateBalloon("Выделение", "текст не найден");
            return;
        }

        if (!TextHeuristics.TryConvertText(selectedText, out var correction))
        {
            clipboard.Restore();
            PlayErrorSound();
            UpdateBalloon("Выделение", "нужен текст в одной раскладке");
            return;
        }

        var layoutSwitch = NativeMethods.SwitchForegroundLayout(correction.TargetLayout);
        StoreLayoutSwitch(layoutSwitch);
        Clipboard.SetText(correction.Text, TextDataFormat.UnicodeText);
        InputSender.SendChord(Keys.ControlKey, Keys.V);
        Thread.Sleep(120);
        clipboard.Restore();

        ResetTypingState();
        AddHistory("Выделение", selectedText, correction.Text);
        PlaySwitchSound(correction.Direction);
        UpdateBalloon("Выделенный текст", $"{TrimForStatus(selectedText)} -> {TrimForStatus(correction.Text)}");
    }

    private void UndoLastCorrection()
    {
        if (_lastCorrection is null)
        {
            PlayErrorSound();
            return;
        }

        var correction = _lastCorrection;
        if (!SwitchLayoutAndReplace(correction.SourceLayout, correction.CorrectedText.Length, correction.OriginalText, out var inputMethod))
        {
            PlayErrorSound();
            UpdateBalloon("Ошибка ввода", "Windows заблокировала замену текста");
            return;
        }

        _lastCorrection = null;
        _lastTypedSegment = null;
        _currentWord.Clear();
        AddHistory("Откат", correction.CorrectedText, correction.OriginalText);
        PlayErrorSound();
        UpdateBalloon("Откат", $"{correction.OriginalText.TrimEnd()} ({inputMethod})");
    }

    public void ClearHistory()
    {
        UpdateSettings(s => s.History.Clear());
    }

    public void TestSound(LayoutDirection direction)
    {
        PlaySwitchSound(direction);
    }

    public void TogglePause()
    {
        UpdateSettings(s => s.Paused = !s.Paused);
        UpdateBalloon("Пауза", _settings.Paused ? "включена" : "выключена");
    }

    public void ReinstallKeyboardHook()
    {
        try
        {
            _keyboardHook.Reinstall();
            _diagnostics.HookReinstallCount++;
            _diagnostics.LastHookReinstallUtc = DateTime.UtcNow;
            SetDiagnosticDecision("Keyboard hook переустановлен");
            MessageBox.Show("Keyboard hook переустановлен.", "Switcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SetDiagnosticFailure($"Не удалось переустановить hook: {ex.Message}");
            MessageBox.Show($"Не удалось переустановить hook:\r\n{ex.Message}", "Switcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void ShowFirstRunHint(bool force)
    {
        if (!force && _settings.FirstRunHintShown)
        {
            return;
        }

        _settings.FirstRunHintShown = true;
        SettingsStore.Save(_settings);

        var result = MessageBox.Show(
            $"Switcher работает в трее и исправляет слова после пробела или знака.\r\n\r\n" +
            $"{_settings.ConvertWordHotkey} - конвертировать текущее или последнее слово.\r\n" +
            $"{_settings.ConvertSelectionHotkey} - конвертировать выделенный текст.\r\n" +
            $"{_settings.UndoHotkey} - откатить последнюю автозамену.\r\n" +
            $"{_settings.PauseHotkey} - включить или выключить паузу.\r\n\r\n" +
            "Открыть настройки сейчас?",
            "Первый запуск Switcher",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

        if (result == DialogResult.Yes)
        {
            ShowSettings();
        }
    }

    public void InstallToSystem()
    {
        try
        {
            var installedPath = InstallManager.InstallCurrentExecutable();
            _settings.StartWithWindows = StartupManager.IsEnabledForPath(installedPath);
            SettingsStore.Save(_settings);
            RefreshMenu();
            _settingsForm?.RefreshFromSettings();

            if (!InstallManager.IsCurrentInstanceInstalled)
            {
                var result = MessageBox.Show(
                    $"Switcher установлен в:\r\n{installedPath}\r\n\r\nЗапустить установленную версию сейчас?",
                    "Switcher",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (result == DialogResult.Yes)
                {
                    InstallManager.ScheduleStartAfterExit(installedPath, Environment.ProcessId);
                    ExitThread();
                }
            }
            else
            {
                MessageBox.Show("Текущая копия уже установлена.", "Switcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось установить Switcher:\r\n{ex.Message}", "Switcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void UninstallFromSystem()
    {
        try
        {
            if (!InstallManager.IsInstalled)
            {
                MessageBox.Show("Установленная копия не найдена.", "Switcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (InstallManager.IsCurrentInstanceInstalled)
            {
                var result = MessageBox.Show(
                    "Установленная копия будет удалена после закрытия Switcher. Продолжить?",
                    "Switcher",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (result != DialogResult.Yes)
                {
                    return;
                }

                InstallManager.RemoveInstalledCopy();
                ExitThread();
                return;
            }

            InstallManager.RemoveInstalledCopy();
            _settings.StartWithWindows = false;
            SettingsStore.Save(_settings);
            RefreshMenu();
            _settingsForm?.RefreshFromSettings();
            MessageBox.Show("Установленная копия удалена.", "Switcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось удалить установленную копию:\r\n{ex.Message}", "Switcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public async Task<UpdateInfo?> CheckForUpdatesAsync(bool showResult)
    {
        try
        {
            var update = await UpdateManager.CheckLatestAsync();
            StoreUpdateCheckResult(update);
            if (showResult)
            {
                var message = update.IsNewer
                    ? $"Доступна новая версия {update.TagName}.\r\nТекущая версия: v{ApplicationInfo.CurrentVersionText}"
                    : $"Установлена актуальная версия v{ApplicationInfo.CurrentVersionText}.";
                MessageBox.Show(message, "Switcher", MessageBoxButtons.OK, update.IsNewer ? MessageBoxIcon.Information : MessageBoxIcon.None);
            }

            return update;
        }
        catch (Exception ex)
        {
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            SettingsStore.Save(_settings);
            if (showResult)
            {
                MessageBox.Show($"Не удалось проверить обновления:\r\n{ex.Message}", "Switcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return null;
        }
    }

    public async Task InstallUpdateAsync(UpdateInfo update)
    {
        try
        {
            var result = MessageBox.Show(
                $"Скачать и установить {update.TagName}?\r\n\r\nSwitcher будет перезапущен.",
                "Switcher",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
            {
                return;
            }

            var downloadedExe = await UpdateManager.DownloadUpdateAsync(update);
            InstallManager.ScheduleReplacement(downloadedExe, Application.ExecutablePath, Environment.ProcessId);
            ExitThread();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось установить обновление:\r\n{ex.Message}", "Switcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task CheckForUpdatesInBackgroundAsync()
    {
        if (!_settings.AutoCheckUpdates)
        {
            return;
        }

        if (_settings.LastUpdateCheckUtc is { } lastCheck
            && DateTime.UtcNow - lastCheck.ToUniversalTime() < UpdateCheckInterval)
        {
            return;
        }

        try
        {
            await Task.Delay(BackgroundUpdateDelay);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var update = await UpdateManager.CheckLatestAsync(timeout.Token);
            StoreUpdateCheckResult(update);
            if (update.IsNewer)
            {
                PostToUi(() =>
                {
                    _notifyIcon.ShowBalloonTip(
                        10000,
                        "Доступно обновление Switcher",
                        $"{update.TagName} готова к установке. Открой меню трея.",
                        ToolTipIcon.Info);
                    RefreshMenu();
                    _settingsForm?.RefreshFromSettings();
                });
            }
        }
        catch
        {
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            SettingsStore.Save(_settings);
        }
    }

    private async Task ShowFirstRunHintLaterAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(900));
            PostToUi(() => ShowFirstRunHint(force: false));
        }
        catch
        {
            // First-run help is optional and must never block startup.
        }
    }

    private void CheckHookHealth()
    {
        if (!_settings.AutoHealKeyboardHook)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var lastEvent = _keyboardHook.LastEventUtc ?? _diagnostics.LastHookEventUtc ?? _diagnostics.StartedAtUtc;
        var stale = now - lastEvent > HookStaleAfter;
        var cooldownPassed = _diagnostics.LastHookReinstallUtc is not { } lastRepair
            || now - lastRepair > HookRepairCooldown;
        if (!_keyboardHook.IsInstalled || (stale && cooldownPassed))
        {
            try
            {
                _keyboardHook.Reinstall();
                _diagnostics.HookReinstallCount++;
                _diagnostics.LastHookReinstallUtc = now;
                SetDiagnosticDecision(stale
                    ? "Keyboard hook переустановлен автоматически после долгой тишины"
                    : "Keyboard hook переустановлен автоматически");
            }
            catch (Exception ex)
            {
                SetDiagnosticFailure($"Автовосстановление hook не удалось: {ex.Message}");
            }
        }
    }

    private void StoreUpdateCheckResult(UpdateInfo update)
    {
        _availableUpdate = update.IsNewer ? update : null;
        _settings.LastUpdateCheckUtc = DateTime.UtcNow;
        SettingsStore.Save(_settings);
        PostToUi(() =>
        {
            RefreshMenu();
            _settingsForm?.RefreshFromSettings();
        });
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

        SoundPlayerNames.Play(direction == LayoutDirection.LatinToCyrillic
            ? _settings.EnToRuSound
            : _settings.RuToEnSound);
    }

    private void PlayErrorSound()
    {
        if (_settings.Sound)
        {
            SystemSounds.Exclamation.Play();
        }
    }

    private bool SwitchLayoutAndReplace(KeyboardLayout targetLayout, int backspaceCount, string text, out string inputMethod)
    {
        var layoutSwitch = NativeMethods.SwitchForegroundLayout(targetLayout);
        StoreLayoutSwitch(layoutSwitch);

        if (InputSender.ReplaceText(backspaceCount, text))
        {
            inputMethod = "Unicode SendInput";
            _diagnostics.LastInputMethod = inputMethod;
            return true;
        }

        if (_settings.ClipboardFallback && InputSender.ReplaceTextViaClipboard(backspaceCount, text))
        {
            inputMethod = "Clipboard fallback";
            _diagnostics.LastInputMethod = inputMethod;
            SetDiagnosticDecision($"Замена выполнена запасным способом: {inputMethod}");
            return true;
        }

        inputMethod = "ошибка ввода";
        _diagnostics.LastInputMethod = inputMethod;
        SetDiagnosticFailure("SendInput не принял замену текста, clipboard fallback не помог или выключен");
        return false;
    }

    private void StoreLayoutSwitch(LayoutSwitchResult result)
    {
        _diagnostics.LastLayoutSwitch = result.ToString();
        if (!result.Success)
        {
            SetDiagnosticFailure("Раскладка не подтвердила переключение: " + result);
        }
    }

    private void UpdateBalloon(string title, string text)
    {
        _notifyIcon.Text = "Switcher: " + CurrentStatus;
        _settingsForm?.SetLastAction($"{title}: {text}");
        _settingsForm?.RefreshDiagnostics();
    }

    private void AddHistory(string kind, string original, string corrected)
    {
        _settings.History.Insert(0, new CorrectionHistoryItem
        {
            Timestamp = DateTime.Now,
            Kind = kind,
            Original = original,
            Corrected = corrected,
        });

        if (_settings.History.Count > 30)
        {
            _settings.History.RemoveRange(30, _settings.History.Count - 30);
        }

        SettingsStore.Save(_settings);
        _settingsForm?.RefreshHistory();
    }

    private void MaybeLearnAutoReplacement(string original, string corrected)
    {
        if (!_settings.LearnFromManualCorrections
            || !TextHeuristics.CanLearnAutoReplacement(original, corrected)
            || !AutoReplacementRule.AddOrUpdate(_settings.CustomAutoReplacements, original, corrected, learned: true))
        {
            return;
        }

        SettingsStore.Save(_settings);
        _settingsForm?.RefreshReplacementRules();
    }

    private bool IsForegroundProcessExcluded(out string processName)
    {
        processName = NativeMethods.GetForegroundProcessName();
        return processName.Length > 0
            && _settings.ExcludedProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase);
    }

    private void SetDiagnosticDecision(string decision)
    {
        _diagnostics.LastDecision = $"{DateTime.Now:HH:mm:ss} {decision}";
        _settingsForm?.RefreshDiagnostics();
    }

    private void SetDiagnosticFailure(string failure)
    {
        _diagnostics.LastFailure = $"{DateTime.Now:HH:mm:ss} {failure}";
        _settingsForm?.RefreshDiagnostics();
    }

    private string BuildDiagnosticsText()
    {
        var foreground = NativeMethods.GetForegroundWindowInfo();
        var now = DateTime.UtcNow;
        var lastHook = _keyboardHook.LastEventUtc ?? _diagnostics.LastHookEventUtc;
        var hookAge = lastHook is null ? "нет событий" : FormatAge(now - lastHook.Value);
        var excluded = foreground.ProcessName.Length > 0
            && _settings.ExcludedProcesses.Contains(foreground.ProcessName, StringComparer.OrdinalIgnoreCase);
        var builder = new StringBuilder();
        builder.AppendLine($"Версия: v{ApplicationInfo.CurrentVersionText}");
        builder.AppendLine($"Статус: {CurrentStatus}");
        builder.AppendLine($"Hook: {(_keyboardHook.IsInstalled ? "установлен" : "не установлен")}");
        builder.AppendLine($"Последнее событие hook: {hookAge}");
        builder.AppendLine($"Переустановок hook: {_diagnostics.HookReinstallCount}");
        builder.AppendLine($"Последний ремонт hook: {FormatLocal(_diagnostics.LastHookReinstallUtc)}");
        builder.AppendLine();
        builder.AppendLine($"Активный процесс: {foreground.ProcessName}{(excluded ? " (исключён)" : "")}");
        builder.AppendLine($"Заголовок окна: {foreground.Title}");
        builder.AppendLine($"Путь: {foreground.ProcessPath}");
        builder.AppendLine($"Раскладка окна: {foreground.LayoutName}");
        builder.AppendLine();
        builder.AppendLine($"Текущее слово: {_diagnostics.CurrentWord}");
        builder.AppendLine($"Последнее слово: {_diagnostics.LastWord}");
        builder.AppendLine($"Последняя клавиша: {_diagnostics.LastKey}");
        builder.AppendLine($"Последнее решение: {_diagnostics.LastDecision}");
        builder.AppendLine($"Последняя ошибка: {_diagnostics.LastFailure}");
        builder.AppendLine($"Последняя смена раскладки: {_diagnostics.LastLayoutSwitch}");
        builder.AppendLine($"Последний ввод: {_diagnostics.LastInputMethod}");
        builder.AppendLine();
        builder.AppendLine($"Автоисправление: {(_settings.AutoSwitch ? "включено" : "выключено")}");
        builder.AppendLine($"Смелый режим коротких слов: {(_settings.AggressiveShortWords ? "включен" : "выключен")}");
        builder.AppendLine($"Clipboard fallback: {(_settings.ClipboardFallback ? "включен" : "выключен")}");
        builder.AppendLine($"Автовосстановление hook: {(_settings.AutoHealKeyboardHook ? "включено" : "выключено")}");
        return builder.ToString();
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalSeconds < 1)
        {
            return "только что";
        }

        if (age.TotalMinutes < 1)
        {
            return $"{age.TotalSeconds:N0} сек. назад";
        }

        if (age.TotalHours < 1)
        {
            return $"{age.TotalMinutes:N0} мин. назад";
        }

        return $"{age.TotalHours:N1} ч. назад";
    }

    private static string FormatLocal(DateTime? utc)
    {
        return utc is null ? "нет" : utc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
    }

    private static string TrimForStatus(string text)
    {
        var normalized = text.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 32 ? normalized : normalized[..29] + "...";
    }
}

internal sealed class SettingsForm : Form
{
    private readonly SwitcherApplicationContext _context;
    private readonly Label _statusLabel = new();
    private readonly Label _lastActionLabel = new();
    private readonly CheckBox _autoSwitch = new();
    private readonly CheckBox _sound = new();
    private readonly CheckBox _paused = new();
    private readonly CheckBox _startup = new();
    private readonly CheckBox _autoCheckUpdates = new();
    private readonly CheckBox _learnFromManualCorrections = new();
    private readonly CheckBox _aggressiveShortWords = new();
    private readonly CheckBox _clipboardFallback = new();
    private readonly CheckBox _autoHealKeyboardHook = new();
    private readonly ComboBox _enToRuSound = new();
    private readonly ComboBox _ruToEnSound = new();
    private readonly Label _hotkeysLabel = new();
    private readonly HotkeyRow _convertWordHotkey = new("Текущее или последнее слово");
    private readonly HotkeyRow _convertSelectionHotkey = new("Выделенный текст");
    private readonly HotkeyRow _undoHotkey = new("Откат автозамены");
    private readonly HotkeyRow _pauseHotkey = new("Пауза");
    private readonly TextBox _excludedProcesses = new();
    private readonly TextBox _russianWords = new();
    private readonly TextBox _englishWords = new();
    private readonly TextBox _neverCorrectWords = new();
    private readonly TextBox _autoReplacements = new();
    private readonly TextBox _diagnostics = new();
    private readonly ListBox _history = new();
    private readonly Label _installStatus = new();
    private readonly Label _updateStatus = new();
    private readonly Button _installButton = new();
    private readonly Button _uninstallButton = new();
    private readonly Button _checkUpdateButton = new();
    private readonly Button _installUpdateButton = new();
    private readonly Button _saveHotkeysButton = new();
    private readonly Button _resetHotkeysButton = new();
    private readonly Button _saveReplacementsButton = new();
    private readonly Button _clearLearnedReplacementsButton = new();
    private readonly Button _refreshDiagnosticsButton = new();
    private readonly Button _reinstallHookButton = new();
    private readonly Button _exportSettingsButton = new();
    private readonly Button _importSettingsButton = new();
    private readonly Button _showFirstRunHintButton = new();
    private UpdateInfo? _latestUpdate;
    private bool _updating;

    public SettingsForm(SwitcherApplicationContext context)
    {
        _context = context;
        Text = "Switcher";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(860, 650);
        ClientSize = new Size(920, 680);
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(247, 249, 252);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            RowCount = 4,
            ColumnCount = 1,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "Switcher",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 20F),
            ForeColor = Color.FromArgb(24, 32, 43),
        }, 0, 0);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.ForeColor = Color.FromArgb(71, 85, 105);
        root.Controls.Add(_statusLabel, 0, 1);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Multiline = true,
        };
        tabs.TabPages.Add(CreateGeneralTab());
        tabs.TabPages.Add(CreateListsTab());
        tabs.TabPages.Add(CreateReplacementsTab());
        tabs.TabPages.Add(CreateSettingsTab());
        tabs.TabPages.Add(CreateHistoryTab());
        tabs.TabPages.Add(CreateDiagnosticsTab());
        tabs.TabPages.Add(CreateInstallTab());
        root.Controls.Add(tabs, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
        };
        var close = new Button
        {
            Text = "Свернуть",
            Width = 130,
            Height = 36,
        };
        close.Click += (_, _) => Close();
        buttons.Controls.Add(close);
        root.Controls.Add(buttons, 0, 3);

        BindEvents();
        RefreshFromSettings();
    }

    public void RefreshFromSettings()
    {
        _updating = true;
        _autoSwitch.Checked = _context.Settings.AutoSwitch;
        _sound.Checked = _context.Settings.Sound;
        _paused.Checked = _context.Settings.Paused;
        _startup.Checked = StartupManager.IsEnabledForPath(InstallManager.PreferredStartupPath);
        _autoCheckUpdates.Checked = _context.Settings.AutoCheckUpdates;
        _learnFromManualCorrections.Checked = _context.Settings.LearnFromManualCorrections;
        _aggressiveShortWords.Checked = _context.Settings.AggressiveShortWords;
        _clipboardFallback.Checked = _context.Settings.ClipboardFallback;
        _autoHealKeyboardHook.Checked = _context.Settings.AutoHealKeyboardHook;
        SetComboValue(_enToRuSound, _context.Settings.EnToRuSound);
        SetComboValue(_ruToEnSound, _context.Settings.RuToEnSound);
        _convertWordHotkey.SetBinding(_context.Settings.ConvertWordHotkey);
        _convertSelectionHotkey.SetBinding(_context.Settings.ConvertSelectionHotkey);
        _undoHotkey.SetBinding(_context.Settings.UndoHotkey);
        _pauseHotkey.SetBinding(_context.Settings.PauseHotkey);
        _excludedProcesses.Text = LinesFrom(_context.Settings.ExcludedProcesses);
        _russianWords.Text = LinesFrom(_context.Settings.CustomRussianWords);
        _englishWords.Text = LinesFrom(_context.Settings.CustomEnglishWords);
        _neverCorrectWords.Text = LinesFrom(_context.Settings.CustomNeverCorrectWords);
        _autoReplacements.Text = ReplacementLinesFrom(_context.Settings.CustomAutoReplacements);
        _updating = false;

        UpdateStatus();
        UpdateHotkeysLabel();
        RefreshHistory();
        RefreshDiagnostics();
        RefreshInstallState();
    }

    public void RefreshReplacementRules()
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(RefreshReplacementRules);
            return;
        }

        if (!_autoReplacements.Focused)
        {
            _autoReplacements.Text = ReplacementLinesFrom(_context.Settings.CustomAutoReplacements);
        }
    }

    public void RefreshHistory()
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(RefreshHistory);
            return;
        }

        _history.BeginUpdate();
        _history.Items.Clear();
        foreach (var item in _context.History)
        {
            _history.Items.Add(item.ToString());
        }

        _history.EndUpdate();
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
        UpdateStatus();
    }

    public void RefreshDiagnostics()
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(RefreshDiagnostics);
            return;
        }

        if (!_diagnostics.Focused)
        {
            _diagnostics.Text = _context.DiagnosticsText;
        }
    }

    private TabPage CreateGeneralTab()
    {
        var page = new TabPage("Основное");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            RowCount = 12,
            ColumnCount = 1,
        };
        for (var i = 0; i < 7; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        }

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        ConfigureCheckBox(_autoSwitch, "Автоматически исправлять слова после пробела или знака");
        ConfigureCheckBox(_sound, "Проигрывать звук при смене раскладки");
        ConfigureCheckBox(_paused, "Пауза");
        ConfigureCheckBox(_startup, "Запускать вместе с Windows");
        ConfigureCheckBox(_aggressiveShortWords, "Смелее исправлять короткие слова");
        ConfigureCheckBox(_clipboardFallback, "Использовать буфер обмена, если обычная замена не сработала");
        ConfigureCheckBox(_autoHealKeyboardHook, "Автоматически переустанавливать keyboard hook после долгой тишины");
        layout.Controls.Add(_autoSwitch, 0, 0);
        layout.Controls.Add(_sound, 0, 1);
        layout.Controls.Add(_paused, 0, 2);
        layout.Controls.Add(_startup, 0, 3);
        layout.Controls.Add(_aggressiveShortWords, 0, 4);
        layout.Controls.Add(_clipboardFallback, 0, 5);
        layout.Controls.Add(_autoHealKeyboardHook, 0, 6);

        _hotkeysLabel.Dock = DockStyle.Fill;
        _hotkeysLabel.ForeColor = Color.FromArgb(51, 65, 85);
        layout.Controls.Add(_hotkeysLabel, 0, 7);

        var soundGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
        };
        soundGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        soundGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        soundGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        ConfigureCombo(_enToRuSound);
        ConfigureCombo(_ruToEnSound);
        AddSoundRow(soundGrid, 0, "EN -> RU", _enToRuSound, () => _context.TestSound(LayoutDirection.LatinToCyrillic));
        AddSoundRow(soundGrid, 1, "RU -> EN", _ruToEnSound, () => _context.TestSound(LayoutDirection.CyrillicToLatin));
        layout.Controls.Add(soundGrid, 0, 8);

        _lastActionLabel.Dock = DockStyle.Fill;
        _lastActionLabel.ForeColor = Color.FromArgb(71, 85, 105);
        layout.Controls.Add(_lastActionLabel, 0, 10);

        return page;
    }

    private TabPage CreateSettingsTab()
    {
        var page = new TabPage("Настройки");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            RowCount = 8,
            ColumnCount = 1,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        layout.Controls.Add(new Label
        {
            Text = "Горячие клавиши",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 10F),
            ForeColor = Color.FromArgb(24, 32, 43),
        }, 0, 0);

        var hotkeyGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 5,
        };
        hotkeyGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        hotkeyGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        hotkeyGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        hotkeyGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        hotkeyGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 5; row++)
        {
            hotkeyGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        }

        AddHeader(hotkeyGrid, 0, "Действие");
        AddHeader(hotkeyGrid, 1, "Ctrl");
        AddHeader(hotkeyGrid, 2, "Alt");
        AddHeader(hotkeyGrid, 3, "Shift");
        AddHeader(hotkeyGrid, 4, "Клавиша");
        _convertWordHotkey.AddTo(hotkeyGrid, 1);
        _convertSelectionHotkey.AddTo(hotkeyGrid, 2);
        _undoHotkey.AddTo(hotkeyGrid, 3);
        _pauseHotkey.AddTo(hotkeyGrid, 4);
        layout.Controls.Add(hotkeyGrid, 0, 1);

        var hotkeyButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
        };
        ConfigureButton(_saveHotkeysButton, "Сохранить клавиши", 200);
        ConfigureButton(_resetHotkeysButton, "Вернуть стандартные", 210);
        hotkeyButtons.Controls.Add(_saveHotkeysButton);
        hotkeyButtons.Controls.Add(_resetHotkeysButton);
        layout.Controls.Add(hotkeyButtons, 0, 2);

        layout.Controls.Add(new Label
        {
            Text = "Перенос настроек",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 10F),
            ForeColor = Color.FromArgb(24, 32, 43),
        }, 0, 4);

        var dataButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
        };
        ConfigureButton(_exportSettingsButton, "Экспорт JSON", 160);
        ConfigureButton(_importSettingsButton, "Импорт JSON", 160);
        ConfigureButton(_showFirstRunHintButton, "Показать подсказку", 210);
        dataButtons.Controls.Add(_exportSettingsButton);
        dataButtons.Controls.Add(_importSettingsButton);
        dataButtons.Controls.Add(_showFirstRunHintButton);
        layout.Controls.Add(dataButtons, 0, 5);

        layout.Controls.Add(new Label
        {
            Text = "Экспорт сохраняет основные настройки, списки, историю, звуки, автозапуск и горячие клавиши в один JSON-файл. При импорте текущие настройки заменяются.",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(71, 85, 105),
        }, 0, 6);

        return page;
    }

    private TabPage CreateListsTab()
    {
        var page = new TabPage("Списки");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 4,
            RowCount = 3,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        page.Controls.Add(layout);

        AddHeader(layout, 0, "Исключённые процессы");
        AddHeader(layout, 1, "Русские слова");
        AddHeader(layout, 2, "Английские слова");
        AddHeader(layout, 3, "Никогда не исправлять");
        ConfigureTextArea(_excludedProcesses);
        ConfigureTextArea(_russianWords);
        ConfigureTextArea(_englishWords);
        ConfigureTextArea(_neverCorrectWords);
        layout.Controls.Add(_excludedProcesses, 0, 1);
        layout.Controls.Add(_russianWords, 1, 1);
        layout.Controls.Add(_englishWords, 2, 1);
        layout.Controls.Add(_neverCorrectWords, 3, 1);

        var save = new Button
        {
            Text = "Сохранить списки",
            Dock = DockStyle.Right,
            Width = 150,
            Height = 32,
        };
        save.Click += (_, _) =>
        {
            _context.UpdateSettings(s =>
            {
                s.ExcludedProcesses = ParseLines(_excludedProcesses.Text);
                s.CustomRussianWords = ParseLines(_russianWords.Text);
                s.CustomEnglishWords = ParseLines(_englishWords.Text);
                s.CustomNeverCorrectWords = ParseLines(_neverCorrectWords.Text);
            });
        };
        layout.Controls.Add(save, 3, 2);

        return page;
    }

    private TabPage CreateReplacementsTab()
    {
        var page = new TabPage("Замены");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            RowCount = 5,
            ColumnCount = 1,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        page.Controls.Add(layout);

        ConfigureCheckBox(_learnFromManualCorrections, "Учиться на ручной конвертации слов");
        layout.Controls.Add(_learnFromManualCorrections, 0, 0);

        layout.Controls.Add(new Label
        {
            Text = "Точечные автозамены применяются до эвристики и словаря. Формат: исходное -> исправленное, например ghbdtn -> привет.",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(71, 85, 105),
        }, 0, 1);

        ConfigureTextArea(_autoReplacements);
        layout.Controls.Add(_autoReplacements, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
        };
        ConfigureButton(_saveReplacementsButton, "Сохранить замены", 200);
        ConfigureButton(_clearLearnedReplacementsButton, "Очистить выученные", 220);
        buttons.Controls.Add(_saveReplacementsButton);
        buttons.Controls.Add(_clearLearnedReplacementsButton);
        layout.Controls.Add(buttons, 0, 3);

        layout.Controls.Add(new Label
        {
            Text = "Обучение добавляет только ручные исправления одиночных слов в разных раскладках. Выделенный текст не добавляется в правила.",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(71, 85, 105),
        }, 0, 4);

        return page;
    }

    private TabPage CreateHistoryTab()
    {
        var page = new TabPage("История");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            RowCount = 2,
            ColumnCount = 1,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        page.Controls.Add(layout);

        _history.Dock = DockStyle.Fill;
        _history.HorizontalScrollbar = true;
        layout.Controls.Add(_history, 0, 0);

        var clear = new Button
        {
            Text = "Очистить историю",
            Dock = DockStyle.Right,
            Width = 150,
            Height = 32,
        };
        clear.Click += (_, _) => _context.ClearHistory();
        layout.Controls.Add(clear, 0, 1);
        return page;
    }

    private TabPage CreateDiagnosticsTab()
    {
        var page = new TabPage("Диагностика");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            RowCount = 2,
            ColumnCount = 1,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        page.Controls.Add(layout);

        ConfigureTextArea(_diagnostics);
        _diagnostics.ReadOnly = true;
        layout.Controls.Add(_diagnostics, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
        };
        ConfigureButton(_refreshDiagnosticsButton, "Обновить диагностику", 220);
        ConfigureButton(_reinstallHookButton, "Переустановить hook", 210);
        buttons.Controls.Add(_refreshDiagnosticsButton);
        buttons.Controls.Add(_reinstallHookButton);
        layout.Controls.Add(buttons, 0, 1);
        return page;
    }

    private TabPage CreateInstallTab()
    {
        var page = new TabPage("Установка");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            RowCount = 9,
            ColumnCount = 1,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        layout.Controls.Add(new Label
        {
            Text = $"Версия: v{ApplicationInfo.CurrentVersionText}",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 10F),
        }, 0, 0);

        _installStatus.Dock = DockStyle.Fill;
        _installStatus.ForeColor = Color.FromArgb(51, 65, 85);
        _installStatus.AutoEllipsis = false;
        layout.Controls.Add(_installStatus, 0, 1);

        var installButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
        };
        ConfigureButton(_installButton, "Установить в систему", 230);
        ConfigureButton(_uninstallButton, "Удалить установку", 190);
        installButtons.Controls.Add(_installButton);
        installButtons.Controls.Add(_uninstallButton);
        layout.Controls.Add(installButtons, 0, 2);

        layout.Controls.Add(new Label
        {
            Text = "Обновления",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 10F),
        }, 0, 3);

        ConfigureCheckBox(_autoCheckUpdates, "Проверять новые версии автоматически в фоне");
        layout.Controls.Add(_autoCheckUpdates, 0, 4);

        _updateStatus.Dock = DockStyle.Fill;
        _updateStatus.ForeColor = Color.FromArgb(51, 65, 85);
        _updateStatus.AutoEllipsis = false;
        layout.Controls.Add(_updateStatus, 0, 5);

        var updateButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
        };
        ConfigureButton(_checkUpdateButton, "Проверить обновления", 230);
        ConfigureButton(_installUpdateButton, "Установить обновление", 240);
        _installUpdateButton.Enabled = false;
        updateButtons.Controls.Add(_checkUpdateButton);
        updateButtons.Controls.Add(_installUpdateButton);
        layout.Controls.Add(updateButtons, 0, 6);

        layout.Controls.Add(new Label
        {
            Text = "Установка выполняется без прав администратора в профиль пользователя. Обновление скачивает последний Switcher.exe из GitHub Releases и заменяет текущий файл после закрытия приложения.",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(71, 85, 105),
        }, 0, 7);

        return page;
    }

    private void BindEvents()
    {
        _autoSwitch.CheckedChanged += (_, _) =>
        {
            if (!_updating)
            {
                _context.UpdateSettings(s => s.AutoSwitch = _autoSwitch.Checked);
            }
        };
        _sound.CheckedChanged += (_, _) =>
        {
            if (!_updating)
            {
                _context.UpdateSettings(s => s.Sound = _sound.Checked);
            }
        };
        _paused.CheckedChanged += (_, _) =>
        {
            if (!_updating)
            {
                _context.UpdateSettings(s => s.Paused = _paused.Checked);
            }
        };
        _startup.CheckedChanged += (_, _) =>
        {
            if (_updating)
            {
                return;
            }

            StartupManager.SetEnabled(_startup.Checked, InstallManager.PreferredStartupPath);
            _context.UpdateSettings(s => s.StartWithWindows = _startup.Checked);
        };
        _autoCheckUpdates.CheckedChanged += (_, _) =>
        {
            if (!_updating)
            {
                _context.UpdateSettings(s => s.AutoCheckUpdates = _autoCheckUpdates.Checked);
            }
        };
        _learnFromManualCorrections.CheckedChanged += (_, _) =>
        {
            if (!_updating)
            {
                _context.UpdateSettings(s => s.LearnFromManualCorrections = _learnFromManualCorrections.Checked);
            }
        };
        _aggressiveShortWords.CheckedChanged += (_, _) =>
        {
            if (!_updating)
            {
                _context.UpdateSettings(s => s.AggressiveShortWords = _aggressiveShortWords.Checked);
            }
        };
        _clipboardFallback.CheckedChanged += (_, _) =>
        {
            if (!_updating)
            {
                _context.UpdateSettings(s => s.ClipboardFallback = _clipboardFallback.Checked);
            }
        };
        _autoHealKeyboardHook.CheckedChanged += (_, _) =>
        {
            if (!_updating)
            {
                _context.UpdateSettings(s => s.AutoHealKeyboardHook = _autoHealKeyboardHook.Checked);
            }
        };
        _enToRuSound.SelectedIndexChanged += (_, _) =>
        {
            if (!_updating && _enToRuSound.SelectedItem is string value)
            {
                _context.UpdateSettings(s => s.EnToRuSound = value);
            }
        };
        _ruToEnSound.SelectedIndexChanged += (_, _) =>
        {
            if (!_updating && _ruToEnSound.SelectedItem is string value)
            {
                _context.UpdateSettings(s => s.RuToEnSound = value);
            }
        };
        _installButton.Click += (_, _) =>
        {
            _context.InstallToSystem();
            RefreshInstallState();
        };
        _uninstallButton.Click += (_, _) =>
        {
            _context.UninstallFromSystem();
            RefreshInstallState();
        };
        _checkUpdateButton.Click += async (_, _) =>
        {
            await CheckUpdatesFromFormAsync();
        };
        _installUpdateButton.Click += async (_, _) =>
        {
            if (_latestUpdate is not null)
            {
                await _context.InstallUpdateAsync(_latestUpdate);
            }
        };
        _saveHotkeysButton.Click += (_, _) => SaveHotkeys();
        _resetHotkeysButton.Click += (_, _) => ResetHotkeys();
        _saveReplacementsButton.Click += (_, _) => SaveReplacements();
        _clearLearnedReplacementsButton.Click += (_, _) => ClearLearnedReplacements();
        _refreshDiagnosticsButton.Click += (_, _) => RefreshDiagnostics();
        _reinstallHookButton.Click += (_, _) => _context.ReinstallKeyboardHook();
        _exportSettingsButton.Click += (_, _) => ExportSettings();
        _importSettingsButton.Click += (_, _) => ImportSettings();
        _showFirstRunHintButton.Click += (_, _) => _context.ShowFirstRunHint(force: true);
    }

    private void UpdateStatus()
    {
        _statusLabel.Text = "Состояние: " + _context.CurrentStatus;
        if (string.IsNullOrWhiteSpace(_lastActionLabel.Text))
        {
            _lastActionLabel.Text = "Последнее действие: нет";
        }
    }

    private void UpdateHotkeysLabel()
    {
        _hotkeysLabel.Text =
            $"{_context.Settings.ConvertWordHotkey} - конвертировать текущее/последнее слово\r\n" +
            $"{_context.Settings.ConvertSelectionHotkey} - конвертировать выделенный текст\r\n" +
            $"{_context.Settings.UndoHotkey} - откатить последнее автоисправление\r\n" +
            $"{_context.Settings.PauseHotkey} - включить или выключить паузу";
    }

    private void SaveHotkeys()
    {
        var hotkeys = new[]
        {
            _convertWordHotkey.Binding,
            _convertSelectionHotkey.Binding,
            _undoHotkey.Binding,
            _pauseHotkey.Binding,
        };

        if (hotkeys.Any(binding => !binding.IsUsable))
        {
            MessageBox.Show(
                this,
                "Каждая комбинация должна содержать Ctrl или Alt и основную клавишу.",
                "Switcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (HotkeyBinding.HasDuplicates(hotkeys))
        {
            MessageBox.Show(
                this,
                "Комбинации не должны повторяться.",
                "Switcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _context.UpdateSettings(s =>
        {
            s.ConvertWordHotkey = _convertWordHotkey.Binding;
            s.ConvertSelectionHotkey = _convertSelectionHotkey.Binding;
            s.UndoHotkey = _undoHotkey.Binding;
            s.PauseHotkey = _pauseHotkey.Binding;
        });
    }

    private void ResetHotkeys()
    {
        _context.UpdateSettings(s =>
        {
            s.ConvertWordHotkey = AppSettings.DefaultConvertWordHotkey();
            s.ConvertSelectionHotkey = AppSettings.DefaultConvertSelectionHotkey();
            s.UndoHotkey = AppSettings.DefaultUndoHotkey();
            s.PauseHotkey = AppSettings.DefaultPauseHotkey();
        });
    }

    private void SaveReplacements()
    {
        var replacements = ParseReplacementRules(_autoReplacements.Text, out var errors);
        if (errors.Count > 0)
        {
            MessageBox.Show(
                this,
                "Не удалось сохранить замены:\r\n" + string.Join("\r\n", errors.Take(8)),
                "Switcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _context.UpdateSettings(s => s.CustomAutoReplacements = replacements);
        MessageBox.Show(this, "Точечные замены сохранены.", "Switcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ClearLearnedReplacements()
    {
        var learnedCount = _context.Settings.CustomAutoReplacements.Count(rule => rule.Learned);
        if (learnedCount == 0)
        {
            MessageBox.Show(this, "Выученных замен пока нет.", "Switcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Удалить выученные замены: {learnedCount}?",
            "Switcher",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (result != DialogResult.Yes)
        {
            return;
        }

        _context.UpdateSettings(s => s.CustomAutoReplacements = s.CustomAutoReplacements
            .Where(rule => !rule.Learned)
            .ToList());
    }

    private void ExportSettings()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Экспорт настроек Switcher",
            Filter = "JSON (*.json)|*.json|Все файлы (*.*)|*.*",
            FileName = $"Switcher-settings-{DateTime.Now:yyyyMMdd-HHmm}.json",
            AddExtension = true,
            DefaultExt = "json",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _context.ExportSettings(dialog.FileName);
            MessageBox.Show(this, "Настройки экспортированы.", "Switcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Не удалось экспортировать настройки:\r\n{ex.Message}", "Switcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportSettings()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Импорт настроек Switcher",
            Filter = "JSON (*.json)|*.json|Все файлы (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            "Текущие настройки будут заменены настройками из файла. Продолжить?",
            "Switcher",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (result != DialogResult.Yes)
        {
            return;
        }

        try
        {
            _context.ImportSettings(dialog.FileName);
            MessageBox.Show(this, "Настройки импортированы.", "Switcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Не удалось импортировать настройки:\r\n{ex.Message}", "Switcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshInstallState()
    {
        if (_context.AvailableUpdate?.IsNewer == true)
        {
            _latestUpdate = _context.AvailableUpdate;
        }

        _installStatus.Text = _context.InstallationStatus;
        _installButton.Enabled = !InstallManager.IsCurrentInstanceInstalled;
        _uninstallButton.Enabled = InstallManager.IsInstalled;
        _updateStatus.Text = BuildUpdateStatusText();
        _installUpdateButton.Enabled = _latestUpdate?.IsNewer == true;
    }

    private string BuildUpdateStatusText()
    {
        if (_latestUpdate is not null)
        {
            return _latestUpdate.IsNewer
                ? $"Доступна версия {_latestUpdate.TagName}."
                : $"Установлена актуальная версия v{ApplicationInfo.CurrentVersionText}.";
        }

        return _context.Settings.LastUpdateCheckUtc is { } lastCheck
            ? $"Последняя фоновая проверка: {lastCheck.ToLocalTime():dd.MM.yyyy HH:mm}. Новых версий не найдено."
            : "Обновления ещё не проверялись.";
    }

    private async Task CheckUpdatesFromFormAsync()
    {
        _checkUpdateButton.Enabled = false;
        _installUpdateButton.Enabled = false;
        _updateStatus.Text = "Проверяю GitHub Releases...";
        try
        {
            _latestUpdate = await _context.CheckForUpdatesAsync(showResult: true);
        }
        finally
        {
            _checkUpdateButton.Enabled = true;
            RefreshInstallState();
        }
    }

    private static void ConfigureCheckBox(CheckBox checkBox, string text)
    {
        checkBox.Text = text;
        checkBox.Dock = DockStyle.Fill;
        checkBox.ForeColor = Color.FromArgb(24, 32, 43);
        checkBox.AutoSize = false;
        checkBox.TextAlign = ContentAlignment.MiddleLeft;
    }

    private static void ConfigureCombo(ComboBox combo)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Dock = DockStyle.Fill;
        combo.Items.AddRange(SoundPlayerNames.All.Cast<object>().ToArray());
    }

    private static void ConfigureButton(Button button, string text, int width)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 36;
    }

    private static void ConfigureTextArea(TextBox textBox)
    {
        textBox.Dock = DockStyle.Fill;
        textBox.Multiline = true;
        textBox.ScrollBars = ScrollBars.Vertical;
        textBox.AcceptsReturn = true;
        textBox.AcceptsTab = false;
        textBox.WordWrap = false;
        textBox.Font = new Font("Consolas", 10F);
    }

    private static void AddSoundRow(TableLayoutPanel grid, int row, string label, ComboBox combo, Action test)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        grid.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, row);
        grid.Controls.Add(combo, 1, row);
        var button = new Button
        {
            Text = "Проверить",
            Dock = DockStyle.Fill,
            MinimumSize = new Size(140, 34),
        };
        button.Click += (_, _) => test();
        grid.Controls.Add(button, 2, row);
    }

    private static void AddHeader(TableLayoutPanel layout, int column, string text)
    {
        layout.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 10F),
            ForeColor = Color.FromArgb(24, 32, 43),
        }, column, 0);
    }

    private static void SetComboValue(ComboBox combo, string value)
    {
        combo.SelectedItem = SoundPlayerNames.All.Contains(value) ? value : combo.Items[0];
    }

    private static List<string> ParseLines(string text)
    {
        return text
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string LinesFrom(IEnumerable<string> values)
    {
        return string.Join(Environment.NewLine, values);
    }

    private static List<AutoReplacementRule> ParseReplacementRules(string text, out List<string> errors)
    {
        errors = [];
        var rules = new List<AutoReplacementRule>();
        var lineNumber = 0;
        foreach (var rawLine in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (!TrySplitReplacementRule(line, out var original, out var corrected))
            {
                errors.Add($"Строка {lineNumber}: нужен формат исходное -> исправленное.");
                continue;
            }

            if (!AutoReplacementRule.IsValidText(original) || !AutoReplacementRule.IsValidText(corrected))
            {
                errors.Add($"Строка {lineNumber}: слова должны быть без пробелов и не длиннее 64 символов.");
                continue;
            }

            if (string.Equals(original, corrected, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Строка {lineNumber}: исходное и исправленное слово совпадают.");
                continue;
            }

            rules.Add(new AutoReplacementRule
            {
                Original = original,
                Corrected = corrected,
                Learned = false,
                CreatedAt = DateTime.Now,
            });
        }

        return AutoReplacementRule.Normalize(rules);
    }

    private static bool TrySplitReplacementRule(string line, out string original, out string corrected)
    {
        foreach (var separator in new[] { "->", "=>", "\t", ";" })
        {
            var index = line.IndexOf(separator, StringComparison.Ordinal);
            if (index <= 0)
            {
                continue;
            }

            original = line[..index].Trim();
            corrected = line[(index + separator.Length)..].Trim();
            return original.Length > 0 && corrected.Length > 0;
        }

        original = "";
        corrected = "";
        return false;
    }

    private static string ReplacementLinesFrom(IEnumerable<AutoReplacementRule> rules)
    {
        return string.Join(Environment.NewLine, rules.Select(rule => $"{rule.Original} -> {rule.Corrected}"));
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
    private readonly object _sync = new();
    private IntPtr _hookId;

    public KeyboardHook(Func<Keys, int, bool> handler)
    {
        _handler = handler;
        _proc = HookCallback;
        Install();
    }

    public DateTime? LastEventUtc { get; private set; }

    public bool IsInstalled
    {
        get
        {
            lock (_sync)
            {
                return _hookId != IntPtr.Zero;
            }
        }
    }

    public void Reinstall()
    {
        lock (_sync)
        {
            Uninstall();
            Install();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            Uninstall();
        }
    }

    private void Install()
    {
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = NativeMethods.GetModuleHandle(module?.ModuleName);
        _hookId = NativeMethods.SetWindowsHookEx(WhKeyboardLl, _proc, moduleHandle, 0);

        if (_hookId == IntPtr.Zero)
        {
            throw new InvalidOperationException("Не удалось установить глобальный keyboard hook.");
        }
    }

    private void Uninstall()
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
                LastEventUtc = DateTime.UtcNow;
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

internal sealed class HotkeyBinding
{
    public bool Ctrl { get; set; } = true;
    public bool Alt { get; set; } = true;
    public bool Shift { get; set; }
    public Keys Key { get; set; } = Keys.None;

    public bool IsUsable => (Ctrl || Alt) && AllowedKeys.Contains(Key);

    public static IReadOnlyList<Keys> AllowedKeys { get; } = BuildAllowedKeys();

    public static HotkeyBinding CtrlAlt(Keys key)
    {
        return new HotkeyBinding
        {
            Ctrl = true,
            Alt = true,
            Shift = false,
            Key = key,
        };
    }

    public static HotkeyBinding Normalize(HotkeyBinding? binding, HotkeyBinding fallback)
    {
        if (binding is null || !binding.IsUsable)
        {
            return fallback.Clone();
        }

        return binding.Clone();
    }

    public static bool SameChord(HotkeyBinding left, HotkeyBinding right)
    {
        return left.Ctrl == right.Ctrl
            && left.Alt == right.Alt
            && left.Shift == right.Shift
            && left.Key == right.Key;
    }

    public static bool HasDuplicates(IEnumerable<HotkeyBinding> bindings)
    {
        var list = bindings.ToList();
        return list
            .Where(binding => binding.IsUsable)
            .Select(binding => $"{binding.Ctrl}:{binding.Alt}:{binding.Shift}:{binding.Key}")
            .Distinct(StringComparer.Ordinal)
            .Count() != list.Count(binding => binding.IsUsable);
    }

    public static string KeyDisplayName(Keys key)
    {
        return key switch
        {
            Keys.Space => "Space",
            Keys.Enter => "Enter",
            Keys.Back => "Backspace",
            Keys.Tab => "Tab",
            Keys.Escape => "Esc",
            _ => key.ToString(),
        };
    }

    public HotkeyBinding Clone()
    {
        return new HotkeyBinding
        {
            Ctrl = Ctrl,
            Alt = Alt,
            Shift = Shift,
            Key = Key,
        };
    }

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Ctrl)
        {
            parts.Add("Ctrl");
        }

        if (Alt)
        {
            parts.Add("Alt");
        }

        if (Shift)
        {
            parts.Add("Shift");
        }

        parts.Add(KeyDisplayName(Key));
        return string.Join("+", parts);
    }

    private static Keys[] BuildAllowedKeys()
    {
        var keys = new List<Keys>
        {
            Keys.Space,
            Keys.Enter,
            Keys.Back,
            Keys.Tab,
            Keys.Escape,
        };

        for (var key = (int)Keys.A; key <= (int)Keys.Z; key++)
        {
            keys.Add((Keys)key);
        }

        for (var key = (int)Keys.F1; key <= (int)Keys.F12; key++)
        {
            keys.Add((Keys)key);
        }

        return keys.ToArray();
    }
}

internal sealed class HotkeyRow
{
    private readonly Label _action = new();
    private readonly CheckBox _ctrl = new();
    private readonly CheckBox _alt = new();
    private readonly CheckBox _shift = new();
    private readonly ComboBox _key = new();

    public HotkeyRow(string action)
    {
        _action.Text = action;
        _action.Dock = DockStyle.Fill;
        _action.TextAlign = ContentAlignment.MiddleLeft;
        _ctrl.Dock = DockStyle.Fill;
        _alt.Dock = DockStyle.Fill;
        _shift.Dock = DockStyle.Fill;
        _key.Dock = DockStyle.Fill;
        _key.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var key in HotkeyBinding.AllowedKeys)
        {
            _key.Items.Add(new KeyOption(key));
        }
    }

    public HotkeyBinding Binding => new()
    {
        Ctrl = _ctrl.Checked,
        Alt = _alt.Checked,
        Shift = _shift.Checked,
        Key = SelectedKey,
    };

    public void AddTo(TableLayoutPanel layout, int row)
    {
        layout.Controls.Add(_action, 0, row);
        layout.Controls.Add(_ctrl, 1, row);
        layout.Controls.Add(_alt, 2, row);
        layout.Controls.Add(_shift, 3, row);
        layout.Controls.Add(_key, 4, row);
    }

    public void SetBinding(HotkeyBinding binding)
    {
        _ctrl.Checked = binding.Ctrl;
        _alt.Checked = binding.Alt;
        _shift.Checked = binding.Shift;
        foreach (var item in _key.Items.OfType<KeyOption>())
        {
            if (item.Key == binding.Key)
            {
                _key.SelectedItem = item;
                return;
            }
        }

        _key.SelectedIndex = 0;
    }

    private Keys SelectedKey => _key.SelectedItem is KeyOption option
        ? option.Key
        : HotkeyBinding.AllowedKeys[0];

    private sealed record KeyOption(Keys Key)
    {
        public override string ToString()
        {
            return HotkeyBinding.KeyDisplayName(Key);
        }
    }
}

internal sealed record AppSettings
{
    public static HotkeyBinding DefaultConvertWordHotkey() => HotkeyBinding.CtrlAlt(Keys.Space);
    public static HotkeyBinding DefaultConvertSelectionHotkey() => HotkeyBinding.CtrlAlt(Keys.Enter);
    public static HotkeyBinding DefaultUndoHotkey() => HotkeyBinding.CtrlAlt(Keys.Back);
    public static HotkeyBinding DefaultPauseHotkey() => HotkeyBinding.CtrlAlt(Keys.P);

    public bool AutoSwitch { get; set; } = true;
    public bool Paused { get; set; }
    public bool Sound { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool AutoCheckUpdates { get; set; } = true;
    public bool LearnFromManualCorrections { get; set; } = true;
    public bool AggressiveShortWords { get; set; }
    public bool ClipboardFallback { get; set; } = true;
    public bool AutoHealKeyboardHook { get; set; } = true;
    public bool FirstRunHintShown { get; set; }
    public DateTime? LastUpdateCheckUtc { get; set; }
    public string EnToRuSound { get; set; } = SoundPlayerNames.Asterisk;
    public string RuToEnSound { get; set; } = SoundPlayerNames.Question;
    public HotkeyBinding ConvertWordHotkey { get; set; } = DefaultConvertWordHotkey();
    public HotkeyBinding ConvertSelectionHotkey { get; set; } = DefaultConvertSelectionHotkey();
    public HotkeyBinding UndoHotkey { get; set; } = DefaultUndoHotkey();
    public HotkeyBinding PauseHotkey { get; set; } = DefaultPauseHotkey();
    public List<string> ExcludedProcesses { get; set; } =
    [
        "1password",
        "bitwarden",
        "keepass",
        "keepassxc",
        "lastpass",
        "cmd",
        "powershell",
        "pwsh",
        "wt",
        "windowsterminal",
    ];

    public List<string> CustomRussianWords { get; set; } = [];
    public List<string> CustomEnglishWords { get; set; } = [];
    public List<string> CustomNeverCorrectWords { get; set; } = [];
    public List<AutoReplacementRule> CustomAutoReplacements { get; set; } = [];
    public List<CorrectionHistoryItem> History { get; set; } = [];
}

internal sealed record AutoReplacementRule
{
    public string Original { get; set; } = "";
    public string Corrected { get; set; } = "";
    public bool Learned { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public static bool IsValidText(string text)
    {
        return text.Length is > 0 and <= 64 && !text.Any(char.IsWhiteSpace);
    }

    public static List<AutoReplacementRule> Normalize(IEnumerable<AutoReplacementRule> rules)
    {
        var normalized = new Dictionary<string, AutoReplacementRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            var original = rule.Original.Trim();
            var corrected = rule.Corrected.Trim();
            if (!IsValidText(original)
                || !IsValidText(corrected)
                || string.Equals(original, corrected, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            normalized[original] = new AutoReplacementRule
            {
                Original = original,
                Corrected = corrected,
                Learned = rule.Learned,
                CreatedAt = rule.CreatedAt == default ? DateTime.Now : rule.CreatedAt,
            };
        }

        return normalized.Values.ToList();
    }

    public static bool AddOrUpdate(List<AutoReplacementRule> rules, string original, string corrected, bool learned)
    {
        original = original.Trim();
        corrected = corrected.Trim();
        if (!IsValidText(original) || !IsValidText(corrected))
        {
            return false;
        }

        var existing = rules.FirstOrDefault(rule => string.Equals(rule.Original, original, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (string.Equals(existing.Corrected, corrected, StringComparison.Ordinal)
                && existing.Learned == learned)
            {
                return false;
            }

            existing.Original = original;
            existing.Corrected = corrected;
            existing.Learned = existing.Learned && learned;
            return true;
        }

        rules.Insert(0, new AutoReplacementRule
        {
            Original = original,
            Corrected = corrected,
            Learned = learned,
            CreatedAt = DateTime.Now,
        });
        return true;
    }
}

internal sealed record CorrectionHistoryItem
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Kind { get; set; } = "";
    public string Original { get; set; } = "";
    public string Corrected { get; set; } = "";

    public override string ToString()
    {
        return $"{Timestamp:HH:mm:ss} {Kind}: {Original} -> {Corrected}";
    }
}

internal static class SoundPlayerNames
{
    public const string None = "Без звука";
    public const string Asterisk = "Asterisk";
    public const string Question = "Question";
    public const string Beep = "Beep";
    public const string Exclamation = "Exclamation";
    public const string Hand = "Hand";

    public static readonly string[] All = [Asterisk, Question, Beep, Exclamation, Hand, None];

    public static void Play(string name)
    {
        switch (name)
        {
            case Asterisk:
                SystemSounds.Asterisk.Play();
                break;
            case Question:
                SystemSounds.Question.Play();
                break;
            case Beep:
                SystemSounds.Beep.Play();
                break;
            case Exclamation:
                SystemSounds.Exclamation.Play();
                break;
            case Hand:
                SystemSounds.Hand.Play();
                break;
        }
    }
}

internal static class SettingsStore
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Switcher");

    private static readonly string SettingsPath = Path.Combine(DirectoryPath, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            return Normalize(JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings());
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static AppSettings LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Файл настроек не найден.", path);
        }

        var json = File.ReadAllText(path, Encoding.UTF8);
        return Normalize(JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
            ?? throw new InvalidOperationException("Файл настроек пустой или повреждён."));
    }

    public static void Save(AppSettings settings)
    {
        SaveToFile(settings, SettingsPath);
    }

    public static void SaveToFile(AppSettings settings, string path)
    {
        Normalize(settings);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.EnToRuSound = NormalizeSound(settings.EnToRuSound, SoundPlayerNames.Asterisk);
        settings.RuToEnSound = NormalizeSound(settings.RuToEnSound, SoundPlayerNames.Question);
        settings.ConvertWordHotkey = HotkeyBinding.Normalize(settings.ConvertWordHotkey, AppSettings.DefaultConvertWordHotkey());
        settings.ConvertSelectionHotkey = HotkeyBinding.Normalize(settings.ConvertSelectionHotkey, AppSettings.DefaultConvertSelectionHotkey());
        settings.UndoHotkey = HotkeyBinding.Normalize(settings.UndoHotkey, AppSettings.DefaultUndoHotkey());
        settings.PauseHotkey = HotkeyBinding.Normalize(settings.PauseHotkey, AppSettings.DefaultPauseHotkey());
        if (HotkeyBinding.HasDuplicates([
            settings.ConvertWordHotkey,
            settings.ConvertSelectionHotkey,
            settings.UndoHotkey,
            settings.PauseHotkey,
        ]))
        {
            settings.ConvertWordHotkey = AppSettings.DefaultConvertWordHotkey();
            settings.ConvertSelectionHotkey = AppSettings.DefaultConvertSelectionHotkey();
            settings.UndoHotkey = AppSettings.DefaultUndoHotkey();
            settings.PauseHotkey = AppSettings.DefaultPauseHotkey();
        }

        settings.ExcludedProcesses ??= [];
        settings.CustomRussianWords ??= [];
        settings.CustomEnglishWords ??= [];
        settings.CustomNeverCorrectWords ??= [];
        settings.CustomAutoReplacements ??= [];
        settings.History ??= [];
        settings.ExcludedProcesses = NormalizeList(settings.ExcludedProcesses);
        settings.CustomRussianWords = NormalizeList(settings.CustomRussianWords);
        settings.CustomEnglishWords = NormalizeList(settings.CustomEnglishWords);
        settings.CustomNeverCorrectWords = NormalizeList(settings.CustomNeverCorrectWords);
        settings.CustomAutoReplacements = AutoReplacementRule.Normalize(settings.CustomAutoReplacements);
        settings.History = settings.History
            .Where(item => !string.IsNullOrWhiteSpace(item.Original) || !string.IsNullOrWhiteSpace(item.Corrected))
            .Take(30)
            .ToList();
        return settings;
    }

    private static string NormalizeSound(string? value, string fallback)
    {
        return SoundPlayerNames.All.Contains(value) ? value! : fallback;
    }

    private static List<string> NormalizeList(IEnumerable<string> values)
    {
        return values
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Switcher";

    public static bool IsEnabled()
    {
        return IsEnabledForPath(Application.ExecutablePath);
    }

    public static bool IsEnabledForPath(string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) is string value
            && value.Contains(executablePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled, string? executablePath = null)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            key.SetValue(ValueName, $"\"{executablePath ?? Application.ExecutablePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }
}

internal static class ApplicationInfo
{
    public const string Repository = "yavasilek/Switcher";
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/yavasilek/Switcher/releases/latest";
    public const string ReleasesUrl = "https://github.com/yavasilek/Switcher/releases";

    public static Version CurrentVersion { get; } = GetCurrentVersion();

    public static string CurrentVersionText => CurrentVersion.ToString(3);

    private static Version GetCurrentVersion()
    {
        var informational = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+', 2)[0];

        if (Version.TryParse(informational, out var version))
        {
            return version;
        }

        return typeof(Program).Assembly.GetName().Version ?? new Version(0, 0, 0);
    }
}

internal static class InstallManager
{
    private const string AppFolderName = "Switcher";
    private const string ExeName = "Switcher.exe";

    public static string InstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        AppFolderName);

    public static string InstalledExePath => Path.Combine(InstallDirectory, ExeName);

    public static string PreferredStartupPath => IsInstalled ? InstalledExePath : Application.ExecutablePath;

    public static bool IsCurrentInstanceInstalled => SamePath(Application.ExecutablePath, InstalledExePath);

    public static bool IsInstalled => File.Exists(InstalledExePath);

    public static bool ShouldPromptForPortableLaunch => !IsCurrentInstanceInstalled;

    public static string InstallCurrentExecutable()
    {
        Directory.CreateDirectory(InstallDirectory);
        var current = Path.GetFullPath(Application.ExecutablePath);
        var installed = Path.GetFullPath(InstalledExePath);

        if (!SamePath(current, installed))
        {
            File.Copy(current, installed, true);
        }

        StartupManager.SetEnabled(true, installed);
        CreateStartMenuShortcut(installed);
        return installed;
    }

    public static void RemoveInstalledCopy()
    {
        StartupManager.SetEnabled(false);
        DeleteStartMenuShortcut();
        if (IsCurrentInstanceInstalled)
        {
            ScheduleDirectoryDeleteAfterExit(InstallDirectory, Environment.ProcessId);
            return;
        }

        if (Directory.Exists(InstallDirectory))
        {
            Directory.Delete(InstallDirectory, true);
        }
    }

    public static void ScheduleReplacement(string sourceExe, string targetExe, int processId)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"Switcher.Update.{Guid.NewGuid():N}.cmd");
        var script = $"""
@echo off
setlocal
:wait
tasklist /FI "PID eq {processId}" 2>NUL | find "{processId}" >NUL
if not errorlevel 1 (
  timeout /t 1 /nobreak >NUL
  goto wait
)
copy /Y "{sourceExe}" "{targetExe}" >NUL
del /F /Q "{sourceExe}" >NUL 2>NUL
start "" "{targetExe}" --updated
del "%~f0" >NUL 2>NUL
""";
        File.WriteAllText(scriptPath, script, Encoding.ASCII);
        StartHidden("cmd.exe", $"/c \"{scriptPath}\"");
    }

    public static void ScheduleStartAfterExit(string executablePath, int processId)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"Switcher.Start.{Guid.NewGuid():N}.cmd");
        var script = $"""
@echo off
setlocal
:wait
tasklist /FI "PID eq {processId}" 2>NUL | find "{processId}" >NUL
if not errorlevel 1 (
  timeout /t 1 /nobreak >NUL
  goto wait
)
start "" "{executablePath}"
del "%~f0" >NUL 2>NUL
""";
        File.WriteAllText(scriptPath, script, Encoding.ASCII);
        StartHidden("cmd.exe", $"/c \"{scriptPath}\"");
    }

    private static void ScheduleDirectoryDeleteAfterExit(string directory, int processId)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"Switcher.Uninstall.{Guid.NewGuid():N}.cmd");
        var script = $"""
@echo off
setlocal
:wait
tasklist /FI "PID eq {processId}" 2>NUL | find "{processId}" >NUL
if not errorlevel 1 (
  timeout /t 1 /nobreak >NUL
  goto wait
)
rmdir /S /Q "{directory}" >NUL 2>NUL
del "%~f0" >NUL 2>NUL
""";
        File.WriteAllText(scriptPath, script, Encoding.ASCII);
        StartHidden("cmd.exe", $"/c \"{scriptPath}\"");
    }

    private static void CreateStartMenuShortcut(string installedExe)
    {
        var shortcutPath = GetStartMenuShortcutPath();
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        var scriptPath = Path.Combine(Path.GetTempPath(), $"Switcher.Shortcut.{Guid.NewGuid():N}.ps1");
        var script = $"""
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut('{EscapePowerShellSingleQuoted(shortcutPath)}')
$shortcut.TargetPath = '{EscapePowerShellSingleQuoted(installedExe)}'
$shortcut.WorkingDirectory = '{EscapePowerShellSingleQuoted(Path.GetDirectoryName(installedExe)!)}'
$shortcut.Description = 'Switcher'
$shortcut.Save()
Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
""";
        File.WriteAllText(scriptPath, script, Encoding.UTF8);
        StartHidden("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"");
    }

    private static void DeleteStartMenuShortcut()
    {
        var shortcutPath = GetStartMenuShortcutPath();
        if (File.Exists(shortcutPath))
        {
            File.Delete(shortcutPath);
        }
    }

    private static string GetStartMenuShortcutPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            "Switcher.lnk");
    }

    private static string EscapePowerShellSingleQuoted(string value)
    {
        return value.Replace("'", "''");
    }

    private static void StartHidden(string fileName, string arguments)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = false,
        });
    }

    private static bool SamePath(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record UpdateInfo(
    string TagName,
    Version Version,
    string PageUrl,
    string DownloadUrl,
    long Size,
    bool IsNewer);

internal static class UpdateManager
{
    public static async Task<UpdateInfo> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(TimeSpan.FromSeconds(6));
        using var response = await client.GetAsync(ApplicationInfo.LatestReleaseApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tagName = root.GetProperty("tag_name").GetString() ?? "v0.0.0";
        var pageUrl = root.TryGetProperty("html_url", out var htmlUrl)
            ? htmlUrl.GetString() ?? ApplicationInfo.ReleasesUrl
            : ApplicationInfo.ReleasesUrl;

        var versionText = tagName.TrimStart('v', 'V');
        if (!Version.TryParse(versionText, out var latestVersion))
        {
            latestVersion = new Version(0, 0, 0);
        }

        var asset = root
            .GetProperty("assets")
            .EnumerateArray()
            .FirstOrDefault(item => string.Equals(item.GetProperty("name").GetString(), "Switcher.exe", StringComparison.OrdinalIgnoreCase));

        if (asset.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException("В последнем релизе не найден Switcher.exe.");
        }

        var downloadUrl = asset.GetProperty("browser_download_url").GetString()
            ?? throw new InvalidOperationException("В релизе нет ссылки на скачивание Switcher.exe.");
        var size = asset.TryGetProperty("size", out var sizeElement) ? sizeElement.GetInt64() : 0;

        return new UpdateInfo(
            tagName,
            latestVersion,
            pageUrl,
            downloadUrl,
            size,
            latestVersion > ApplicationInfo.CurrentVersion);
    }

    public static async Task<string> DownloadUpdateAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"Switcher.{update.TagName}.exe");
        using var client = CreateClient(TimeSpan.FromMinutes(10));
        using var response = await client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(tempPath);
        await source.CopyToAsync(target, cancellationToken);
        return tempPath;
    }

    private static System.Net.Http.HttpClient CreateClient(TimeSpan timeout)
    {
        var client = new System.Net.Http.HttpClient
        {
            Timeout = timeout,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Switcher/{ApplicationInfo.CurrentVersionText}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}

internal sealed class ClipboardBackup : IDisposable
{
    private readonly IDataObject? _dataObject;
    private bool _restored;

    private ClipboardBackup(IDataObject? dataObject)
    {
        _dataObject = dataObject;
    }

    public static ClipboardBackup Capture()
    {
        try
        {
            return new ClipboardBackup(Clipboard.GetDataObject());
        }
        catch
        {
            return new ClipboardBackup(null);
        }
    }

    public static string WaitForText(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                {
                    return Clipboard.GetText(TextDataFormat.UnicodeText);
                }
            }
            catch
            {
                return string.Empty;
            }

            Application.DoEvents();
            Thread.Sleep(25);
        }

        return string.Empty;
    }

    public void Restore()
    {
        if (_restored)
        {
            return;
        }

        _restored = true;
        try
        {
            if (_dataObject is null)
            {
                Clipboard.Clear();
            }
            else
            {
                Clipboard.SetDataObject(_dataObject, true);
            }
        }
        catch
        {
            // Clipboard ownership can be temporarily locked by the foreground app.
        }
    }

    public void Dispose()
    {
        Restore();
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

internal sealed class DiagnosticState
{
    public DateTime StartedAtUtc { get; } = DateTime.UtcNow;
    public DateTime? LastHookEventUtc { get; set; }
    public DateTime? LastHookReinstallUtc { get; set; }
    public int HookReinstallCount { get; set; }
    public string CurrentWord { get; set; } = "";
    public string LastWord { get; set; } = "";
    public string LastKey { get; set; } = "";
    public string LastDecision { get; set; } = "ожидание ввода";
    public string LastFailure { get; set; } = "нет";
    public string LastLayoutSwitch { get; set; } = "нет";
    public string LastInputMethod { get; set; } = "нет";
}

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

internal enum DetectedKeyboardLayout
{
    English,
    Russian,
    Other,
    Unknown,
}

internal sealed record LayoutSwitchResult(
    KeyboardLayout Target,
    string Before,
    string After,
    bool Success,
    int Attempts)
{
    public override string ToString()
    {
        return $"{Before} -> {After}, цель {Target}, попыток {Attempts}, {(Success ? "успех" : "не подтверждено")}";
    }
}

internal sealed record ForegroundWindowInfo(
    string ProcessName,
    string ProcessPath,
    string Title,
    string LayoutName);

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
        "версия", "релиз", "коммит", "ветка", "репозиторий", "программа", "утилита",
        "пользователь", "пользователи", "профиль", "запуск", "автозагрузка", "обновление",
        "скачать", "установить", "установка", "импорт", "экспорт", "горячие", "клавиши",
        "клавиатура", "выделение", "буфер", "обмен", "копия", "ошибки", "исправление",
        "подсказка", "инструкция", "помощь", "раздел", "вкладка", "справка", "путь",
        "локально", "локальный", "параметр", "параметры", "проверить", "собрать",
        "выпустить", "опубликовать", "скачивание", "фоновая", "фон", "тихо", "быстро",
        "медленно", "работает", "запущено", "установлено", "доступно", "найдено",
        "ссылка", "окей", "понял", "давай", "нормально", "класс", "супер", "сейчас",
        "потом", "после", "перед", "первый", "последний", "новый", "старый", "общий",
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
        "github", "windows", "keyboard", "shortcut", "hotkey", "design", "program",
        "utility", "profile", "startup", "autostart", "install", "installer", "portable",
        "download", "upload", "import", "export", "json", "background", "silent",
        "notification", "tray", "menu", "dialog", "selection", "clipboard", "copy",
        "versioning", "publish", "artifact", "workflow", "action", "local", "global",
        "parameter", "option", "check", "compile", "create", "read", "write", "edit",
        "settings", "configuration", "config", "help", "hint", "guide", "first", "last",
        "new", "old", "current", "latest", "available", "found", "path", "link",
        "normal", "class", "super", "now", "later", "before", "after", "next",
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

    public static bool TryAutoCorrect(string word, AppSettings settings, out CorrectionResult correction)
    {
        return TryAutoCorrect(word, settings, out correction, out _);
    }

    public static bool TryAutoCorrect(string word, AppSettings settings, out CorrectionResult correction, out string reason)
    {
        correction = default!;
        reason = "";
        if (IsNeverCorrectWord(word, settings))
        {
            reason = "слово в списке 'никогда не исправлять'";
            return false;
        }

        if (TryCustomAutoReplacement(word, settings, out correction))
        {
            reason = "точечная пользовательская замена";
            return true;
        }

        if (word.Length < 2)
        {
            reason = "слово короче 2 символов";
            return false;
        }

        if (!TryConvertAny(word, out var converted))
        {
            reason = "слово не состоит целиком из русских или английских букв";
            return false;
        }

        var sourceScore = converted.Direction == LayoutDirection.LatinToCyrillic
            ? ScoreEnglish(word, settings)
            : ScoreRussian(word, settings);
        var targetScore = converted.Direction == LayoutDirection.LatinToCyrillic
            ? ScoreRussian(converted.Text, settings)
            : ScoreEnglish(converted.Text, settings);

        var sourceIsKnownWord = converted.Direction == LayoutDirection.LatinToCyrillic
            ? IsKnownEnglishWord(word, settings)
            : IsKnownRussianWord(word, settings);
        if (sourceIsKnownWord)
        {
            reason = "исходное слово уже похоже на известное слово";
            return false;
        }

        var targetIsKnownWord = converted.Direction == LayoutDirection.LatinToCyrillic
            ? IsKnownRussianWord(converted.Text, settings)
            : IsKnownEnglishWord(converted.Text, settings);

        if (word.Length < 4 && !targetIsKnownWord && !settings.AggressiveShortWords)
        {
            reason = "короткое слово, цель не найдена в словаре, смелый режим выключен";
            return false;
        }

        if (targetIsKnownWord && targetScore - sourceScore >= 8)
        {
            correction = converted;
            reason = $"цель есть в словаре, score {sourceScore}->{targetScore}";
            return true;
        }

        if (word.Length < 4 && settings.AggressiveShortWords && targetScore >= 10 && targetScore - sourceScore >= 8)
        {
            correction = converted;
            reason = $"смелый режим коротких слов, score {sourceScore}->{targetScore}";
            return true;
        }

        if (targetScore >= 22 && targetScore - sourceScore >= 14)
        {
            correction = converted;
            reason = $"эвристика уверена, score {sourceScore}->{targetScore}";
            return true;
        }

        reason = $"недостаточная уверенность, score {sourceScore}->{targetScore}, цель известна: {targetIsKnownWord}";
        return false;
    }

    public static bool CanLearnAutoReplacement(string original, string corrected)
    {
        if (!TryCreateAutoReplacementCorrection(original, corrected, out _)
            || !TryConvertAny(original, out var converted))
        {
            return false;
        }

        return string.Equals(converted.Text, corrected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryCustomAutoReplacement(string word, AppSettings settings, out CorrectionResult correction)
    {
        correction = default!;
        foreach (var rule in settings.CustomAutoReplacements)
        {
            if (string.Equals(rule.Original, word, StringComparison.OrdinalIgnoreCase)
                && TryCreateAutoReplacementCorrection(word, rule.Corrected, out correction))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryCreateAutoReplacementCorrection(string original, string corrected, out CorrectionResult correction)
    {
        correction = default!;
        original = original.Trim();
        corrected = corrected.Trim();
        if (!CanConvert(original)
            || corrected.Length == 0
            || corrected.Any(char.IsWhiteSpace)
            || string.Equals(original, corrected, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (original.All(IsLatin) && corrected.All(IsCyrillic))
        {
            correction = new CorrectionResult(
                corrected,
                LayoutDirection.LatinToCyrillic,
                KeyboardLayout.English,
                KeyboardLayout.Russian);
            return true;
        }

        if (original.All(IsCyrillic) && corrected.All(IsLatin))
        {
            correction = new CorrectionResult(
                corrected,
                LayoutDirection.CyrillicToLatin,
                KeyboardLayout.Russian,
                KeyboardLayout.English);
            return true;
        }

        return false;
    }

    public static bool TryConvertText(string text, out CorrectionResult correction)
    {
        correction = default!;
        var letters = text.Where(IsWordChar).ToArray();
        if (letters.Length == 0)
        {
            return false;
        }

        if (letters.All(IsLatin))
        {
            correction = new CorrectionResult(
                ConvertWithMap(text, LatinToCyrillic),
                LayoutDirection.LatinToCyrillic,
                KeyboardLayout.English,
                KeyboardLayout.Russian);
            return true;
        }

        if (letters.All(IsCyrillic))
        {
            correction = new CorrectionResult(
                ConvertWithMap(text, CyrillicToLatin),
                LayoutDirection.CyrillicToLatin,
                KeyboardLayout.Russian,
                KeyboardLayout.English);
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

    private static int ScoreRussian(string word, AppSettings settings)
    {
        var lower = word.ToLowerInvariant();
        var score = 0;
        if (IsKnownRussianWord(lower, settings))
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

    private static int ScoreEnglish(string word, AppSettings settings)
    {
        var lower = word.ToLowerInvariant();
        var score = 0;
        if (IsKnownEnglishWord(lower, settings))
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

    private static bool IsKnownRussianWord(string word, AppSettings settings)
    {
        return RussianWords.Contains(word) || settings.CustomRussianWords.Contains(word, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsKnownEnglishWord(string word, AppSettings settings)
    {
        return EnglishWords.Contains(word) || settings.CustomEnglishWords.Contains(word, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsNeverCorrectWord(string word, AppSettings settings)
    {
        return settings.CustomNeverCorrectWords.Contains(word, StringComparer.OrdinalIgnoreCase);
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

    public static bool Matches(HotkeyBinding binding, Keys key)
    {
        return binding.IsUsable
            && key == binding.Key
            && IsKeyDown(Keys.ControlKey) == binding.Ctrl
            && IsKeyDown(Keys.Menu) == binding.Alt
            && IsKeyDown(Keys.ShiftKey) == binding.Shift;
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

    public static bool ReplaceTextViaClipboard(int backspaceCount, string text)
    {
        using var clipboard = ClipboardBackup.Capture();
        try
        {
            if (backspaceCount > 0 && !SendBackspaces(backspaceCount))
            {
                return false;
            }

            Thread.Sleep(35);
            if (text.Length > 0)
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                if (!SendChord(Keys.ControlKey, Keys.V))
                {
                    return false;
                }

                Thread.Sleep(90);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool SendBackspaces(int count)
    {
        if (count <= 0)
        {
            return true;
        }

        var inputs = new List<NativeMethods.Input>(count * 2);
        for (var i = 0; i < count; i++)
        {
            inputs.Add(KeyboardInput((ushort)Keys.Back, 0, 0));
            inputs.Add(KeyboardInput((ushort)Keys.Back, 0, KeyeventfKeyup));
        }

        return NativeMethods.SendKeyboardInput(inputs.ToArray());
    }

    public static bool SendChord(params Keys[] keys)
    {
        if (keys.Length == 0)
        {
            return true;
        }

        var inputs = new List<NativeMethods.Input>(keys.Length * 2);
        foreach (var key in keys)
        {
            inputs.Add(KeyboardInput((ushort)key, 0, 0));
        }

        for (var i = keys.Length - 1; i >= 0; i--)
        {
            inputs.Add(KeyboardInput((ushort)keys[i], 0, KeyeventfKeyup));
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
    private const int RussianLocaleId = 0x0419;
    private const int EnglishLocaleId = 0x0409;
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

    public static LayoutSwitchResult SwitchForegroundLayout(KeyboardLayout layout)
    {
        var before = DescribeKeyboardLayout(GetForegroundKeyboardLayout());
        var layoutId = layout == KeyboardLayout.Russian ? RussianLayoutId : EnglishLayoutId;
        var hkl = LoadKeyboardLayout(layoutId, KlfActivate);
        if (hkl == IntPtr.Zero)
        {
            return new LayoutSwitchResult(layout, before, before, false, 0);
        }

        var attempts = 0;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            attempts = attempt;
            var foreground = GetForegroundWindow();
            if (foreground != IntPtr.Zero)
            {
                PostMessage(foreground, WmInputLangChangeRequest, IntPtr.Zero, hkl);
            }

            ActivateKeyboardLayout(hkl, KlfActivate);
            Thread.Sleep(35 * attempt);

            var current = GetForegroundKeyboardLayout();
            if (MatchesKeyboardLayout(current, layout))
            {
                return new LayoutSwitchResult(layout, before, DescribeKeyboardLayout(current), true, attempts);
            }
        }

        return new LayoutSwitchResult(layout, before, DescribeKeyboardLayout(GetForegroundKeyboardLayout()), false, attempts);
    }

    public static string GetForegroundProcessName()
    {
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return string.Empty;
            }

            GetWindowThreadProcessId(foreground, out var processId);
            if (processId == 0)
            {
                return string.Empty;
            }

            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static ForegroundWindowInfo GetForegroundWindowInfo()
    {
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return new ForegroundWindowInfo("", "", "", "Unknown");
            }

            GetWindowThreadProcessId(foreground, out var processId);
            var title = new StringBuilder(512);
            GetWindowText(foreground, title, title.Capacity);
            var processName = "";
            var processPath = "";
            if (processId != 0)
            {
                try
                {
                    using var process = Process.GetProcessById((int)processId);
                    processName = process.ProcessName;
                    processPath = process.MainModule?.FileName ?? "";
                }
                catch
                {
                    processName = processId.ToString();
                }
            }

            return new ForegroundWindowInfo(
                processName,
                processPath,
                title.ToString(),
                DescribeKeyboardLayout(GetForegroundKeyboardLayout()));
        }
        catch
        {
            return new ForegroundWindowInfo("", "", "", "Unknown");
        }
    }

    private static IntPtr GetForegroundKeyboardLayout()
    {
        var foreground = GetForegroundWindow();
        var threadId = GetWindowThreadProcessId(foreground, out _);
        return GetKeyboardLayout(threadId);
    }

    private static bool MatchesKeyboardLayout(IntPtr hkl, KeyboardLayout layout)
    {
        var localeId = GetLocaleId(hkl);
        return layout switch
        {
            KeyboardLayout.Russian => localeId == RussianLocaleId,
            KeyboardLayout.English => localeId == EnglishLocaleId,
            _ => false,
        };
    }

    private static string DescribeKeyboardLayout(IntPtr hkl)
    {
        if (hkl == IntPtr.Zero)
        {
            return "Unknown";
        }

        var localeId = GetLocaleId(hkl);
        return localeId switch
        {
            EnglishLocaleId => $"EN ({localeId:X4})",
            RussianLocaleId => $"RU ({localeId:X4})",
            _ => $"Other ({localeId:X4})",
        };
    }

    private static int GetLocaleId(IntPtr hkl)
    {
        return unchecked((int)((long)hkl & 0xFFFF));
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

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
