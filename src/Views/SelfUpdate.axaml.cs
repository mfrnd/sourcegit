using System;
using System.Diagnostics;
using System.IO;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.TextMate;

namespace SourceGit.Views
{
    public class UpdateInfoView : TextEditor
    {
        protected override Type StyleKeyOverride => typeof(TextEditor);

        public UpdateInfoView() : base(new TextArea(), new TextDocument())
        {
            IsReadOnly = true;
            ShowLineNumbers = false;
            WordWrap = true;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

            TextArea.TextView.Margin = new Thickness(4, 0);
            TextArea.TextView.Options.EnableHyperlinks = false;
            TextArea.TextView.Options.EnableEmailHyperlinks = false;
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            if (_textMate == null)
            {
                _textMate = Models.TextMateHelper.CreateForEditor(this);
                Models.TextMateHelper.SetGrammarByFileName(_textMate, "README.md");
            }
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);

            if (_textMate != null)
            {
                _textMate.Dispose();
                _textMate = null;
            }

            GC.Collect();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is Models.Version ver)
                Text = ver.Body;
        }

        private TextMate.Installation _textMate = null;
    }

    public partial class SelfUpdate : ChromelessWindow
    {
        public SelfUpdate()
        {
            CloseOnESC = true;
            InitializeComponent();
        }

        // Shown only on Windows when SourceGit was installed via Scoop (the running
        // exe lives under <scoop>\apps\sourcegit\ and the scoop shim is present).
        public bool CanUpdateViaScoop => _canUpdateViaScoop ??= DetectScoopInstall(out _scoopExe);

        private void CloseWindow(object _1, RoutedEventArgs _2)
        {
            Close();
        }

        private void OnUpdateViaScoop(object _, RoutedEventArgs e)
        {
            e.Handled = true;
            if (!CanUpdateViaScoop)
                return;

            // A running exe can't update its own files, so hand off to a detached
            // console: wait for us to exit, run scoop, then relaunch on success or
            // hold the window open on error.
            var pid = Environment.ProcessId;
            var relaunch = Path.Combine(Path.GetDirectoryName(_scoopExe) ?? string.Empty, "sourcegit.exe");
            var script = string.Join(' ',
                "$ErrorActionPreference='Continue';",
                $"Wait-Process -Id {pid} -Timeout 60 -ErrorAction SilentlyContinue;",
                "Write-Host 'Updating SourceGit via Scoop...';",
                $"& '{_scoopExe}' update;",
                $"& '{_scoopExe}' update sourcegit;",
                $"if ($LASTEXITCODE -eq 0) {{ Start-Process '{relaunch}' }}",
                "else { Write-Host ''; Write-Host 'Scoop update failed. Press Enter to close.' -ForegroundColor Red; Read-Host }");

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + script + "\"",
                    UseShellExecute = true,
                });
            }
            catch
            {
                Close();
                return;
            }

            // Quit so scoop can replace our files; the helper relaunches us.
            if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
            else
                Environment.Exit(0);
        }

        private static bool DetectScoopInstall(out string scoopExe)
        {
            scoopExe = null;
            if (!OperatingSystem.IsWindows())
                return false;

            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                return false;

            var normalized = exe.Replace('/', '\\');
            var idx = normalized.IndexOf("\\scoop\\apps\\sourcegit\\", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return false;

            var shims = Path.Combine(normalized.Substring(0, idx), "scoop", "shims");
            foreach (var name in new[] { "scoop.cmd", "scoop.ps1", "scoop.exe" })
            {
                var candidate = Path.Combine(shims, name);
                if (File.Exists(candidate))
                {
                    scoopExe = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool? _canUpdateViaScoop;
        private string _scoopExe;

        private void GotoDownload(object _, RoutedEventArgs e)
        {
            Native.OS.OpenBrowser("https://github.com/mfrnd/sourcegit/releases/latest");
            e.Handled = true;
        }

        private void IgnoreThisVersion(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: Models.Version ver })
                ViewModels.Preferences.Instance.IgnoreUpdateTag = ver.TagName;

            Close();
            e.Handled = true;
        }
    }
}
