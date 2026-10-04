// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MatchAlert.App;
using MatchAlert.Domain;
using MatchAlert.Tray.Resources;
using WpfColor = System.Windows.Media.Color;

namespace MatchAlert.Tray.Editor;

/// <summary>
/// The pattern editor: a timeline of color blocks, the selected step's color, brightness and effect, which
/// keyboard plays which pattern, and a switch that plays the pattern being edited on a real keyboard. All the
/// rules about patterns live in App (PatternDraft, PatternCheck, SettingsWriter, LivePreview); this is the glue.
/// </summary>
internal sealed partial class PatternEditorWindow : Window
{
    private const string DefaultName = SettingsLoader.DefaultPattern;

    private readonly AlertService _alerts;
    private readonly IDeviceSource _devices;
    private readonly SettingsService _settings;
    private readonly FileLog _log;

    private readonly Dictionary<string, PatternDraft> _drafts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _userPatterns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (bool? Enabled, string? Pattern)> _choices = new(StringComparer.Ordinal);
    private readonly List<(ILightingDevice Device, DeviceProfile Profile)> _shown = [];
    private IReadOnlyDictionary<string, Pattern> _builtIn = new Dictionary<string, Pattern>();
    private string? _default;
    private PatternDraft _current = null!;
    private int _step;
    private bool _loading;
    private bool _dirty;
    private bool _closingForReal;
    private Rgb _lastLit = Rgb.Parse("#FF0000");

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _animation;
    private readonly DispatcherTimer _pushToKeyboard;
    private LivePreview? _live;

