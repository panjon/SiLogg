using System.ComponentModel;
using SiLogg.Reader.Models;

namespace SiLogg.Reader;

public sealed class MainForm : Form
{
    private readonly ReadoutCoordinator _coordinator;
    private readonly Label _statusLabel;
    private readonly Label _currentStationLabel;
    private readonly ComboBox _deviceComboBox;
    private readonly RadioButton _remoteModeRadio;
    private readonly RadioButton _directModeRadio;
    private readonly CheckBox _soundCheckBox;
    private readonly Button _forceReadButton;
    private readonly DataGridView _logGrid;
    private bool _updatingDeviceList;
    private bool _errorSoundPlayed;
    private bool _questionSoundPlayed;

    public MainForm(ReaderOptions options)
    {
        _coordinator = new ReadoutCoordinator(options);

        Text = options.UseMockDevice ? "SiLogg Reader (MOCK)" : "SiLogg Reader";
        Width = 900;
        Height = 500;
        MinimumSize = new Size(700, 400);

        _statusLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 32,
            Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
            Text = "Startar..."
        };

        _currentStationLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "Aktuell kontroll: -"
        };

        _deviceComboBox = new ComboBox
        {
            Dock = DockStyle.Top,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _deviceComboBox.Items.Add("Auto (första hittade enhet)");
        _deviceComboBox.SelectedIndex = 0;
        _deviceComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingDeviceList)
            {
                return;
            }
            var selected = _deviceComboBox.SelectedIndex <= 0 ? null : (string)_deviceComboBox.SelectedItem!;
            _coordinator.SelectDevice(selected);
        };

        _forceReadButton = new Button
        {
            Dock = DockStyle.Top,
            Height = 32,
            Text = "Tvinga läsning",
            Enabled = false
        };
        _forceReadButton.Click += (_, _) => _coordinator.ForceReadout();

        _soundCheckBox = new CheckBox
        {
            Dock = DockStyle.Top,
            Height = 32,
            Text = "Ljud när utläsning är klar",
            Checked = options.EnableSound,
            AutoSize = false,
        };

        var targetModePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 32,
            FlowDirection = FlowDirection.LeftToRight,
        };
        _remoteModeRadio = new RadioButton { Text = "Remote (färransluten kontroll)", AutoSize = true, Checked = _coordinator.DefaultTargetMode == TargetMode.Remote };
        _directModeRadio = new RadioButton { Text = "Direct (lokalt ansluten enhet)", AutoSize = true, Checked = _coordinator.DefaultTargetMode == TargetMode.Direct, Margin = new Padding(12, 3, 3, 3) };
        _remoteModeRadio.CheckedChanged += (_, _) =>
        {
            if (_remoteModeRadio.Checked)
            {
                _coordinator.SetTargetMode(TargetMode.Remote);
            }
        };
        _directModeRadio.CheckedChanged += (_, _) =>
        {
            if (_directModeRadio.Checked)
            {
                _coordinator.SetTargetMode(TargetMode.Direct);
            }
        };
        targetModePanel.Controls.Add(_remoteModeRadio);
        targetModePanel.Controls.Add(_directModeRadio);

        _logGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
        };
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tid", DataPropertyName = nameof(LogEntry.Time), Width = 130 });
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Serienummer", DataPropertyName = nameof(LogEntry.StationSerial), Width = 100 });
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Code number", DataPropertyName = nameof(LogEntry.CodeNumber), Width = 100 });
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Antal stämplingar", DataPropertyName = nameof(LogEntry.PunchCount), Width = 120 });
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "JSON", DataPropertyName = nameof(LogEntry.JsonStatus), Width = 180 });
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Uppladdning", DataPropertyName = nameof(LogEntry.UploadStatus), Width = 180 });
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Typ", DataPropertyName = nameof(LogEntry.Type), Width = 80 });
        _logGrid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "RetryUpload",
            HeaderText = "Åtgärd",
            Text = "Ladda upp igen",
            UseColumnTextForButtonValue = true,
            Width = 130
        });
        _logGrid.CellContentClick += async (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _logGrid.Columns["RetryUpload"]!.Index)
            {
                return;
            }

            if (_logGrid.Rows[e.RowIndex].DataBoundItem is LogEntry entry)
            {
                await _coordinator.RetryUploadAsync(entry);
            }
        };

        Controls.Add(_logGrid);
        Controls.Add(_soundCheckBox);
        Controls.Add(_forceReadButton);
        Controls.Add(_deviceComboBox);
        Controls.Add(targetModePanel);
        Controls.Add(_currentStationLabel);
        Controls.Add(_statusLabel);

        _coordinator.StatusChanged += OnStatusChanged;
        _coordinator.CurrentStationChanged += OnCurrentStationChanged;
        _coordinator.AvailableDevicesChanged += OnAvailableDevicesChanged;
        _coordinator.ReadoutCompleted += OnReadoutCompleted;
        _logGrid.DataSource = _coordinator.Log;

        Load += (_, _) => _coordinator.Start();
        FormClosed += (_, _) => _coordinator.Dispose();
    }

    private void OnStatusChanged(string message) => RunOnUiThread(() =>
    {
        _statusLabel.Text = message;
        _statusLabel.BackColor = GetStatusColor(message);
        _statusLabel.ForeColor = _statusLabel.BackColor is Color color
            && (color == Color.Gold || color == Color.DarkOrange)
            ? Color.Black
            : Color.White;

        var isError = IsErrorStatus(message);
        var isQuestion = message.Contains("Tvinga läsning", StringComparison.OrdinalIgnoreCase);
        if (isQuestion && !_questionSoundPlayed && _soundCheckBox.Checked)
        {
            System.Media.SystemSounds.Question.Play();
            _questionSoundPlayed = true;
        }
        else if (!isQuestion)
        {
            _questionSoundPlayed = false;
        }

        if (isError && !_errorSoundPlayed && _soundCheckBox.Checked)
        {
            System.Media.SystemSounds.Hand.Play();
            _errorSoundPlayed = true;
        }
        else if (!isError)
        {
            _errorSoundPlayed = false;
        }
    });

    private static bool IsErrorStatus(string message) =>
        message.Contains("fel", StringComparison.OrdinalIgnoreCase)
        || message.Contains("misslyck", StringComparison.OrdinalIgnoreCase)
        || message.Contains("kunde inte", StringComparison.OrdinalIgnoreCase);

    private void OnReadoutCompleted()
    {
        if (_soundCheckBox.Checked)
        {
            System.Media.SystemSounds.Asterisk.Play();
        }
    }

    private static Color GetStatusColor(string message)
    {
        if (message.Contains("fel", StringComparison.OrdinalIgnoreCase)
            || message.Contains("misslyck", StringComparison.OrdinalIgnoreCase)
            || message.Contains("kunde inte", StringComparison.OrdinalIgnoreCase))
        {
            return Color.Firebrick;
        }

        if (message.Contains("redan läst", StringComparison.OrdinalIgnoreCase))
        {
            return Color.DarkOrange;
        }

        if (message.Contains("läser", StringComparison.OrdinalIgnoreCase)
            || message.Contains("sparar", StringComparison.OrdinalIgnoreCase)
            || message.Contains("laddar upp", StringComparison.OrdinalIgnoreCase)
            || message.Contains("försöker", StringComparison.OrdinalIgnoreCase))
        {
            return Color.Gold;
        }

        if (message.Contains("ansluten", StringComparison.OrdinalIgnoreCase)
            || message.Contains("väntar på kontrollenhet", StringComparison.OrdinalIgnoreCase)
            || message.Contains("upptäckt", StringComparison.OrdinalIgnoreCase)
            || message.Contains("klar", StringComparison.OrdinalIgnoreCase))
        {
            return Color.ForestGreen;
        }

        return SystemColors.Control;
    }

    private void OnCurrentStationChanged(string? stationSerial, uint? codeNumber) => RunOnUiThread(() =>
    {
        _currentStationLabel.Text = stationSerial is null
            ? "Aktuell kontroll: -"
            : $"Aktuell kontroll: Code {codeNumber} (serienummer {stationSerial})";
        _forceReadButton.Enabled = stationSerial is not null;
    });

    private void OnAvailableDevicesChanged(IReadOnlyList<string> devices) => RunOnUiThread(() =>
    {
        _updatingDeviceList = true;
        var previouslySelected = _deviceComboBox.SelectedIndex <= 0 ? null : (string)_deviceComboBox.SelectedItem!;
        _deviceComboBox.Items.Clear();
        _deviceComboBox.Items.Add("Auto (första hittade enhet)");
        foreach (var device in devices)
        {
            _deviceComboBox.Items.Add(device);
        }
        var indexToRestore = previouslySelected is not null ? _deviceComboBox.Items.IndexOf(previouslySelected) : -1;
        _deviceComboBox.SelectedIndex = indexToRestore >= 0 ? indexToRestore : 0;
        _updatingDeviceList = false;
    });

    // SDK-events kan komma från en bakgrundstråd - verifiera vid hårdvarutest.
    private void RunOnUiThread(Action action)
    {
        if (IsHandleCreated && InvokeRequired)
        {
            BeginInvoke(action);
        }
        else if (IsHandleCreated)
        {
            action();
        }
    }
}
