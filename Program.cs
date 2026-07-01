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
        CheckDefaultReplacements(failures);
        CheckSettingsRoundTrip(failures);
        CheckInputSize(failures);
        CheckDownloadProgress(failures);

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

    private static void CheckDefaultReplacements(List<string> failures)
    {
        var rules = AutoReplacementDefaults.Create();
        if (rules.Count < 120)
        {
            failures.Add($"DEFAULT replacements: expected at least 120 rules, actual {rules.Count}");
        }

        if (!rules.Any(rule => rule.Original == "ghbdtn" && rule.Corrected == "привет"))
        {
            failures.Add("DEFAULT replacements: missing ghbdtn -> привет");
        }

        if (!rules.Any(rule => rule.Original == "руддщ" && rule.Corrected == "hello"))
        {
            failures.Add("DEFAULT replacements: missing руддщ -> hello");
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
                IgnorePasswordFields = false,
                LearnFromManualCorrections = true,
                CorrectionProfile = CorrectionProfile.Bold,
                DarkTheme = true,
                BuiltInReplacementsVersion = AutoReplacementDefaults.Version,
                CustomAutoReplacements = [new AutoReplacementRule { Original = "ghbdtn", Corrected = "привет", Enabled = false }],
                DecisionLog = [new DecisionLogItem { Original = "ghbdtn", Corrected = "привет", Reason = "test" }],
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

            if (loaded.IgnorePasswordFields || loaded.CorrectionProfile != CorrectionProfile.Bold || !loaded.DarkTheme)
            {
                failures.Add("SETTINGS roundtrip: profile/theme/password settings were not preserved");
            }

            if (!loaded.LearnFromManualCorrections
                || loaded.CustomAutoReplacements.Count != 1
                || loaded.CustomAutoReplacements[0].Corrected != "привет"
                || loaded.CustomAutoReplacements[0].Enabled)
            {
                failures.Add("SETTINGS roundtrip: auto replacement rules were not preserved");
            }

            if (loaded.DecisionLog.Count != 1 || loaded.DecisionLog[0].Corrected != "привет")
            {
                failures.Add("SETTINGS roundtrip: decision log was not preserved");
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

    private static void CheckDownloadProgress(List<string> failures)
    {
        var half = new DownloadProgress(50, 100);
        if (half.Percent != 50 || !half.StatusText.Contains("50%", StringComparison.Ordinal))
        {
            failures.Add($"PROGRESS half: expected 50%, actual {half.Percent} / {half.StatusText}");
        }

        var unknown = new DownloadProgress(2048, null);
        if (unknown.Percent is not null || !unknown.StatusText.Contains("KB", StringComparison.Ordinal))
        {
            failures.Add($"PROGRESS unknown: expected KB text, actual {unknown.Percent} / {unknown.StatusText}");
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
    private DateTime? _pauseUntilUtc;
    private string? _lastExternalProcessName;
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
            if (IsPaused(out var pauseReason))
            {
                return pauseReason;
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

    public IReadOnlyList<DecisionLogItem> DecisionLog => _settings.DecisionLog;

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
        var pauseTwoMinutesItem = new ToolStripMenuItem("Пауза на 2 минуты", null, (_, _) => PauseTemporarily(TimeSpan.FromMinutes(2)))
        {
            Name = "PauseTwoMinutes",
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
            pauseTwoMinutesItem,
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
        ClearExpiredTemporaryPause();

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

        if (menu.Items["PauseTwoMinutes"] is ToolStripMenuItem pauseTwoMinutesItem)
        {
            pauseTwoMinutesItem.Enabled = !_settings.Paused;
            pauseTwoMinutesItem.Text = _pauseUntilUtc is { } until && until > DateTime.UtcNow
                ? $"Временная пауза до {until.ToLocalTime():HH:mm:ss}"
                : "Пауза на 2 минуты";
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

    private SettingsForm ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            if (_settingsForm.WindowState == FormWindowState.Minimized)
            {
                _settingsForm.WindowState = FormWindowState.Normal;
            }

            _settingsForm.Show();
            _settingsForm.Activate();
            return _settingsForm;
        }

        _settingsForm = new SettingsForm(this);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        return _settingsForm;
    }

    private bool HandleKeyDown(Keys key, int scanCode)
    {
        _diagnostics.LastHookEventUtc = DateTime.UtcNow;
        _diagnostics.LastKey = $"{key} / scan {scanCode}";
        _diagnostics.CurrentWord = _currentWord.ToString();
        TrackForegroundProcess();

        if (KeyboardState.Matches(_settings.PauseHotkey, key))
        {
            SetDiagnosticDecision($"Горячая клавиша паузы: {key}");
            PostToUi(TogglePause);
            return true;
        }

        if (IsPaused(out var pauseReason))
        {
            SetDiagnosticDecision($"Пропуск: {pauseReason}");
            ResetTypingState();
            return false;
        }

        if (_settings.IgnorePasswordFields && NativeMethods.IsForegroundPasswordField())
        {
            SetDiagnosticDecision("Пропуск: похоже на поле пароля");
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
            PostToUi(() => AddDecisionLog("Авто", word, "", "авто выключено", applied: false));
        }
        else if (TextHeuristics.TryAutoCorrect(word, _settings, out var correction, out var reason))
        {
            SetDiagnosticDecision($"Автозамена: {word} -> {correction.Text}. {reason}");
            PostToUi(() => ApplyCorrection(word, delimiter, correction, reason));
            return true;
        }
        else
        {
            SetDiagnosticDecision($"Автозамены нет для '{word}': {reason}");
            var candidate = TextHeuristics.TryConvertAny(word, out var candidateCorrection)
                ? candidateCorrection.Text
                : "";
            PostToUi(() => AddDecisionLog("Авто", word, candidate, reason, applied: false));
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

    private void ApplyCorrection(string originalWord, string delimiter, CorrectionResult correction, string reason)
    {
        var originalText = originalWord + delimiter;
        var correctedText = correction.Text + delimiter;

        if (!SwitchLayoutAndReplace(correction.TargetLayout, originalWord.Length, correctedText, out var inputMethod))
        {
            AddDecisionLog("Авто", originalWord, correction.Text, "ошибка ввода: " + reason, applied: false);
            PlayErrorSound();
            UpdateBalloon("Ошибка ввода", "Windows заблокировала замену текста");
            return;
        }

        _lastCorrection = new LastCorrection(originalText, correctedText, correction.SourceLayout, correction.TargetLayout);
        _lastTypedSegment = null;

        AddHistory("Авто", originalText, correctedText);
        AddDecisionLog("Авто", originalWord, correction.Text, $"{reason}; ввод: {inputMethod}", applied: true);
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
            AddDecisionLog("Ручная", word, correction.Text, "ручная конвертация текущего слова", applied: true);
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
        AddDecisionLog("Ручная", segment.Word, segmentCorrection.Text, "ручная конвертация последнего слова", applied: true);
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
        AddDecisionLog("Выделение", TrimForStatus(selectedText), TrimForStatus(correction.Text), "ручная конвертация выделенного текста", applied: true);
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

    public void ClearDecisionLog()
    {
        UpdateSettings(s => s.DecisionLog.Clear());
    }

    public bool AddNeverCorrectWord(string word)
    {
        word = NormalizeRuleWord(word);
        if (!AutoReplacementRule.IsValidText(word))
        {
            return false;
        }

        UpdateSettings(s =>
        {
            if (!s.CustomNeverCorrectWords.Contains(word, StringComparer.OrdinalIgnoreCase))
            {
                s.CustomNeverCorrectWords.Insert(0, word);
            }
        });
        return true;
    }

    public bool AddAutoReplacement(string original, string corrected, bool learned)
    {
        original = NormalizeRuleWord(original);
        corrected = NormalizeRuleWord(corrected);
        if (!TextHeuristics.CanLearnAutoReplacement(original, corrected))
        {
            return false;
        }

        UpdateSettings(s => AutoReplacementRule.AddOrUpdate(s.CustomAutoReplacements, original, corrected, learned));
        return true;
    }

    public string? AddLastActiveProcessToExclusions()
    {
        var processName = _lastExternalProcessName;
        if (string.IsNullOrWhiteSpace(processName))
        {
            var foreground = NativeMethods.GetForegroundWindowInfo();
            if (!IsOwnProcess(foreground.ProcessName))
            {
                processName = foreground.ProcessName;
            }
        }

        if (string.IsNullOrWhiteSpace(processName) || IsOwnProcess(processName))
        {
            return null;
        }

        AddProcessesToExclusions([processName]);
        return processName;
    }

    public IReadOnlyList<ProcessOption> GetProcessOptions()
    {
        return Process.GetProcesses()
            .Select(ProcessOption.TryCreate)
            .Where(option => option is not null)
            .Cast<ProcessOption>()
            .Where(option => !IsOwnProcess(option.ProcessName))
            .GroupBy(option => option.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(option => option.HasWindow)
                .ThenBy(option => option.DisplayText, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderByDescending(option => option.HasWindow)
            .ThenBy(option => option.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void AddProcessesToExclusions(IEnumerable<string> processNames)
    {
        UpdateSettings(s =>
        {
            foreach (var processName in processNames.Select(NormalizeProcessName))
            {
                if (processName.Length > 0
                    && !IsOwnProcess(processName)
                    && !s.ExcludedProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase))
                {
                    s.ExcludedProcesses.Insert(0, processName);
                }
            }
        });
    }

    public void PauseTemporarily(TimeSpan duration)
    {
        _settings.Paused = false;
        _pauseUntilUtc = DateTime.UtcNow.Add(duration);
        SettingsStore.Save(_settings);
        ResetTypingState();
        RefreshMenu();
        _settingsForm?.RefreshFromSettings();
        UpdateBalloon("Пауза", $"до {_pauseUntilUtc.Value.ToLocalTime():HH:mm:ss}");
    }

    public void TestSound(LayoutDirection direction)
    {
        PlaySwitchSound(direction);
    }

    public void TogglePause()
    {
        _pauseUntilUtc = null;
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
        var updateWindowShown = false;
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

            var settingsForm = ShowSettings();
            settingsForm.ShowInstallTab();
            updateWindowShown = true;
            _settingsForm?.BeginUpdateInstall($"Скачиваю {update.TagName}...");

            var progress = new Progress<DownloadProgress>(value =>
            {
                _settingsForm?.ReportUpdateProgress(value);
            });

            var downloadedExe = await UpdateManager.DownloadUpdateAsync(update, progress);
            _settingsForm?.CompleteUpdateInstall("Скачано. Готовлю замену и перезапуск...", keepBusy: true);
            InstallManager.ScheduleReplacement(downloadedExe, Application.ExecutablePath, Environment.ProcessId);
            ExitThread();
        }
        catch (Exception ex)
        {
            if (updateWindowShown)
            {
                _settingsForm?.CompleteUpdateInstall("Не удалось установить обновление.", keepBusy: false);
            }

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

    private bool IsPaused(out string reason)
    {
        ClearExpiredTemporaryPause();
        if (_settings.Paused)
        {
            reason = "пауза включена";
            return true;
        }

        if (_pauseUntilUtc is { } until && until > DateTime.UtcNow)
        {
            reason = $"пауза до {until.ToLocalTime():HH:mm:ss}";
            return true;
        }

        reason = "";
        return false;
    }

    private void ClearExpiredTemporaryPause()
    {
        if (_pauseUntilUtc is { } until && until <= DateTime.UtcNow)
        {
            _pauseUntilUtc = null;
        }
    }

    private void TrackForegroundProcess()
    {
        var processName = NativeMethods.GetForegroundProcessName();
        if (processName.Length > 0 && !IsOwnProcess(processName))
        {
            _lastExternalProcessName = processName;
        }
    }

    private static bool IsOwnProcess(string processName)
    {
        return string.Equals(processName, Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(processName, "Switcher", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeProcessName(string value)
    {
        value = value.Trim();
        return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(value)
            : value;
    }

    private static string NormalizeRuleWord(string value)
    {
        return value.Trim()
            .Trim('.', ',', ';', ':', '!', '?', ')', ']', '}', '"', '\'')
            .Trim();
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

    private void AddDecisionLog(string kind, string original, string corrected, string reason, bool applied)
    {
        _settings.DecisionLog.Insert(0, new DecisionLogItem
        {
            Timestamp = DateTime.Now,
            Kind = kind,
            Original = original,
            Corrected = corrected,
            Reason = reason,
            Applied = applied,
        });

        if (_settings.DecisionLog.Count > 80)
        {
            _settings.DecisionLog.RemoveRange(80, _settings.DecisionLog.Count - 80);
        }

        SettingsStore.Save(_settings);
        _settingsForm?.RefreshDecisionLog();
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
        builder.AppendLine($"Профиль исправлений: {DisplayProfile(_settings.CorrectionProfile)}");
        builder.AppendLine($"Защита password-полей: {(_settings.IgnorePasswordFields ? "включена" : "выключена")}");
        builder.AppendLine($"Смелый режим коротких слов: {(_settings.AggressiveShortWords ? "включен" : "выключен")}");
        builder.AppendLine($"Clipboard fallback: {(_settings.ClipboardFallback ? "включен" : "выключен")}");
        builder.AppendLine($"Автовосстановление hook: {(_settings.AutoHealKeyboardHook ? "включено" : "выключено")}");
        builder.AppendLine($"Тёмная тема: {(_settings.DarkTheme ? "включена" : "выключена")}");
        return builder.ToString();
    }

    private static string DisplayProfile(CorrectionProfile profile)
    {
        return profile switch
        {
            CorrectionProfile.Careful => "Осторожный",
            CorrectionProfile.Bold => "Смелый",
            _ => "Обычный",
        };
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
    private readonly TabControl _tabs = new();
    private TabPage? _installTab;
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
    private readonly CheckBox _ignorePasswordFields = new();
    private readonly CheckBox _darkTheme = new();
    private readonly ComboBox _correctionProfile = new();
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
    private readonly DataGridView _autoReplacementsGrid = new();
    private readonly TextBox _diagnostics = new();
    private readonly ListBox _history = new();
    private readonly ListBox _decisionLog = new();
    private readonly Label _installStatus = new();
    private readonly Label _updateStatus = new();
    private readonly ProgressBar _updateProgress = new();
    private readonly Button _pauseTwoMinutesButton = new();
    private readonly Button _addActiveProcessButton = new();
    private readonly Button _pickProcessesButton = new();
    private readonly Button _installButton = new();
    private readonly Button _uninstallButton = new();
    private readonly Button _checkUpdateButton = new();
    private readonly Button _installUpdateButton = new();
    private readonly Button _saveHotkeysButton = new();
    private readonly Button _resetHotkeysButton = new();
    private readonly Button _saveReplacementsButton = new();
    private readonly Button _deleteReplacementButton = new();
    private readonly Button _clearLearnedReplacementsButton = new();
    private readonly Button _alwaysCorrectDecisionButton = new();
    private readonly Button _neverCorrectDecisionButton = new();
    private readonly Button _clearDecisionLogButton = new();
    private readonly Button _refreshDiagnosticsButton = new();
    private readonly Button _reinstallHookButton = new();
    private readonly Button _exportSettingsButton = new();
    private readonly Button _importSettingsButton = new();
    private readonly Button _showFirstRunHintButton = new();
    private UpdateInfo? _latestUpdate;
    private bool _updating;
    private bool _installingUpdate;

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

        _tabs.Dock = DockStyle.Fill;
        _tabs.Multiline = true;
        _tabs.TabPages.Add(CreateGeneralTab());
        _tabs.TabPages.Add(CreateListsTab());
        _tabs.TabPages.Add(CreateReplacementsTab());
        _tabs.TabPages.Add(CreateSettingsTab());
        _tabs.TabPages.Add(CreateDecisionLogTab());
        _tabs.TabPages.Add(CreateHistoryTab());
        _tabs.TabPages.Add(CreateDiagnosticsTab());
        _tabs.TabPages.Add(CreateInstallTab());
        root.Controls.Add(_tabs, 0, 2);

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
        _ignorePasswordFields.Checked = _context.Settings.IgnorePasswordFields;
        _darkTheme.Checked = _context.Settings.DarkTheme;
        SetProfileComboValue(_context.Settings.CorrectionProfile);
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
        RefreshReplacementRules(force: true);
        _updating = false;

        UpdateStatus();
        UpdateHotkeysLabel();
        RefreshHistory();
        RefreshDecisionLog();
        RefreshDiagnostics();
        RefreshInstallState();
        ApplyTheme();
    }

    public void ShowInstallTab()
    {
        if (InvokeRequired)
        {
            BeginInvoke(ShowInstallTab);
            return;
        }

        if (_installTab is not null)
        {
            _tabs.SelectedTab = _installTab;
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Show();
        Activate();
    }

    public void RefreshReplacementRules()
    {
        RefreshReplacementRules(force: false);
    }

    private void RefreshReplacementRules(bool force)
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

        if (force || !_autoReplacementsGrid.ContainsFocus)
        {
            PopulateReplacementGrid(_context.Settings.CustomAutoReplacements);
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

    public void RefreshDecisionLog()
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(RefreshDecisionLog);
            return;
        }

        _decisionLog.BeginUpdate();
        _decisionLog.Items.Clear();
        foreach (var item in _context.DecisionLog)
        {
            _decisionLog.Items.Add(item);
        }

        _decisionLog.EndUpdate();
        UpdateDecisionButtons();
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
            RowCount = 7,
            ColumnCount = 1,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        var profileGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };
        profileGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        profileGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        profileGrid.Controls.Add(new Label
        {
            Text = "Профиль",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        ConfigureProfileCombo(_correctionProfile);
        profileGrid.Controls.Add(_correctionProfile, 1, 0);
        layout.Controls.Add(profileGrid, 0, 0);

        ConfigureCheckBox(_autoSwitch, "Автоисправление после пробела/знака");
        ConfigureCheckBox(_sound, "Звук при смене раскладки");
        ConfigureCheckBox(_paused, "Пауза");
        ConfigureCheckBox(_startup, "Запуск вместе с Windows");
        ConfigureCheckBox(_ignorePasswordFields, "Пропускать password-поля");
        ConfigureCheckBox(_aggressiveShortWords, "Смелее короткие слова");
        ConfigureCheckBox(_clipboardFallback, "Запасная вставка через буфер");
        ConfigureCheckBox(_autoHealKeyboardHook, "Автовосстановление hook");
        ConfigureCheckBox(_darkTheme, "Тёмная тема");

        var optionGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
        };
        optionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        optionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        optionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        for (var row = 0; row < 3; row++)
        {
            optionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        }

        optionGrid.Controls.Add(_autoSwitch, 0, 0);
        optionGrid.Controls.Add(_sound, 1, 0);
        optionGrid.Controls.Add(_paused, 2, 0);
        optionGrid.Controls.Add(_startup, 0, 1);
        optionGrid.Controls.Add(_ignorePasswordFields, 1, 1);
        optionGrid.Controls.Add(_aggressiveShortWords, 2, 1);
        optionGrid.Controls.Add(_clipboardFallback, 0, 2);
        optionGrid.Controls.Add(_autoHealKeyboardHook, 1, 2);
        optionGrid.Controls.Add(_darkTheme, 2, 2);
        layout.Controls.Add(optionGrid, 0, 1);

        var pauseButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
        };
        ConfigureButton(_pauseTwoMinutesButton, "Пауза на 2 минуты", 190);
        _pauseTwoMinutesButton.Text = "Пауза 2 мин";
        _pauseTwoMinutesButton.Width = 150;
        pauseButtons.Controls.Add(_pauseTwoMinutesButton);
        layout.Controls.Add(pauseButtons, 0, 2);

        _hotkeysLabel.Dock = DockStyle.Fill;
        _hotkeysLabel.ForeColor = Color.FromArgb(51, 65, 85);
        layout.Controls.Add(_hotkeysLabel, 0, 3);

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
        layout.Controls.Add(soundGrid, 0, 4);

        _lastActionLabel.Dock = DockStyle.Fill;
        _lastActionLabel.ForeColor = Color.FromArgb(71, 85, 105);
        layout.Controls.Add(_lastActionLabel, 0, 5);

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
        AddHeader(layout, 3, "Не исправлять");
        ConfigureTextArea(_excludedProcesses);
        ConfigureTextArea(_russianWords);
        ConfigureTextArea(_englishWords);
        ConfigureTextArea(_neverCorrectWords);
        layout.Controls.Add(_excludedProcesses, 0, 1);
        layout.Controls.Add(_russianWords, 1, 1);
        layout.Controls.Add(_englishWords, 2, 1);
        layout.Controls.Add(_neverCorrectWords, 3, 1);

        var excludeButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
        };
        ConfigureButton(_addActiveProcessButton, "Добавить активное", 180);
        ConfigureButton(_pickProcessesButton, "Выбрать процессы", 180);
        excludeButtons.Controls.Add(_addActiveProcessButton);
        excludeButtons.Controls.Add(_pickProcessesButton);
        layout.Controls.Add(excludeButtons, 0, 2);
        layout.SetColumnSpan(excludeButtons, 3);

        var save = new Button
        {
            Text = "Сохранить списки",
            Dock = DockStyle.Right,
            Width = 150,
            Height = 32,
        };
        save.Click += (_, _) => SaveLists();
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
            Text = "Точечные автозамены применяются до эвристики и словаря. Выключенные правила остаются в списке, но не применяются.",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(71, 85, 105),
        }, 0, 1);

        ConfigureReplacementGrid(_autoReplacementsGrid);
        layout.Controls.Add(_autoReplacementsGrid, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
        };
        ConfigureButton(_saveReplacementsButton, "Сохранить замены", 200);
        ConfigureButton(_deleteReplacementButton, "Удалить выбранные", 190);
        ConfigureButton(_clearLearnedReplacementsButton, "Очистить выученные", 220);
        buttons.Controls.Add(_saveReplacementsButton);
        buttons.Controls.Add(_deleteReplacementButton);
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

    private TabPage CreateDecisionLogTab()
    {
        var page = new TabPage("Журнал");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            RowCount = 3,
            ColumnCount = 1,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        page.Controls.Add(layout);

        layout.Controls.Add(new Label
        {
            Text = "Журнал показывает исправленные и пропущенные слова с причиной решения. По выбранной строке можно добавить точную замену или запретить исправление.",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(71, 85, 105),
        }, 0, 0);

        _decisionLog.Dock = DockStyle.Fill;
        _decisionLog.HorizontalScrollbar = true;
        layout.Controls.Add(_decisionLog, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
        };
        ConfigureButton(_alwaysCorrectDecisionButton, "Всегда исправлять так", 220);
        ConfigureButton(_neverCorrectDecisionButton, "Никогда не исправлять", 220);
        ConfigureButton(_clearDecisionLogButton, "Очистить журнал", 170);
        buttons.Controls.Add(_alwaysCorrectDecisionButton);
        buttons.Controls.Add(_neverCorrectDecisionButton);
        buttons.Controls.Add(_clearDecisionLogButton);
        layout.Controls.Add(buttons, 0, 2);
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
        _installTab = page;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            RowCount = 10,
            ColumnCount = 1,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
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

        _updateProgress.Dock = DockStyle.Fill;
        _updateProgress.Minimum = 0;
        _updateProgress.Maximum = 100;
        _updateProgress.Style = ProgressBarStyle.Continuous;
        _updateProgress.Visible = false;
        _updateProgress.Margin = new Padding(0, 6, 0, 6);
        layout.Controls.Add(_updateProgress, 0, 6);

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
        layout.Controls.Add(updateButtons, 0, 7);

        layout.Controls.Add(new Label
        {
            Text = "Установка выполняется без прав администратора в профиль пользователя. Обновление скачивает последний Switcher.exe из GitHub Releases и заменяет текущий файл после закрытия приложения.",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(71, 85, 105),
        }, 0, 8);

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
        _correctionProfile.SelectedIndexChanged += (_, _) =>
        {
            if (!_updating && _correctionProfile.SelectedItem is ProfileOption option)
            {
                _context.UpdateSettings(s => s.CorrectionProfile = option.Profile);
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
        _ignorePasswordFields.CheckedChanged += (_, _) =>
        {
            if (!_updating)
            {
                _context.UpdateSettings(s => s.IgnorePasswordFields = _ignorePasswordFields.Checked);
            }
        };
        _darkTheme.CheckedChanged += (_, _) =>
        {
            if (!_updating)
            {
                _context.UpdateSettings(s => s.DarkTheme = _darkTheme.Checked);
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
        _pauseTwoMinutesButton.Click += (_, _) => _context.PauseTemporarily(TimeSpan.FromMinutes(2));
        _addActiveProcessButton.Click += (_, _) => AddActiveProcessToExclusions();
        _pickProcessesButton.Click += (_, _) => PickProcessesForExclusions();
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
        _deleteReplacementButton.Click += (_, _) => DeleteSelectedReplacements();
        _clearLearnedReplacementsButton.Click += (_, _) => ClearLearnedReplacements();
        _decisionLog.SelectedIndexChanged += (_, _) => UpdateDecisionButtons();
        _alwaysCorrectDecisionButton.Click += (_, _) => AddSelectedDecisionReplacement();
        _neverCorrectDecisionButton.Click += (_, _) => AddSelectedDecisionNeverCorrect();
        _clearDecisionLogButton.Click += (_, _) => _context.ClearDecisionLog();
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
        var replacements = ReadReplacementRulesFromGrid(out var errors);
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

    private void DeleteSelectedReplacements()
    {
        foreach (DataGridViewRow row in _autoReplacementsGrid.SelectedRows)
        {
            if (!row.IsNewRow)
            {
                _autoReplacementsGrid.Rows.Remove(row);
            }
        }
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

    private void SaveLists()
    {
        _context.UpdateSettings(s =>
        {
            s.ExcludedProcesses = ParseLines(_excludedProcesses.Text);
            s.CustomRussianWords = ParseLines(_russianWords.Text);
            s.CustomEnglishWords = ParseLines(_englishWords.Text);
            s.CustomNeverCorrectWords = ParseLines(_neverCorrectWords.Text);
        });
    }

    private void AddActiveProcessToExclusions()
    {
        var processName = _context.AddLastActiveProcessToExclusions();
        if (processName is null)
        {
            MessageBox.Show(this, "Активное внешнее приложение не найдено. Используйте выбор из списка процессов.", "Switcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _excludedProcesses.Text = LinesFrom(_context.Settings.ExcludedProcesses);
        MessageBox.Show(this, $"Добавлено в исключения: {processName}", "Switcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void PickProcessesForExclusions()
    {
        using var dialog = new ProcessPickerDialog(_context.GetProcessOptions(), _context.Settings.ExcludedProcesses, _context.Settings.DarkTheme);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _context.AddProcessesToExclusions(dialog.SelectedProcessNames);
        _excludedProcesses.Text = LinesFrom(_context.Settings.ExcludedProcesses);
    }

    private void UpdateDecisionButtons()
    {
        var selected = _decisionLog.SelectedItem as DecisionLogItem;
        _alwaysCorrectDecisionButton.Enabled = selected is not null
            && AutoReplacementRule.IsValidText(selected.Original)
            && AutoReplacementRule.IsValidText(selected.Corrected);
        _neverCorrectDecisionButton.Enabled = selected is not null
            && AutoReplacementRule.IsValidText(selected.Original);
    }

    private void AddSelectedDecisionReplacement()
    {
        if (_decisionLog.SelectedItem is not DecisionLogItem selected)
        {
            return;
        }

        if (!_context.AddAutoReplacement(selected.Original, selected.Corrected, learned: false))
        {
            MessageBox.Show(this, "Для этой строки нельзя создать точную замену.", "Switcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        MessageBox.Show(this, "Точная замена добавлена.", "Switcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void AddSelectedDecisionNeverCorrect()
    {
        if (_decisionLog.SelectedItem is not DecisionLogItem selected)
        {
            return;
        }

        if (!_context.AddNeverCorrectWord(selected.Original))
        {
            MessageBox.Show(this, "Для этой строки нельзя добавить запрет исправления.", "Switcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        MessageBox.Show(this, "Слово добавлено в список 'Никогда не исправлять'.", "Switcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        if (_installingUpdate)
        {
            return;
        }

        _updateProgress.Visible = false;
        _updateProgress.Value = 0;
        _updateProgress.Style = ProgressBarStyle.Continuous;
        _updateProgress.MarqueeAnimationSpeed = 0;
        _updateStatus.Text = BuildUpdateStatusText();
        _checkUpdateButton.Enabled = true;
        _installUpdateButton.Enabled = _latestUpdate?.IsNewer == true;
    }

    public void BeginUpdateInstall(string status)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => BeginUpdateInstall(status)));
            return;
        }

        ShowInstallTab();
        _installingUpdate = true;
        _updateStatus.Text = status;
        _updateProgress.Visible = true;
        _updateProgress.Style = ProgressBarStyle.Marquee;
        _updateProgress.MarqueeAnimationSpeed = 30;
        _updateProgress.Value = 0;
        _checkUpdateButton.Enabled = false;
        _installUpdateButton.Enabled = false;
    }

    public void ReportUpdateProgress(DownloadProgress progress)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => ReportUpdateProgress(progress)));
            return;
        }

        _installingUpdate = true;
        _updateProgress.Visible = true;
        if (progress.Percent is { } percent)
        {
            _updateProgress.Style = ProgressBarStyle.Continuous;
            _updateProgress.MarqueeAnimationSpeed = 0;
            _updateProgress.Value = Math.Clamp(percent, _updateProgress.Minimum, _updateProgress.Maximum);
        }
        else
        {
            _updateProgress.Style = ProgressBarStyle.Marquee;
            _updateProgress.MarqueeAnimationSpeed = 30;
        }

        _updateStatus.Text = $"Скачиваю обновление: {progress.StatusText}";
        _checkUpdateButton.Enabled = false;
        _installUpdateButton.Enabled = false;
    }

    public void CompleteUpdateInstall(string status, bool keepBusy)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => CompleteUpdateInstall(status, keepBusy)));
            return;
        }

        _updateStatus.Text = status;
        if (keepBusy)
        {
            _updateProgress.Visible = true;
            _updateProgress.Style = ProgressBarStyle.Continuous;
            _updateProgress.MarqueeAnimationSpeed = 0;
            _updateProgress.Value = _updateProgress.Maximum;
            _checkUpdateButton.Enabled = false;
            _installUpdateButton.Enabled = false;
            return;
        }

        _installingUpdate = false;
        _updateProgress.Visible = false;
        _updateProgress.Style = ProgressBarStyle.Continuous;
        _updateProgress.MarqueeAnimationSpeed = 0;
        _updateProgress.Value = 0;
        _checkUpdateButton.Enabled = true;
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
        if (_installingUpdate)
        {
            return;
        }

        _checkUpdateButton.Enabled = false;
        _installUpdateButton.Enabled = false;
        _updateProgress.Visible = true;
        _updateProgress.Style = ProgressBarStyle.Marquee;
        _updateProgress.MarqueeAnimationSpeed = 30;
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
        checkBox.AutoEllipsis = true;
        checkBox.TextAlign = ContentAlignment.MiddleLeft;
    }

    private static void ConfigureCombo(ComboBox combo)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Dock = DockStyle.Fill;
        combo.Items.AddRange(SoundPlayerNames.All.Cast<object>().ToArray());
    }

    private static void ConfigureProfileCombo(ComboBox combo)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Dock = DockStyle.Fill;
        if (combo.Items.Count == 0)
        {
            combo.Items.AddRange(
            [
                new ProfileOption(CorrectionProfile.Careful, "Осторожный - меньше ложных срабатываний"),
                new ProfileOption(CorrectionProfile.Balanced, "Обычный - текущий баланс"),
                new ProfileOption(CorrectionProfile.Bold, "Смелый - активнее исправляет короткие слова"),
            ]);
        }
    }

    private void SetProfileComboValue(CorrectionProfile profile)
    {
        foreach (var item in _correctionProfile.Items.OfType<ProfileOption>())
        {
            if (item.Profile == profile)
            {
                _correctionProfile.SelectedItem = item;
                return;
            }
        }

        _correctionProfile.SelectedIndex = 1;
    }

    private static void ConfigureReplacementGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = true;
        grid.AllowUserToDeleteRows = true;
        grid.AutoGenerateColumns = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = true;
        grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.Columns.Clear();
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Original",
            HeaderText = "Исходное",
            FillWeight = 34,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Corrected",
            HeaderText = "Исправленное",
            FillWeight = 34,
        });
        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Enabled",
            HeaderText = "Вкл.",
            FillWeight = 14,
        });
        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Learned",
            HeaderText = "Выучено",
            FillWeight = 18,
            ReadOnly = true,
        });
    }

    private void PopulateReplacementGrid(IEnumerable<AutoReplacementRule> rules)
    {
        _autoReplacementsGrid.Rows.Clear();
        foreach (var rule in rules)
        {
            _autoReplacementsGrid.Rows.Add(rule.Original, rule.Corrected, rule.Enabled, rule.Learned);
        }
    }

    private List<AutoReplacementRule> ReadReplacementRulesFromGrid(out List<string> errors)
    {
        errors = [];
        var rules = new List<AutoReplacementRule>();
        var rowNumber = 0;
        foreach (DataGridViewRow row in _autoReplacementsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            rowNumber++;
            var original = Convert.ToString(row.Cells["Original"].Value)?.Trim() ?? "";
            var corrected = Convert.ToString(row.Cells["Corrected"].Value)?.Trim() ?? "";
            if (original.Length == 0 && corrected.Length == 0)
            {
                continue;
            }

            if (!AutoReplacementRule.IsValidText(original) || !AutoReplacementRule.IsValidText(corrected))
            {
                errors.Add($"Строка {rowNumber}: слова должны быть без пробелов и не длиннее 64 символов.");
                continue;
            }

            if (string.Equals(original, corrected, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Строка {rowNumber}: исходное и исправленное слово совпадают.");
                continue;
            }

            rules.Add(new AutoReplacementRule
            {
                Original = original,
                Corrected = corrected,
                Enabled = row.Cells["Enabled"].Value is not bool enabled || enabled,
                Learned = row.Cells["Learned"].Value is bool learned && learned,
                CreatedAt = DateTime.Now,
            });
        }

        return AutoReplacementRule.Normalize(rules);
    }

    private static void ConfigureButton(Button button, string text, int width)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 36;
        button.AutoEllipsis = true;
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
            AutoEllipsis = true,
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

    private void ApplyTheme()
    {
        var dark = _context.Settings.DarkTheme;
        var back = dark ? Color.FromArgb(18, 24, 32) : Color.FromArgb(247, 249, 252);
        var surface = dark ? Color.FromArgb(30, 41, 59) : Color.White;
        var text = dark ? Color.FromArgb(226, 232, 240) : Color.FromArgb(24, 32, 43);
        var muted = dark ? Color.FromArgb(148, 163, 184) : Color.FromArgb(71, 85, 105);
        var border = dark ? Color.FromArgb(71, 85, 105) : Color.FromArgb(203, 213, 225);

        ApplyThemeTo(this, back, surface, text, muted, border);
        _statusLabel.ForeColor = muted;
        _lastActionLabel.ForeColor = muted;
        _hotkeysLabel.ForeColor = muted;
        _installStatus.ForeColor = muted;
        _updateStatus.ForeColor = muted;
    }

    private static void ApplyThemeTo(Control control, Color back, Color surface, Color text, Color muted, Color border)
    {
        switch (control)
        {
            case TextBox textBox:
                textBox.BackColor = surface;
                textBox.ForeColor = text;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ListBox listBox:
                listBox.BackColor = surface;
                listBox.ForeColor = text;
                break;
            case ComboBox comboBox:
                comboBox.BackColor = surface;
                comboBox.ForeColor = text;
                break;
            case DataGridView grid:
                grid.BackgroundColor = surface;
                grid.GridColor = border;
                grid.DefaultCellStyle.BackColor = surface;
                grid.DefaultCellStyle.ForeColor = text;
                grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(15, 118, 110);
                grid.DefaultCellStyle.SelectionForeColor = Color.White;
                grid.ColumnHeadersDefaultCellStyle.BackColor = back;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = text;
                grid.EnableHeadersVisualStyles = false;
                break;
            case Button button:
                button.BackColor = surface;
                button.ForeColor = text;
                button.FlatStyle = FlatStyle.Standard;
                break;
            default:
                control.BackColor = back;
                control.ForeColor = text;
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyThemeTo(child, back, surface, text, muted, border);
        }
    }

    private sealed record ProfileOption(CorrectionProfile Profile, string Text)
    {
        public override string ToString()
        {
            return Text;
        }
    }
}

internal sealed class ProcessPickerDialog : Form
{
    private readonly ListBox _processes = new();

    public ProcessPickerDialog(
        IReadOnlyList<ProcessOption> options,
        IEnumerable<string> excludedProcesses,
        bool darkTheme)
    {
        Text = "Выбрать процессы";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(620, 520);
        ClientSize = new Size(720, 560);
        Font = new Font("Segoe UI", 10F);

        var excluded = new HashSet<string>(excludedProcesses, StringComparer.OrdinalIgnoreCase);
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            RowCount = 3,
            ColumnCount = 1,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "Выберите процессы, где Switcher не должен автоматически исправлять ввод.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);

        _processes.Dock = DockStyle.Fill;
        _processes.SelectionMode = SelectionMode.MultiExtended;
        _processes.HorizontalScrollbar = true;
        foreach (var option in options.Where(option => !excluded.Contains(option.ProcessName)))
        {
            _processes.Items.Add(option);
        }

        root.Controls.Add(_processes, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
        };
        var ok = new Button
        {
            Text = "Добавить",
            Width = 130,
            Height = 34,
        };
        var cancel = new Button
        {
            Text = "Отмена",
            Width = 110,
            Height = 34,
            DialogResult = DialogResult.Cancel,
        };
        ok.Click += (_, _) =>
        {
            SelectedProcessNames = _processes.SelectedItems
                .OfType<ProcessOption>()
                .Select(option => option.ProcessName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            DialogResult = DialogResult.OK;
            Close();
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 2);

        AcceptButton = ok;
        CancelButton = cancel;
        ApplyTheme(darkTheme);
    }

    public IReadOnlyList<string> SelectedProcessNames { get; private set; } = [];

    private void ApplyTheme(bool darkTheme)
    {
        var back = darkTheme ? Color.FromArgb(18, 24, 32) : Color.FromArgb(247, 249, 252);
        var surface = darkTheme ? Color.FromArgb(30, 41, 59) : Color.White;
        var text = darkTheme ? Color.FromArgb(226, 232, 240) : Color.FromArgb(24, 32, 43);
        BackColor = back;
        ForeColor = text;
        foreach (Control control in Controls)
        {
            ApplyThemeTo(control, back, surface, text);
        }
    }

    private static void ApplyThemeTo(Control control, Color back, Color surface, Color text)
    {
        control.BackColor = control is TextBox or ListBox or ComboBox or Button ? surface : back;
        control.ForeColor = text;
        foreach (Control child in control.Controls)
        {
            ApplyThemeTo(child, back, surface, text);
        }
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
    public bool IgnorePasswordFields { get; set; } = true;
    public bool DarkTheme { get; set; }
    public bool FirstRunHintShown { get; set; }
    public CorrectionProfile CorrectionProfile { get; set; } = CorrectionProfile.Balanced;
    public int BuiltInReplacementsVersion { get; set; }
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
    public List<DecisionLogItem> DecisionLog { get; set; } = [];
}

internal sealed record AutoReplacementRule
{
    public string Original { get; set; } = "";
    public string Corrected { get; set; } = "";
    public bool Enabled { get; set; } = true;
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
                Enabled = rule.Enabled,
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
            existing.Enabled = true;
            existing.Learned = existing.Learned && learned;
            return true;
        }

        rules.Insert(0, new AutoReplacementRule
        {
            Original = original,
            Corrected = corrected,
            Enabled = true,
            Learned = learned,
            CreatedAt = DateTime.Now,
        });
        return true;
    }
}

internal static class AutoReplacementDefaults
{
    public const int Version = 1;

    private const string LatinKeys = "`qwertyuiop[]asdfghjkl;'zxcvbnm,./";
    private const string CyrillicKeys = "ёйцукенгшщзхъфывапролджэячсмитьбю.";

    private static readonly string[] RussianWords =
    [
        "привет", "пока", "спасибо", "пожалуйста", "да", "нет", "как", "что", "это",
        "меня", "тебя", "тест", "текст", "код", "проект", "работа", "файл", "папка",
        "пароль", "логин", "настройки", "ошибка", "исправить", "сделать", "отправить",
        "сообщение", "документ", "таблица", "встреча", "задача", "список", "данные",
        "сервер", "клиент", "браузер", "страница", "кнопка", "форма", "поиск", "ответ",
        "вопрос", "пример", "команда", "сборка", "приложение", "раскладка", "русский",
        "английский", "система", "слово", "замена", "звук", "назад", "вперед", "отмена",
        "готово", "открыть", "закрыть", "сохранить", "удалить", "обновить", "скопировать",
        "вставить", "адрес", "телефон", "номер", "город", "москва", "россия", "время",
        "день", "ночь", "утро", "вечер", "хорошо", "плохо", "важно", "срочно", "проверка",
        "решение", "проблема", "версия", "релиз", "коммит", "ветка", "репозиторий",
        "программа", "утилита", "пользователь", "профиль", "запуск", "автозагрузка",
        "скачать", "установить", "установка", "импорт", "экспорт", "горячие", "клавиши",
        "клавиатура", "выделение", "буфер", "обмен", "копия", "подсказка", "инструкция",
        "помощь", "раздел", "вкладка", "путь", "локально", "параметр", "проверить",
        "собрать", "выпустить", "опубликовать", "скачивание", "фоновая", "быстро",
        "медленно", "работает", "запущено", "установлено", "доступно", "найдено",
        "ссылка", "понял", "давай", "нормально", "сейчас", "потом", "после", "перед",
        "новый", "старый", "общий",
    ];

    private static readonly string[] EnglishWords =
    [
        "hello", "hi", "thanks", "thank", "please", "yes", "no", "this", "that", "what",
        "when", "where", "why", "work", "project", "code", "test", "text", "window",
        "site", "email", "file", "folder", "password", "login", "settings", "error",
        "fix", "make", "send", "message", "document", "table", "calendar", "meeting",
        "call", "task", "list", "data", "server", "client", "browser", "page", "button",
        "form", "search", "answer", "question", "example", "command", "build", "application",
        "app", "switcher", "layout", "russian", "english", "system", "auto", "word", "words",
        "replace", "sound", "back", "forward", "undo", "ready", "start", "close", "open",
        "save", "delete", "update", "copy", "paste", "admin", "address", "phone", "number",
        "city", "time", "day", "night", "morning", "evening", "good", "bad", "important",
        "urgent", "check", "solution", "problem", "version", "release", "commit", "branch",
        "repository", "user", "name", "value", "key", "token", "api", "my", "me", "we",
        "us", "it", "is", "am", "in", "on", "of", "to", "as", "at", "if", "do", "go",
        "you", "not", "and", "but", "can", "may", "github", "windows", "keyboard", "shortcut",
        "hotkey", "design", "program", "utility", "profile", "startup", "install", "installer",
        "portable", "download", "upload", "import", "export", "json", "background", "silent",
        "notification", "tray", "menu", "dialog", "selection", "clipboard", "configuration",
        "config", "help", "hint", "guide", "first", "last", "new", "old", "current", "latest",
        "available", "found", "path", "link", "normal", "now", "later", "before", "after", "next",
    ];

    public static List<AutoReplacementRule> Create()
    {
        var rules = new List<AutoReplacementRule>();
        foreach (var word in RussianWords)
        {
            AddRule(rules, ConvertWithMap(word, CyrillicKeys, LatinKeys), word);
        }

        foreach (var word in EnglishWords)
        {
            AddRule(rules, ConvertWithMap(word, LatinKeys, CyrillicKeys), word);
        }

        return AutoReplacementRule.Normalize(rules);
    }

    public static void MergeInto(List<AutoReplacementRule> rules)
    {
        var existing = new HashSet<string>(rules.Select(rule => rule.Original), StringComparer.OrdinalIgnoreCase);
        foreach (var rule in Create())
        {
            if (existing.Add(rule.Original))
            {
                rules.Add(rule);
            }
        }
    }

    private static void AddRule(List<AutoReplacementRule> rules, string original, string corrected)
    {
        if (string.Equals(original, corrected, StringComparison.OrdinalIgnoreCase)
            || !AutoReplacementRule.IsValidText(original)
            || !AutoReplacementRule.IsValidText(corrected))
        {
            return;
        }

        rules.Add(new AutoReplacementRule
        {
            Original = original,
            Corrected = corrected,
            Enabled = true,
            Learned = false,
            CreatedAt = DateTime.Now,
        });
    }

    private static string ConvertWithMap(string text, string source, string target)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            var lower = char.ToLowerInvariant(ch);
            var index = source.IndexOf(lower, StringComparison.Ordinal);
            if (index < 0)
            {
                builder.Append(ch);
                continue;
            }

            var converted = target[index];
            builder.Append(char.IsUpper(ch) ? char.ToUpperInvariant(converted) : converted);
        }

        return builder.ToString();
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

internal sealed record DecisionLogItem
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Kind { get; set; } = "";
    public string Original { get; set; } = "";
    public string Corrected { get; set; } = "";
    public string Reason { get; set; } = "";
    public bool Applied { get; set; }

    public override string ToString()
    {
        var action = Applied ? "исправлено" : "пропущено";
        var pair = string.IsNullOrWhiteSpace(Corrected)
            ? Original
            : $"{Original} -> {Corrected}";
        return $"{Timestamp:HH:mm:ss} {Kind} ({action}): {pair}. {Reason}";
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
                return Normalize(new AppSettings());
            }

            var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            return Normalize(JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings());
        }
        catch
        {
            return Normalize(new AppSettings());
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
        settings.DecisionLog ??= [];
        settings.ExcludedProcesses = NormalizeList(settings.ExcludedProcesses);
        settings.CustomRussianWords = NormalizeList(settings.CustomRussianWords);
        settings.CustomEnglishWords = NormalizeList(settings.CustomEnglishWords);
        settings.CustomNeverCorrectWords = NormalizeList(settings.CustomNeverCorrectWords);
        settings.CustomAutoReplacements = AutoReplacementRule.Normalize(settings.CustomAutoReplacements);
        if (settings.BuiltInReplacementsVersion < AutoReplacementDefaults.Version)
        {
            AutoReplacementDefaults.MergeInto(settings.CustomAutoReplacements);
            settings.CustomAutoReplacements = AutoReplacementRule.Normalize(settings.CustomAutoReplacements);
            settings.BuiltInReplacementsVersion = AutoReplacementDefaults.Version;
        }

        settings.History = settings.History
            .Where(item => !string.IsNullOrWhiteSpace(item.Original) || !string.IsNullOrWhiteSpace(item.Corrected))
            .Take(30)
            .ToList();
        settings.DecisionLog = settings.DecisionLog
            .Where(item => !string.IsNullOrWhiteSpace(item.Original) || !string.IsNullOrWhiteSpace(item.Reason))
            .Take(80)
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

internal sealed record DownloadProgress(long BytesReceived, long? TotalBytes)
{
    public int? Percent => TotalBytes is > 0
        ? (int)Math.Clamp(BytesReceived * 100 / TotalBytes.Value, 0, 100)
        : null;

    public string StatusText
    {
        get
        {
            var received = FormatBytes(BytesReceived);
            return TotalBytes is > 0
                ? $"{Percent ?? 0}% ({received} из {FormatBytes(TotalBytes.Value)})"
                : received;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{value:0} {units[unit]}"
            : $"{value:0.0} {units[unit]}";
    }
}

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

    public static async Task<string> DownloadUpdateAsync(
        UpdateInfo update,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"Switcher.{update.TagName}.exe");
        using var client = CreateClient(TimeSpan.FromMinutes(10));
        using var response = await client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var contentLength = response.Content.Headers.ContentLength;
        long? totalBytes = contentLength is > 0
            ? contentLength.Value
            : update.Size > 0 ? update.Size : null;

        progress?.Report(new DownloadProgress(0, totalBytes));
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(tempPath);
        var buffer = new byte[81920];
        long received = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            progress?.Report(new DownloadProgress(received, totalBytes));
        }

        progress?.Report(new DownloadProgress(received, totalBytes ?? received));
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

internal enum CorrectionProfile
{
    Careful,
    Balanced,
    Bold,
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

internal sealed record ProcessOption(string ProcessName, string Title, string Path)
{
    public bool HasWindow => !string.IsNullOrWhiteSpace(Title);

    public string DisplayText => HasWindow
        ? $"{ProcessName} - {Title}"
        : ProcessName;

    public static ProcessOption? TryCreate(Process process)
    {
        try
        {
            var processName = process.ProcessName;
            if (string.IsNullOrWhiteSpace(processName))
            {
                return null;
            }

            var title = process.MainWindowTitle ?? "";
            var path = "";
            try
            {
                path = process.MainModule?.FileName ?? "";
            }
            catch
            {
                // Some system processes do not expose their module path to a normal user.
            }

            return new ProcessOption(processName, title, path);
        }
        catch
        {
            return null;
        }
    }

    public override string ToString()
    {
        return DisplayText;
    }
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

        var thresholds = CorrectionThresholds.For(settings.CorrectionProfile);
        var allowShortGuess = settings.AggressiveShortWords || settings.CorrectionProfile == CorrectionProfile.Bold;
        if (word.Length < 4 && !targetIsKnownWord && !allowShortGuess)
        {
            reason = $"короткое слово, цель не найдена в словаре, профиль {DisplayProfile(settings.CorrectionProfile)}";
            return false;
        }

        if (targetIsKnownWord && targetScore - sourceScore >= thresholds.KnownWordDelta)
        {
            correction = converted;
            reason = $"цель есть в словаре, профиль {DisplayProfile(settings.CorrectionProfile)}, score {sourceScore}->{targetScore}";
            return true;
        }

        if (word.Length < 4
            && allowShortGuess
            && targetScore >= thresholds.ShortWordTargetScore
            && targetScore - sourceScore >= thresholds.ShortWordDelta)
        {
            correction = converted;
            reason = $"короткое слово разрешено профилем {DisplayProfile(settings.CorrectionProfile)}, score {sourceScore}->{targetScore}";
            return true;
        }

        if (targetScore >= thresholds.TargetScore && targetScore - sourceScore >= thresholds.ScoreDelta)
        {
            correction = converted;
            reason = $"эвристика уверена, профиль {DisplayProfile(settings.CorrectionProfile)}, score {sourceScore}->{targetScore}";
            return true;
        }

        reason = $"недостаточная уверенность для профиля {DisplayProfile(settings.CorrectionProfile)}, score {sourceScore}->{targetScore}, цель известна: {targetIsKnownWord}";
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
            if (rule.Enabled
                && string.Equals(rule.Original, word, StringComparison.OrdinalIgnoreCase)
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

    private static string DisplayProfile(CorrectionProfile profile)
    {
        return profile switch
        {
            CorrectionProfile.Careful => "Осторожный",
            CorrectionProfile.Bold => "Смелый",
            _ => "Обычный",
        };
    }

    private sealed record CorrectionThresholds(
        int KnownWordDelta,
        int ShortWordTargetScore,
        int ShortWordDelta,
        int TargetScore,
        int ScoreDelta)
    {
        public static CorrectionThresholds For(CorrectionProfile profile)
        {
            return profile switch
            {
                CorrectionProfile.Careful => new CorrectionThresholds(12, 14, 12, 28, 20),
                CorrectionProfile.Bold => new CorrectionThresholds(5, 8, 6, 18, 10),
                _ => new CorrectionThresholds(8, 10, 8, 22, 14),
            };
        }
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
    private const int EmGetPasswordChar = 0x00D2;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int CbSize;
        public uint Flags;
        public IntPtr HwndActive;
        public IntPtr HwndFocus;
        public IntPtr HwndCapture;
        public IntPtr HwndMenuOwner;
        public IntPtr HwndMoveSize;
        public IntPtr HwndCaret;
        public Rect RcCaret;

        public static GuiThreadInfo Create()
        {
            return new GuiThreadInfo
            {
                CbSize = Marshal.SizeOf<GuiThreadInfo>(),
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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

    public static bool IsForegroundPasswordField()
    {
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return false;
            }

            var threadId = GetWindowThreadProcessId(foreground, out _);
            var info = GuiThreadInfo.Create();
            var focused = GetGUIThreadInfo(threadId, ref info) && info.HwndFocus != IntPtr.Zero
                ? info.HwndFocus
                : foreground;

            var className = new StringBuilder(256);
            GetClassName(focused, className, className.Capacity);
            var passwordChar = SendMessage(focused, EmGetPasswordChar, IntPtr.Zero, IntPtr.Zero);
            return passwordChar != IntPtr.Zero
                && className.ToString().Contains("edit", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GuiThreadInfo guiThreadInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

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
