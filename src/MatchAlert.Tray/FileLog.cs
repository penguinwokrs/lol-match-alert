// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace MatchAlert.Tray;

/// <summary>A plain text log, rotated to one previous generation at 1 MB. Never throws: a log is not worth a crash.</summary>
internal sealed class FileLog(string path, TextWriter? echo = null)
{
    private const long MaxBytes = 1024 * 1024;
    private readonly object _lock = new();

    public void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
        lock (_lock)
        {
            echo?.WriteLine(line);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                    File.Move(path, Path.ChangeExtension(path, ".1.txt"), overwrite: true);
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
