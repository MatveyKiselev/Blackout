using Blackout.Core;
using System.ComponentModel;

namespace Blackout.Windows;

/// <summary>
/// Settings window on plain Win32 controls. Everything is applied immediately; the form is hidden
/// on close so the tray and the hotkey keep running.
/// </summary>
internal sealed class SettingsForm : Form
{
    private const int HintWidth = 470;

    private readonly BlackoutController _controller;

    private readonly CheckBox _autostart = new()
    {
        Text = "Запускать вместе с Windows",
        AutoSize = true,
    };

    private readonly Button _hotkey = new()
    {
        AutoSize = true,
        MinimumSize = new Size(180, 32),
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private readonly CheckBox _idleEnabled = new()
    {
        Text = "Гасить экран, если пользователя нет за компьютером",
        AutoSize = true,
    };

    private readonly NumericUpDown _idleMinutes = new()
    {
        Minimum = 1,
        Maximum = 120,
        Width = 64,
    };

    private readonly CheckedListBox _monitors = new()
    {
        CheckOnClick = true,
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.FixedSingle,
        IntegralHeight = false,
    };

    private readonly Label _status = new()
    {
        AutoSize = true,
        MaximumSize = new Size(HintWidth, 0),
        Margin = new Padding(0, 0, 0, 6),
    };

    private readonly Button _toggle = new() { AutoSize = true };

    private bool _suppress;
    private bool _capturing;

    public SettingsForm(BlackoutController controller)
    {
        _controller = controller;

        Text = "Blackout";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(600, 780);
        MinimumSize = new Size(520, 560);
        Icon = LoadWindowIcon();

        BuildLayout();
        ApplySettingsToUi();

        _controller.StateChanged += OnControllerStateChanged;
        Shown += (_, _) => ApplySettingsToUi();
        VisibleChanged += (_, _) =>
        {
            if (Visible)
            {
                UpdateStatus();
            }
        };
    }

    /// <summary>Set by the shell before an intentional close, so hiding stays the default.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AllowClose { get; set; }

    /// <summary>Raised when the user asked to close the window; the shell hides it instead.</summary>
    public event EventHandler? HideRequested;

    private static Icon LoadWindowIcon()
    {
        var executable = Environment.ProcessPath;
        if (executable is not null)
        {
            try
            {
                var extracted = Icon.ExtractAssociatedIcon(executable);
                if (extracted is not null)
                {
                    return extracted;
                }
            }
            catch (ArgumentException)
            {
                // Fall through to the system icon.
            }
        }

        return SystemIcons.Application;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoScroll = true,
            Padding = new Padding(16, 12, 16, 12),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(root, new Label
        {
            Text = "Blackout",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 14f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 2),
        });

        AddRow(root, new Label
        {
            Text = "Чёрный экран для OLED-панелей. Приложение живёт в трее, закрытие окна его скрывает.",
            AutoSize = true,
            MaximumSize = new Size(HintWidth, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 8),
        });

        AddRow(root, _status);
        AddRow(root, BuildAutostartGroup());
        AddRow(root, BuildHotkeyGroup());
        AddRow(root, BuildIdleGroup());
        AddRow(root, BuildMonitorsGroup());
        AddRow(root, BuildFooter());

        Controls.Add(root);
    }

