// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

// UseWPF drops System.IO from the implicit usings (it would clash with System.Windows.Shapes.Path);
// nothing here uses Shapes, and the tray code is full of files and folders.
global using System.IO;