    public PatternEditorWindow(AlertService alerts, IDeviceSource devices, SettingsService settings, FileLog log)
    {
        _alerts = alerts;
        _devices = devices;
        _settings = settings;
        _log = log;
        InitializeComponent();
        Localize();
        SetIcon();

        _animation = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Animate(), Dispatcher);
        // Slider drags change a value many times a second; the keyboard gets the last of each burst.
        _pushToKeyboard = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) => PushToKeyboard(), Dispatcher) { IsEnabled = false };

        Load();
        Wire();
        _animation.Start();
    }

    // ---- loading -----------------------------------------------------------------------------------

    private void Load()
    {
        var s = _settings.Current;
        _builtIn = s.BuiltInPatterns;
        foreach (var (name, pattern) in s.Patterns) _drafts[name] = PatternDraft.From(name, pattern);
        _userPatterns.UnionWith(s.UserPatterns);
        _default = s.UserDefaultPattern;
        foreach (var (id, choice) in s.UserDeviceChoices) _choices[id] = (choice.Enabled, choice.Pattern);

        IReadOnlyList<ILightingDevice> found;
        try { found = _devices.Discover(); }
        catch (Exception e)
        {
            _log.Write($"Editor: could not list devices: {e.Message}");
            found = [];
        }
        foreach (var device in found)
            if (s.Profiles.FirstOrDefault(p => p.Id == device.Id) is { } profile) _shown.Add((device, profile));

        FillPatternBoxes(select: s.UserDefaultPattern ?? DefaultName);
        FillKeyboards();
        foreach (var (device, _) in _shown) LiveDeviceBox.Items.Add(new ComboBoxItem { Content = device.Name, Tag = device });
        if (LiveDeviceBox.Items.Count > 0) LiveDeviceBox.SelectedIndex = 0;
        LiveBox.IsEnabled = _shown.Count > 0;
    }

    private IEnumerable<string> Names() =>
        _builtIn.Keys.Concat(_drafts.Keys.Where(n => !_builtIn.ContainsKey(n)).Order(StringComparer.CurrentCulture));

    private string Label(string name) =>
        !_builtIn.ContainsKey(name) ? name
        : _userPatterns.Contains(name) ? string.Format(Strings.Editor_Modified, name)
        : string.Format(Strings.Editor_BuiltIn, name);

    private void FillPatternBoxes(string select)
    {
        _loading = true;
        PatternBox.Items.Clear();
        foreach (var name in Names()) PatternBox.Items.Add(new ComboBoxItem { Content = Label(name), Tag = name });
        PatternBox.SelectedItem = PatternBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == select) ?? PatternBox.Items[0];

        DefaultBox.Items.Clear();
        foreach (var name in Names()) DefaultBox.Items.Add(new ComboBoxItem { Content = Label(name), Tag = name });
        DefaultBox.SelectedItem = DefaultBox.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == (_default ?? DefaultName));
        _loading = false;
        Select((string)((ComboBoxItem)PatternBox.SelectedItem).Tag);
    }

    private void FillKeyboards()
    {
        KeyboardsPanel.Children.Clear();
        if (_shown.Count == 0)
        {
            KeyboardsPanel.Children.Add(new TextBlock { Text = Strings.Editor_NoDevices, Foreground = (Brush)FindResource("Quiet") });
            return;
        }
        foreach (var (device, profile) in _shown)
        {
            var choice = _choices.GetValueOrDefault(profile.Id);
            var on = new CheckBox
            {
                Content = profile.Verified ? device.Name : $"{device.Name}  ·  {Strings.Editor_Unverified}",
                IsChecked = choice.Enabled ?? profile.EnabledByDefault,
                Margin = new Thickness(0, 6, 0, 4),
            };
            var pattern = new ComboBox { Margin = new Thickness(22, 0, 0, 4) };
            pattern.Items.Add(new ComboBoxItem { Content = string.Format(Strings.Editor_UseDefault, _default ?? DefaultName), Tag = null });
            foreach (var name in Names()) pattern.Items.Add(new ComboBoxItem { Content = Label(name), Tag = name });
            pattern.SelectedItem = pattern.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == choice.Pattern) ?? pattern.Items[0];

            var id = profile.Id;
            RoutedEventHandler toggled = (_, _) =>
            {
                bool value = on.IsChecked == true;
                _choices[id] = (value == profile.EnabledByDefault ? null : value, _choices.GetValueOrDefault(id).Pattern);
                Changed(patternEdited: false);
            };
            on.Checked += toggled;
            on.Unchecked += toggled;
            pattern.SelectionChanged += (_, _) =>
            {
                if (_loading || pattern.SelectedItem is not ComboBoxItem item) return;
                _choices[id] = (_choices.GetValueOrDefault(id).Enabled, (string?)item.Tag);
                Changed(patternEdited: false);
            };
            KeyboardsPanel.Children.Add(on);
            KeyboardsPanel.Children.Add(pattern);
        }
    }

    // ---- showing a pattern and a step --------------------------------------------------------------

    private void Select(string name)
    {
        _current = _drafts[name];
        Timeline.Draft = _current;
        _step = 0;
        bool builtIn = _builtIn.ContainsKey(name);
        RenameButton.IsEnabled = !builtIn;
        DeleteButton.Content = builtIn ? Strings.Editor_Reset : Strings.Editor_Delete;
        DeleteButton.IsEnabled = !builtIn || _userPatterns.Contains(name);

        _loading = true;
        UntilEndRadio.IsChecked = _current.RepeatCount is null;
        CountRadio.IsChecked = _current.RepeatCount is not null;
        CountBox.Text = (_current.RepeatCount ?? 3).ToString(CultureInfo.InvariantCulture);
        CountBox.IsEnabled = _current.RepeatCount is not null;
        _loading = false;

        ShowStep();
        Restart();
        UpdateNotes();
    }

    private void ShowStep()
    {
        _step = Math.Clamp(_step, 0, _current.Steps.Count - 1);
        Timeline.Selected = _step;
        var step = _current.Steps[_step];
        bool off = step.Color == new Rgb(0, 0, 0);
        if (!off) _lastLit = step.Color;

        _loading = true;
        Wheel.Color = step.Color;
        Wheel.IsEnabled = !off;
        Wheel.Opacity = off ? 0.35 : 1;
        Swatch.Background = new SolidColorBrush(WpfColor.FromRgb(step.Color.R, step.Color.G, step.Color.B));
        HexBox.Text = step.Color.ToString();
        OffBox.IsChecked = off;
        BrightnessSlider.Value = step.Brightness;
        BrightnessValue.Text = $"{step.Brightness}%";
        SteadyRadio.IsChecked = step.Effect != "breathing";
        BreathingRadio.IsChecked = step.Effect == "breathing";
        SpeedSlider.Value = step.Speed ?? 128;
        SpeedSlider.IsEnabled = step.Effect == "breathing";
        SpeedValue.Text = step.Effect == "breathing" ? $"{step.Speed ?? 128}" : "";
        bool many = _current.Steps.Count > 1;
        DurationBox.Text = many ? step.DurationMs.ToString(CultureInfo.InvariantCulture) : "";
        DurationBox.IsEnabled = many;
        EarlierButton.IsEnabled = _step > 0;
        LaterButton.IsEnabled = _step < _current.Steps.Count - 1;
        RemoveStepButton.IsEnabled = _current.CanRemove;
        _loading = false;
    }

    // ---- editing -----------------------------------------------------------------------------------

    private StepDraft Step => _current.Steps[_step];

    /// <summary>
    /// After every edit. Editing a built-in pattern makes it the user's override; the on-screen preview starts
    /// over; the keyboard gets the new pattern shortly after.
    /// </summary>
    private void Changed(bool patternEdited = true)
    {
        _dirty = true;
        StatusText.Text = "";
        if (patternEdited && _builtIn.ContainsKey(_current.Name) && _userPatterns.Add(_current.Name))
        {
            RelabelPattern(_current.Name);
            DeleteButton.IsEnabled = true;
        }
        Timeline.InvalidateVisual();
        Restart();
        UpdateNotes();
        if (_live is not null)
        {
            _pushToKeyboard.Stop();
            _pushToKeyboard.Start();
        }
    }

    private void RelabelPattern(string name)
    {
        foreach (var box in new[] { PatternBox, DefaultBox }.Concat(KeyboardsPanel.Children.OfType<ComboBox>()))
            foreach (var item in box.Items.OfType<ComboBoxItem>().Where(i => (string?)i.Tag == name))
                item.Content = Label(name);
    }

    private void Wire()
    {
        PatternBox.SelectionChanged += (_, _) =>
        {
            if (_loading || PatternBox.SelectedItem is not ComboBoxItem item) return;
            Select((string)item.Tag);
            UpdateNotes();
            if (_live is not null) PushToKeyboard();
        };
        DefaultBox.SelectionChanged += (_, _) =>
        {
            if (_loading || DefaultBox.SelectedItem is not ComboBoxItem item) return;
            _default = (string)item.Tag;
            foreach (var box in KeyboardsPanel.Children.OfType<ComboBox>())
                ((ComboBoxItem)box.Items[0]).Content = string.Format(Strings.Editor_UseDefault, _default);
            Changed(patternEdited: false);
        };

        Timeline.SelectionChanged += i => { _step = i; ShowStep(); };
        Timeline.DraftChanged += () => { ShowStep(); Changed(); };

        Wheel.ColorPicked += rgb =>
        {
            Step.Color = rgb;
            _lastLit = rgb;
            ShowStep();
            Changed();
        };
        HexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyHex(); };
        HexBox.LostFocus += (_, _) => ApplyHex();
        RoutedEventHandler off = (_, _) =>
        {
            if (_loading) return;
            Step.Color = OffBox.IsChecked == true ? new Rgb(0, 0, 0) : _lastLit;
            ShowStep();
            Changed();
        };
        OffBox.Checked += off;
        OffBox.Unchecked += off;
        BrightnessSlider.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            Step.Brightness = (int)Math.Round(BrightnessSlider.Value);
            BrightnessValue.Text = $"{Step.Brightness}%";
            Changed();
        };
        SteadyRadio.Checked += (_, _) => SetEffect(Domain.Step.Solid);
        BreathingRadio.Checked += (_, _) => SetEffect("breathing");
        SpeedSlider.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            Step.Speed = (byte)Math.Round(SpeedSlider.Value);
            SpeedValue.Text = $"{Step.Speed}";
            Changed();
        };
        DurationBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyDuration(); };
        DurationBox.LostFocus += (_, _) => ApplyDuration();
        EarlierButton.Click += (_, _) => MoveStep(-1);
        LaterButton.Click += (_, _) => MoveStep(+1);
        RemoveStepButton.Click += (_, _) =>
        {
            _current.Remove(_step);
            ShowStep();
            Changed();
        };

        UntilEndRadio.Checked += (_, _) => SetRepeat(null);
        CountRadio.Checked += (_, _) => SetRepeat(ParseCount());
        CountBox.TextChanged += (_, _) => { if (!_loading && CountRadio.IsChecked == true) SetRepeat(ParseCount()); };

        NewButton.Click += (_, _) => AddPattern(Ask(Unused("pattern")), null);
        DuplicateButton.Click += (_, _) => AddPattern(Ask(Unused(_current.Name + "-copy")), _current);
        RenameButton.Click += (_, _) => RenamePattern();
        DeleteButton.Click += (_, _) => DeleteOrReset();

        // Checked/Unchecked rather than Click: UI Automation (screen readers, scripts) toggles a check box
        // without clicking it. Unticking from code - a failed start, a match taking over - lands in
        // StopLiveAsync with nothing to stop.
        LiveBox.Checked += async (_, _) => await StartLiveAsync();
        LiveBox.Unchecked += async (_, _) => await StopLiveAsync();
        LiveDeviceBox.SelectionChanged += async (_, _) =>
        {
            if (_live is null) return;
            await StopLiveAsync();
            await StartLiveAsync();
        };

        SaveButton.Click += (_, _) => { if (Save()) Close(); };
        // Cancel throws the edits away; only the window's own close button asks about them.
        CancelButton.Click += (_, _) => { _dirty = false; Close(); };
    }

    private void SetEffect(string effect)
    {
        if (_loading) return;
        Step.Effect = effect;
        if (effect == "breathing") Step.Speed ??= 128;
        ShowStep();
        Changed();
    }

    private void ApplyHex()
    {
        if (_loading) return;
        try
        {
            var rgb = Rgb.Parse(HexBox.Text);
            if (rgb == Step.Color) return;
            Step.Color = rgb;
            ShowStep();
            Changed();
        }
        catch (FormatException)
        {
            HexBox.Text = Step.Color.ToString();
        }
    }

    private void ApplyDuration()
    {
        if (_loading || _current.Steps.Count < 2) return;
        if (int.TryParse(DurationBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms))
        {
            _current.SetDuration(_step, ms);
            Changed();
        }
        ShowStep();
    }

    private void MoveStep(int by)
    {
        _current.Move(_step, _step + by);
        _step += by;
        ShowStep();
        Changed();
    }

    private int? ParseCount() =>
        int.TryParse(CountBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 1 ? Math.Min(n, 99) : 1;

    private void SetRepeat(int? count)
    {
        if (_loading) return;
        _current.RepeatCount = count;
        CountBox.IsEnabled = count is not null;
        Changed();
    }

    // ---- patterns ----------------------------------------------------------------------------------

    private string Unused(string stem)
    {
        if (!_drafts.ContainsKey(stem)) return stem;
        for (int i = 2; ; i++)
            if (!_drafts.ContainsKey($"{stem}-{i}")) return $"{stem}-{i}";
    }

    private string? Ask(string suggestion)
    {
        while (true)
        {
            var name = NameDialog.Ask(this, Strings.Editor_NamePrompt, suggestion);
            if (name is null) return null;
            if (!_drafts.ContainsKey(name)) return name;
            MessageBox.Show(this, Strings.Editor_NameTaken, Title, MessageBoxButton.OK, MessageBoxImage.Information);
            suggestion = name;
        }
    }

    private void AddPattern(string? name, PatternDraft? from)
    {
        if (name is null) return;
        var draft = from?.Copy(name) ?? new PatternDraft { Name = name };
        if (draft.Steps.Count == 0) draft.Add(after: 0);
        _drafts[name] = draft;
        _userPatterns.Add(name);
        _dirty = true;
        FillPatternBoxes(select: name);
        FillKeyboards();
        UpdateNotes();
    }

    private void RenamePattern()
    {
        string old = _current.Name;
        var name = Ask(old);
        if (name is null || name == old) return;
        _drafts.Remove(old);
        _current.Name = name;
        _drafts[name] = _current;
        _userPatterns.Remove(old);
        _userPatterns.Add(name);
        if (_default == old) _default = name;
        foreach (var (id, choice) in _choices.ToList())
            if (choice.Pattern == old) _choices[id] = (choice.Enabled, name);
        _dirty = true;
        FillPatternBoxes(select: name);
        FillKeyboards();
    }

    /// <summary>A user pattern is deleted (and unassigned wherever it was used); a built-in goes back to how it ships.</summary>
    private void DeleteOrReset()
    {
        string name = _current.Name;
        if (_builtIn.TryGetValue(name, out var shipped))
        {
            _drafts[name] = PatternDraft.From(name, shipped);
            _userPatterns.Remove(name);
            _dirty = true;
            FillPatternBoxes(select: name);
            FillKeyboards();
            return;
        }
        _drafts.Remove(name);
        _userPatterns.Remove(name);
        if (_default == name) _default = null;
        foreach (var (id, choice) in _choices.ToList())
            if (choice.Pattern == name) _choices[id] = (choice.Enabled, null);
        _dirty = true;
        FillPatternBoxes(select: _default ?? DefaultName);
        FillKeyboards();
    }

    // ---- notes -------------------------------------------------------------------------------------

    private void UpdateNotes()
    {
        var pattern = _current.ToPattern();
        var lines = new List<string>();
        var live = (LiveDeviceBox.SelectedItem as ComboBoxItem)?.Tag as ILightingDevice;
        foreach (var (device, profile) in _shown)
        {
            var choice = _choices.GetValueOrDefault(profile.Id);
            bool plays = (choice.Pattern ?? _default ?? DefaultName) == _current.Name;
            if (!plays && !(_live is not null && device == live)) continue;
            foreach (var note in PatternCheck.For(pattern, profile))
                lines.Add(note.Kind switch
                {
                    PatternNoteKind.StretchedStep => string.Format(Strings.Note_Stretched, device.Name, note.StepIndex + 1, profile.MinStepMs),
                    PatternNoteKind.MissingEffect => string.Format(Strings.Note_MissingEffect, device.Name, EffectName(pattern.Steps[note.StepIndex].Effect)),
                    _ => string.Format(Strings.Note_BreathingSteady, device.Name),
                });
            if (!profile.Verified) lines.Add(string.Format(Strings.Note_Unverified, device.Name));
        }
        NotesText.Text = string.Join("\n", lines.Distinct());
    }

    private static string EffectName(string effect) => effect switch
    {
        "solid" => Strings.Editor_Steady,
        "breathing" => Strings.Editor_Breathing,
        _ => effect,
    };

    // ---- previews ----------------------------------------------------------------------------------

    private void Restart() => _clock.Restart();

    /// <summary>The on-screen strip: the step showing now, breathing drawn as a slow fade.</summary>
    private void Animate()
    {
        if (_current is null) return;
        int minStep = _live?.Device.MinStepMs ?? 0;
        var (index, phase) = _current.StepAt(_clock.Elapsed, minStep);
        var step = _current.Steps[index];
        double breath = step.Effect == "breathing" ? 0.12 + 0.88 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * phase)) : 1;
        PreviewStrip.Background = new SolidColorBrush(TimelineControl.Shown(step, breath));
        Timeline.Playing = index;
    }

    private async Task StartLiveAsync()
    {
        if (LiveDeviceBox.SelectedItem is not ComboBoxItem { Tag: ILightingDevice device }) return;
        try
        {
            _live = await _alerts.StartPreviewAsync(device, _current.ToPattern());
        }
        catch (Exception e)
        {
            _log.Write($"Editor: preview on {device.Name} failed: {e.Message}");
            StatusText.Text = string.Format(Strings.Editor_PreviewFailed, device.Name, e.Message);
            LiveBox.IsChecked = false;
            return;
        }
        if (_live is null)
        {
            StatusText.Text = Strings.Editor_PreviewBusy;
            LiveBox.IsChecked = false;
            return;
        }
        var started = _live;
        started.Ended += () => Dispatcher.BeginInvoke(() =>
        {
            if (_live != started) return;
            _live = null;
            LiveBox.IsChecked = false;
            StatusText.Text = Strings.Editor_PreviewEnded;
        });
        StatusText.Text = "";
        Restart();
        UpdateNotes();
    }

    private async Task StopLiveAsync()
    {
        _pushToKeyboard.Stop();
        if (_live is not { } live) return;
        _live = null;
        await live.StopAsync();
        UpdateNotes();
    }

    private void PushToKeyboard()
    {
        _pushToKeyboard.Stop();
        _live?.Update(_current.ToPattern());
        Restart();
    }

    // ---- saving and closing ------------------------------------------------------------------------

    private bool Save()
    {
        var edit = new SettingsEdit(
            _userPatterns.Where(_drafts.ContainsKey).ToDictionary(n => n, n => _drafts[n].ToPattern()),
            _default,
            _shown.ToDictionary(s => s.Profile.Id, s => new DeviceChoice(_choices.GetValueOrDefault(s.Profile.Id).Pattern, _choices.GetValueOrDefault(s.Profile.Id).Enabled)));
        try
        {
            SettingsWriter.Save(AppPaths.SettingsDirectory, edit);
        }
        catch (Exception e) when (e is SettingsException or IOException or UnauthorizedAccessException)
        {
            StatusText.Text = string.Format(Strings.Editor_SaveFailed, e.Message);
            return false;
        }
        _dirty = false;
        _log.Write("Editor: saved settings.json");
        return true;
    }

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_closingForReal) return;
        if (_dirty)
        {
            switch (MessageBox.Show(this, Strings.Editor_Unsaved, Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question))
            {
                case MessageBoxResult.Cancel:
                    e.Cancel = true;
                    return;
                case MessageBoxResult.Yes when !Save():
                    e.Cancel = true;
                    return;
            }
        }
        if (_live is not null)
        {
            // Put the lighting back before the window goes away.
            e.Cancel = true;
            await StopLiveAsync();
            _closingForReal = true;
            Close();
            return;
        }
        _closingForReal = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _animation.Stop();
        _pushToKeyboard.Stop();
        base.OnClosed(e);
    }

    /// <summary>Selects a pattern by name, as choosing it in the list would.</summary>
    public void ShowPattern(string name)
    {
        if (PatternBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == name) is { } item) PatternBox.SelectedItem = item;
    }

    /// <summary>Lays the window's content out at its size and draws it to a PNG, without showing anything.</summary>
    public void RenderTo(string path)
    {
        if (Content is not FrameworkElement root) return;
        var size = new System.Windows.Size(Width, Height - 32);   // less the title bar the content never gets
        var frame = new System.Windows.Controls.Border { Background = Background, Padding = new Thickness(0) };
        Content = null;
        frame.Child = root;
        frame.Resources = Resources;   // the styles live on the window
        frame.Measure(size);
        frame.Arrange(new Rect(size));
        frame.UpdateLayout();
        Animate();
        frame.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(frame);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        png.Save(file);
    }

    // ---- text and icon -----------------------------------------------------------------------------

    private void Localize()
    {
        Title = Strings.Editor_Title;
        PatternLabel.Text = Strings.Editor_Pattern;
        NewButton.Content = Strings.Editor_New;
        DuplicateButton.Content = Strings.Editor_Duplicate;
        RenameButton.Content = Strings.Editor_Rename;
        PreviewLabel.Text = Strings.Editor_Preview;
        TimelineHint.Text = Strings.Editor_Timeline;
        Timeline.HoldText = Strings.Editor_Hold;
        StepHeading.Text = Strings.Editor_Step;
        ColorLabel.Text = Strings.Editor_Color;
        OffBox.Content = Strings.Editor_Off;
        BrightnessLabel.Text = Strings.Editor_Brightness;
        EffectLabel.Text = Strings.Editor_Effect;
        SteadyRadio.Content = Strings.Editor_Steady;
        BreathingRadio.Content = Strings.Editor_Breathing;
        SpeedLabel.Text = Strings.Editor_Speed;
        DurationLabel.Text = Strings.Editor_Duration;
        EarlierButton.ToolTip = Strings.Editor_MoveEarlier;
        LaterButton.ToolTip = Strings.Editor_MoveLater;
        RemoveStepButton.Content = Strings.Editor_RemoveStep;
        RepeatHeading.Text = Strings.Editor_Repeat;
        UntilEndRadio.Content = Strings.Editor_UntilEnd;
        TimesLabel.Text = Strings.Editor_Times;
        KeyboardsHeading.Text = Strings.Editor_Keyboards;
        DefaultLabel.Text = Strings.Editor_DefaultPattern;
        LiveBox.Content = Strings.Editor_PreviewOnKeyboard;
        SaveButton.Content = Strings.Editor_Save;
        CancelButton.Content = Strings.Editor_Cancel;
    }

    private void SetIcon()
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
            if (icon is not null)
                Icon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch (Exception e) when (e is IOException or ArgumentException) { }
    }
}