    private static void AddRow(TableLayoutPanel table, Control control)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Dock = DockStyle.Top;
        table.Controls.Add(control, 0, row);
    }

    private GroupBox BuildAutostartGroup()
    {
        _autostart.CheckedChanged += OnAutostartChanged;

        var stack = NewStack();
        stack.Controls.Add(_autostart);
        stack.Controls.Add(Hint(@"Добавляет запись в HKCU\...\Run с ключом --background."));

        return NewGroup("Запуск с системой", stack);
    }

    private GroupBox BuildHotkeyGroup()
    {
        _hotkey.Click += OnHotkeyClicked;

        var stack = NewStack();
        stack.Controls.Add(_hotkey);
        stack.Controls.Add(Hint("Нажмите кнопку и введите комбинацию. Esc отменяет ввод."));
        stack.Controls.Add(Hint("Пока чёрный экран активен, Esc снимает его."));

        return NewGroup("Горячая клавиша", stack);
    }

    private GroupBox BuildIdleGroup()
    {
        _idleEnabled.CheckedChanged += OnIdleEnabledChanged;
        _idleMinutes.ValueChanged += OnIdleMinutesChanged;

        var minutesRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
        };
        minutesRow.Controls.Add(_idleMinutes);
        minutesRow.Controls.Add(new Label
        {
            Text = "минут без ввода",
            AutoSize = true,
            Margin = new Padding(6, 6, 0, 0),
        });

        var stack = NewStack();
        stack.Controls.Add(_idleEnabled);
        stack.Controls.Add(minutesRow);
        stack.Controls.Add(Hint("Отсчёт идёт по последнему вводу. После Esc таймер не сработает повторно, пока вы не вернётесь."));

        return NewGroup("Автоматически по бездействию", stack);
    }

    private GroupBox BuildMonitorsGroup()
    {
        _monitors.ItemCheck += OnMonitorItemCheck;

        var refresh = new Button { Text = "Обновить список", AutoSize = true };
        refresh.Click += (_, _) => ApplySettingsToUi();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(_monitors, 0, 0);
        layout.Controls.Add(refresh, 0, 1);
        layout.Controls.Add(Hint("Снимите все галочки, чтобы затемнять все мониторы, включая новые."), 0, 2);

        var group = new GroupBox
        {
            Text = "Мониторы",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(12, 8, 12, 12),
        };
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildFooter()
    {
        _toggle.Click += (_, _) =>
        {
            _controller.Toggle();
            UpdateStatus();
        };

        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0),
        };
        row.Controls.Add(_toggle);
        row.Controls.Add(new Label
        {
            Text = _controller.SettingsFilePath,
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(12, 8, 0, 0),
        });

        return row;
    }

    private static FlowLayoutPanel NewStack() => new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Dock = DockStyle.Top,
        Margin = new Padding(0),
    };

    private static GroupBox NewGroup(string title, Control content)
    {
        var group = new GroupBox
        {
            Text = title,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(12, 8, 12, 12),
            Margin = new Padding(0, 0, 0, 10),
        };
        group.Controls.Add(content);
        return group;
    }

    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(HintWidth, 0),
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(0, 2, 0, 2),
    };

    private void ApplySettingsToUi()
    {
        _suppress = true;

        _autostart.Checked = _controller.IsAutostartEnabled;
        _idleEnabled.Checked = _controller.Settings.IdleBlackoutEnabled;
        _idleMinutes.Value = Math.Clamp(_controller.Settings.IdleTimeoutMinutes, 1, 120);
        _idleMinutes.Enabled = _idleEnabled.Checked;

        BuildMonitorList();

        _suppress = false;
        UpdateStatus();
    }

    private void BuildMonitorList()
    {
        _monitors.Items.Clear();

        var stored = _controller.Settings.EnabledMonitorIds;
        var selectAll = stored.Count == 0;

        foreach (var monitor in _controller.GetMonitors())
        {
            var index = _monitors.Items.Add(new MonitorItem(monitor));
            _monitors.SetItemChecked(
                index,
                selectAll || stored.Contains(monitor.Id, StringComparer.OrdinalIgnoreCase));
        }

        _monitors.Enabled = _monitors.Items.Count > 0;
    }

    private void OnMonitorItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_suppress)
        {
            return;
        }

        // The new state is applied after this event, so read it back on the next message.
        BeginInvoke(SaveMonitorSelection);
    }

    private void SaveMonitorSelection()
    {
        var selected = _monitors.CheckedItems
            .Cast<MonitorItem>()
            .Select(item => item.Monitor.Id)
            .ToList();

        // "All checked" is stored as an empty list so a newly attached display is covered too.
        var desired = selected.Count == _monitors.Items.Count
            ? new List<string>()
            : selected;

        if (SameSelection(desired, _controller.Settings.EnabledMonitorIds))
        {
            return;
        }

        _controller.SetSelectedMonitors(desired);
        UpdateStatus();
    }

    private static bool SameSelection(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.Count == right.Count &&
        !left.Except(right, StringComparer.OrdinalIgnoreCase).Any();

    private void OnAutostartChanged(object? sender, EventArgs e)
    {
        if (_suppress)
        {
            return;
        }

        _controller.SetAutostart(_autostart.Checked);
        UpdateStatus();
    }

    private void OnIdleEnabledChanged(object? sender, EventArgs e)
    {
        _idleMinutes.Enabled = _idleEnabled.Checked;

        if (_suppress)
        {
            return;
        }

        _controller.SetIdleBlackout(_idleEnabled.Checked, (int)_idleMinutes.Value);
        UpdateStatus();
    }

    private void OnIdleMinutesChanged(object? sender, EventArgs e)
    {
        if (_suppress)
        {
            return;
        }

        _controller.SetIdleBlackout(_idleEnabled.Checked, (int)_idleMinutes.Value);
    }

    private void OnHotkeyClicked(object? sender, EventArgs e)
    {
        if (_capturing)
        {
            return;
        }

        _capturing = true;
        // The global hotkey must not fire while the user is recording a replacement.
        _controller.PauseHotkey();
        _hotkey.Text = "Нажмите комбинацию… (Esc — отмена)";
        _hotkey.Focus();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!_capturing)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        CaptureKey(keyData);
        return true;
    }

    private void CaptureKey(Keys keyData)
    {
        var modifiers = HotkeyModifiers.None;
        if ((keyData & Keys.Control) != 0)
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if ((keyData & Keys.Alt) != 0)
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if ((keyData & Keys.Shift) != 0)
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        var key = keyData & Keys.KeyCode;

        // Modifiers alone are not a combination; keep waiting for the real key.
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            return;
        }

        if (key == Keys.Escape && modifiers == HotkeyModifiers.None)
        {
            EndCapture();
            _controller.ResumeHotkey();
            return;
        }

        if (!Enum.IsDefined(typeof(KeyCode), (KeyCode)key))
        {
            return;
        }

        EndCapture();
        _controller.TrySetHotkey(new HotkeySpec(modifiers, (KeyCode)key));
    }

    private void EndCapture()
    {
        _capturing = false;
        UpdateStatus();
    }

    private void OnControllerStateChanged(object? sender, EventArgs e) => UpdateStatus();

    private void UpdateStatus()
    {
        if (!_capturing)
        {
            _hotkey.Text = _controller.Settings.EffectiveHotkey.DisplayName;
        }

        var lines = new List<string>();

        if (_controller.HotkeyError is { } hotkeyError)
        {
            lines.Add($"Горячая клавиша не зарегистрирована: {hotkeyError}");
        }

        if (_controller.LastError is { } lastError)
        {
            lines.Add(lastError);
        }

        lines.Add(_controller.IsBlackoutActive
            ? "Оверлей активен. Esc — вернуться."
            : $"Активно: {_controller.Settings.EffectiveHotkey.DisplayName} — показать чёрный экран.");

        _status.Text = string.Join(Environment.NewLine, lines);
        _toggle.Text = _controller.IsBlackoutActive ? "Убрать чёрный экран" : "Показать чёрный экран";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Any close except a real shutdown or an explicit exit hides the window instead.
        if (!AllowClose &&
            e.CloseReason is not (CloseReason.ApplicationExitCall or CloseReason.WindowsShutDown))
        {
            e.Cancel = true;
            HideRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _controller.StateChanged -= OnControllerStateChanged;
        }

        base.Dispose(disposing);
    }

    private sealed class MonitorItem(MonitorInfo monitor)
    {
        public MonitorInfo Monitor { get; } = monitor;

        public override string ToString() => Monitor.DisplayName;
    }
}
