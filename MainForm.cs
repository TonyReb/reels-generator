using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ReelsGenerator;

public sealed class MainForm : Form
{
    private readonly Button runButton;
    private readonly TextBox logTextBox;
    private readonly TextBox bestTextBox;
    private readonly Label configLabel;
    private readonly Label configSourceLabel;
    private readonly Label bestLabel;
    private readonly ComboBox configComboBox;
    private readonly StringBuilder lineBuffer = new();
    private bool captureConfigSection;
    private bool captureReelsSection;
    private readonly List<string> bestLines = new();
    private string selectedConfigPath;

    public MainForm()
    {
        Text = "Reels Generator";
        Width = 1300;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;

        runButton = new Button
        {
            Text = "Запустити розрахунок",
            Left = 12,
            Top = 12,
            Width = 220,
            Height = 36
        };
        runButton.Click += async (_, _) => await RunCalculationAsync();
        selectedConfigPath = AppRunner.GetDefaultConfigPath();

        configLabel = new Label
        {
            Left = 250,
            Top = 16,
            Width = 1000,
            Height = 24,
            Text = $"Config: {selectedConfigPath}"
        };

        configSourceLabel = new Label
        {
            Left = 250,
            Top = 44,
            Width = 74,
            Height = 20,
            Text = "Slot cfg:"
        };

        configComboBox = new ComboBox
        {
            Left = 326,
            Top = 40,
            Width = 210,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        configComboBox.SelectedIndexChanged += (_, _) => OnConfigSelectionChanged();

        bestLabel = new Label
        {
            Left = 810,
            Top = 20,
            Width = 460,
            Height = 16,
            Text = "Поточна найкраща конфігурація рейок"
        };

        logTextBox = new TextBox
        {
            Left = 12,
            Top = 72,
            Width = 780,
            Height = 638,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            ReadOnly = true,
            WordWrap = false,
            Font = new System.Drawing.Font("Consolas", 10),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
        };

        bestTextBox = new TextBox
        {
            Left = 810,
            Top = 72,
            Width = 460,
            Height = 638,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            ReadOnly = true,
            WordWrap = false,
            Font = new System.Drawing.Font("Consolas", 10),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        Controls.Add(runButton);
        Controls.Add(configLabel);
        Controls.Add(configSourceLabel);
        Controls.Add(configComboBox);
        Controls.Add(bestLabel);
        Controls.Add(logTextBox);
        Controls.Add(bestTextBox);

        try
        {
            LoadSelections();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Config load error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            runButton.Enabled = false;
        }
    }

    private async Task RunCalculationAsync()
    {
        runButton.Enabled = false;
        logTextBox.Clear();
        bestTextBox.Clear();
        lineBuffer.Clear();
        bestLines.Clear();
        captureConfigSection = false;
        captureReelsSection = false;
        AppendLogLine(">>> Start");

        var previousOut = Console.Out;
        using var writer = new UiTextWriter(AppendLog);
        Console.SetOut(writer);

        try
        {
            await Task.Run(() => AppRunner.Run(new AppRunOptions
            {
                ConfigPath = selectedConfigPath
            }));
            AppendLogLine(">>> Done");
        }
        catch (Exception ex)
        {
            AppendLogLine($">>> ERROR: {ex}");
        }
        finally
        {
            Console.SetOut(previousOut);
            runButton.Enabled = true;
        }
    }

    private void LoadSelections()
    {
        configComboBox.Items.Clear();
        var configOptions = ConfigLoader.GetAvailableConfigFiles();
        foreach (var configOption in configOptions)
        {
            configComboBox.Items.Add(configOption);
        }

        if (configComboBox.Items.Count == 0)
        {
            throw new InvalidOperationException("No config files found. Expected configs/**/config.yml.");
        }

        ConfigFileOption? selectedOption = null;
        foreach (var item in configComboBox.Items)
        {
            if (item is ConfigFileOption option && string.Equals(option.Path, selectedConfigPath, StringComparison.OrdinalIgnoreCase))
            {
                selectedOption = option;
                break;
            }
        }

        if (selectedOption != null)
        {
            configComboBox.SelectedItem = selectedOption;
        }
        else
        {
            configComboBox.SelectedIndex = 0;
        }

        EnsureConfigCanBeLoaded(selectedConfigPath);
    }

    private void OnConfigSelectionChanged()
    {
        if (configComboBox.SelectedItem is not ConfigFileOption selectedOption)
        {
            return;
        }

        try
        {
            selectedConfigPath = selectedOption.Path;
            configLabel.Text = $"Config: {selectedConfigPath}";
            EnsureConfigCanBeLoaded(selectedConfigPath);
            runButton.Enabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Config load error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            runButton.Enabled = false;
        }
    }

    private static void EnsureConfigCanBeLoaded(string configPath)
    {
        _ = ConfigLoader.ResolveConfig(configPath, profileName: null);
    }

    private void AppendLog(string text)
    {
        if (logTextBox.IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendLog), text);
            return;
        }

        ProcessIncomingText(text);
    }

