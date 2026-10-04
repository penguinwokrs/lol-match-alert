// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.Devices.Setup;
using MatchAlert.Tray.Resources;

namespace MatchAlert.Tray;

/// <summary>
/// The setup wizard's questions as Windows task dialogs. The flow runs off the UI thread (it waits on
/// the keyboard between questions); each question is marshalled to the UI thread and waited for.
/// </summary>
internal sealed class TaskDialogPrompt(Control ui, string caption) : IUserPrompt
{
    public int? Choose(string message, IReadOnlyList<string> choices) => (int?)ui.Invoke(() =>
    {
        var buttons = choices.Select(c => new TaskDialogButton(c)).ToList();
        var page = new TaskDialogPage
        {
            Caption = caption,
            Text = message,
            AllowCancel = true,
            SizeToContent = true,
        };
        foreach (var b in buttons) page.Buttons.Add(b);
        var result = TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen);
        int index = buttons.IndexOf(result);
        return index < 0 ? null : (int?)index;
    });

    public void Inform(string message, string? details = null) => ui.Invoke(() =>
    {
        var page = new TaskDialogPage
        {
            Caption = caption,
            Text = message,
            Icon = TaskDialogIcon.Information,
            SizeToContent = true,
            Buttons = { TaskDialogButton.OK },
        };
        if (details is not null)
        {
            page.Expander = new TaskDialogExpander { Text = details, CollapsedButtonText = Strings.Prompt_DeviceDetails, Expanded = true };
            var copy = new TaskDialogButton(Strings.Prompt_CopyDetails) { AllowCloseDialog = false };
            copy.Click += (_, _) => Clipboard.SetText(details);
            page.Buttons.Insert(0, copy);
        }
        TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen);
    });
}
