param(
    [Parameter(Mandatory = $true)][string]$PayloadZip,
    [Parameter(Mandatory = $true)][string]$OutputExe
)

$ErrorActionPreference = "Stop"

if (Test-Path $OutputExe) {
    Remove-Item $OutputExe -Force
}

$cscExe = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$tempCs = Join-Path $env:TEMP "AetheriaSetupStub.cs"

$csSource = @'
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("Aetheria AI Roleplay Platform Setup")]
[assembly: AssemblyProduct("Aetheria AI Roleplay Platform")]
[assembly: AssemblyCompany("Aetheria Studio")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace AetheriaSetup
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm(args != null && args.Length > 0 && args[0] == "--silent"));
        }
    }

    public class SetupForm : Form
    {
        private Label _statusLabel;
        private ProgressBar _progressBar;
        private Button _installBtn;
        private CheckBox _desktopShortcutCheck;
        private CheckBox _launchAfterCheck;
        private TextBox _pathBox;
        private readonly bool _silent;

        public SetupForm(bool silent)
        {
            _silent = silent;
            Text = "Aetheria — Windows 11 AI Roleplay Platform Setup";
            Size = new Size(560, 360);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            BackColor = Color.FromArgb(24, 24, 34);
            ForeColor = Color.FromArgb(242, 242, 250);
            Font = new Font("Segoe UI", 10f);

            var title = new Label
            {
                Text = "Aetheria AI Roleplay Platform",
                Font = new Font("Segoe UI Semibold", 17f, FontStyle.Bold),
                ForeColor = Color.FromArgb(245, 245, 255),
                Location = new Point(28, 22),
                AutoSize = true
            };

            var subtitle = new Label
            {
                Text = "Includes Chainsaw Man & Death Note Worlds, Couple Roleplay, Co-Op PvE & Multiplayer Sync",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(180, 182, 205),
                Location = new Point(30, 56),
                Size = new Size(500, 36)
            };

            var pathLabel = new Label
            {
                Text = "Installation Folder:",
                Location = new Point(30, 100),
                AutoSize = true
            };

            string defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Aetheria");
            _pathBox = new TextBox
            {
                Text = defaultDir,
                Location = new Point(30, 124),
                Size = new Size(490, 28),
                BackColor = Color.FromArgb(36, 36, 50),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            _desktopShortcutCheck = new CheckBox
            {
                Text = "Create Desktop & Start Menu Shortcuts",
                Checked = true,
                Location = new Point(30, 164),
                AutoSize = true
            };

            _launchAfterCheck = new CheckBox
            {
                Text = "Launch Aetheria immediately after installation",
                Checked = true,
                Location = new Point(30, 192),
                AutoSize = true
            };

            _progressBar = new ProgressBar
            {
                Location = new Point(30, 230),
                Size = new Size(490, 20),
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            _statusLabel = new Label
            {
                Text = "Ready to install.",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(180, 220, 200),
                Location = new Point(30, 260),
                AutoSize = true
            };

            _installBtn = new Button
            {
                Text = "Install & Play Now",
                Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(98, 72, 212),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(350, 268),
                Size = new Size(170, 38)
            };
            _installBtn.FlatAppearance.BorderSize = 0;
            _installBtn.Click += async (s, e) => await RunInstallAsync();

            Controls.Add(title);
            Controls.Add(subtitle);
            Controls.Add(pathLabel);
            Controls.Add(_pathBox);
            Controls.Add(_desktopShortcutCheck);
            Controls.Add(_launchAfterCheck);
            Controls.Add(_progressBar);
            Controls.Add(_statusLabel);
            Controls.Add(_installBtn);

            if (_silent)
            {
                Shown += async (s, e) => await RunInstallAsync();
            }
        }

        private async Task RunInstallAsync()
        {
            _installBtn.Enabled = false;
            string targetDir = _pathBox.Text.Trim();
            bool makeShortcuts = _desktopShortcutCheck.Checked;
            bool launchAfter = _launchAfterCheck.Checked && !_silent;

            try
            {
                _statusLabel.Text = "Stopping any running Aetheria instance...";
                _progressBar.Value = 15;
                await Task.Run(() =>
                {
                    foreach (var p in Process.GetProcessesByName("Aetheria"))
                    {
                        try { p.Kill(); p.WaitForExit(2000); } catch { }
                    }
                });

                _statusLabel.Text = "Extracting Aetheria Windows 11 files & scenarios...";
                _progressBar.Value = 40;

                await Task.Run(() =>
                {
                    if (Directory.Exists(targetDir))
                    {
                        try { Directory.Delete(targetDir, true); } catch { }
                    }
                    Directory.CreateDirectory(targetDir);

                    string tempZip = Path.Combine(Path.GetTempPath(), "AetheriaPayload_" + Guid.NewGuid().ToString("N") + ".zip");
                    using (Stream resStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload.zip"))
                    using (FileStream fs = File.Create(tempZip))
                    {
                        resStream.CopyTo(fs);
                    }

                    ZipFile.ExtractToDirectory(tempZip, targetDir);
                    try { File.Delete(tempZip); } catch { }
                });

                _progressBar.Value = 80;
                string exePath = Path.Combine(targetDir, "Aetheria.exe");

                if (makeShortcuts)
                {
                    _statusLabel.Text = "Creating Desktop & Start Menu shortcuts...";
                    CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Aetheria AI Roleplay.lnk"), exePath, targetDir);
                    CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Aetheria AI Roleplay.lnk"), exePath, targetDir);
                }

                _progressBar.Value = 100;
                _statusLabel.Text = "Installation complete!";

                if (launchAfter && File.Exists(exePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        WorkingDirectory = targetDir,
                        UseShellExecute = true
                    });
                }

                if (!_silent)
                {
                    MessageBox.Show("Aetheria AI Roleplay Platform has been installed!\n\nDesktop shortcut: 'Aetheria AI Roleplay'\nInstalled to: " + targetDir, "Installation Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                Close();
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "Error: " + ex.Message;
                _installBtn.Enabled = true;
                if (!_silent)
                {
                    MessageBox.Show("Installation failed: " + ex.Message, "Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static void CreateShortcut(string shortcutPath, string targetExe, string workDir)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetExe;
                shortcut.WorkingDirectory = workDir;
                shortcut.Description = "Aetheria Windows 11 AI Roleplay Platform";
                shortcut.Save();
            }
            catch { }
        }
    }
}
'@

Set-Content -Path $tempCs -Value $csSource -Encoding UTF8

& $cscExe /target:winexe /out:$OutputExe /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:Microsoft.CSharp.dll "/resource:$PayloadZip,Payload.zip" $tempCs | Out-Null

Remove-Item $tempCs -Force -ErrorAction SilentlyContinue

if (-not (Test-Path $OutputExe)) {
    throw "Failed to compile $OutputExe"
}
Write-Host "Created standalone Windows 11 GUI Setup EXE: $OutputExe" -ForegroundColor Green