    private void AppendLogLine(string text)
    {
        AppendLog(text + Environment.NewLine);
    }

    private void ProcessIncomingText(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                string line = lineBuffer.ToString().TrimEnd('\r');
                lineBuffer.Clear();
                bool hideInMainLog = ProcessLogLine(line);
                if (!hideInMainLog)
                {
                    logTextBox.AppendText(line + Environment.NewLine);
                }
            }
            else
            {
                lineBuffer.Append(c);
            }
        }
    }

    private bool ProcessLogLine(string line)
    {
        if (line.Equals("Current Best Individual Items:", StringComparison.Ordinal) ||
            line.Equals("Best Individual Items:", StringComparison.Ordinal))
        {
            captureConfigSection = true;
            captureReelsSection = false;
            bestLines.Clear();
            return true;
        }

        if (line.StartsWith("Best Individual Reels:", StringComparison.Ordinal))
        {
            captureConfigSection = true;
            captureReelsSection = true;
            if (bestLines.Count > 0 && !string.IsNullOrEmpty(bestLines[^1]))
            {
                bestLines.Add(string.Empty);
                bestTextBox.Text = string.Join(Environment.NewLine, bestLines);
            }
            return true;
        }

        if (captureConfigSection)
        {
            if (line.StartsWith("Winning combinations by symbol and length", StringComparison.Ordinal) ||
                line.StartsWith("No winning combinations found.", StringComparison.Ordinal) ||
                line.StartsWith("Symbol ", StringComparison.Ordinal))
            {
                captureConfigSection = false;
                captureReelsSection = false;
                return false;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                return true;
            }

            if (IsReelItemLine(line) || (captureReelsSection && line.StartsWith("Reel ", StringComparison.Ordinal)))
            {
                bestLines.Add(line);
                bestTextBox.Text = string.Join(Environment.NewLine, bestLines);
                return true;
            }

            captureConfigSection = false;
            captureReelsSection = false;
        }

        return false;
    }

    private static bool IsReelItemLine(string line)
    {
        if (line.StartsWith("Reel ", StringComparison.Ordinal) && line.Contains("Symbol", StringComparison.Ordinal))
        {
            return true;
        }

        int firstComma = line.IndexOf(',');
        if (firstComma <= 0)
        {
            return false;
        }

        int secondComma = line.IndexOf(',', firstComma + 1);
        if (secondComma <= firstComma + 1)
        {
            return false;
        }

        string reelPart = line.Substring(0, firstComma).Trim();
        string symbolPart = line.Substring(firstComma + 1, secondComma - firstComma - 1).Trim();
        return int.TryParse(reelPart, out _) && int.TryParse(symbolPart, out _);
    }
}

internal sealed class UiTextWriter : StringWriter
{
    private readonly Action<string> onText;

    public UiTextWriter(Action<string> onText)
    {
        this.onText = onText;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        onText(value.ToString());
    }

    public override void Write(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            onText(value);
        }
    }

    public override void WriteLine(string? value)
    {
        onText((value ?? string.Empty) + Environment.NewLine);
    }
}
