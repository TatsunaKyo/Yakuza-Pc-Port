// The PC port owns disc validation, Restored detection, patching and saves.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

internal static class Yakuza
{
    static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string Ini = Path.Combine(Root, "Yakuza.ini");

    static string Setting(string key)
    {
        foreach (string raw in File.ReadLines(Ini))
        {
            string line = raw.Trim();
            if (line.StartsWith("[")) break;
            int pos = line.IndexOf('=');
            if (pos >= 0 && line.Substring(0, pos).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                return line.Substring(pos + 1).Trim();
        }
        return "";
    }

    static string Pick()
    {
        using (var dialog = new OpenFileDialog())
        {
            dialog.Title = "Open a Yakuza disc image";
            dialog.Filter = "PS2 disc images (*.iso, *.iso.gz)|*.iso;*.iso.gz|All files (*.*)|*.*";
            string start = Setting("disc_folder");
            dialog.InitialDirectory = Directory.Exists(start) ? start : Root;
            dialog.RestoreDirectory = true;
            return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
        }
    }

    static string Prepare(string path)
    {
        if (!Disc.IsGzip(path)) return Path.GetFullPath(path);
        using (var form = new Form { Text = "Opening " + Path.GetFileName(path), ClientSize = new Size(430, 72),
                                     StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog,
                                     MaximizeBox = false, MinimizeBox = false })
        {
            var label = new Label { Left = 12, Top = 12, AutoSize = true, Text = "Opening compressed image..." };
            form.Controls.Add(label);
            form.Controls.Add(new ProgressBar { Left = 12, Top = 37, Width = 406, Style = ProgressBarStyle.Marquee });
            bool cancelled = false;
            form.FormClosing += delegate { cancelled = true; };
            form.Show();
            var timer = Stopwatch.StartNew();
            return Disc.Open(path, Path.Combine(Root, "Discs"), delegate(string stage, long bytes) {
                if (timer.ElapsedMilliseconds < 100) return;
                label.Text = stage + "   " + (bytes >> 20) + " MiB";
                Application.DoEvents();
                if (cancelled) throw new OperationCanceledException();
                timer.Restart();
            });
        }
    }

    // Quote an argument using the Windows C runtime's command-line rules.
    static string Quote(string value)
    {
        var text = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            text.Append('\\', c == '"' ? 2 * slashes + 1 : slashes).Append(c);
            slashes = 0;
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            WindowLayout.EnableDpiAwareness();
            Application.EnableVisualStyles();
            var passthrough = new List<string>();
            string disc = null;
            bool choose = false;
            bool borderless = !Setting("window_layout").Equals("large", StringComparison.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--iso":
                        if (++i == args.Length) throw new ArgumentException("--iso needs a disc image.");
                        disc = args[i]; break;
                    case "--choose": choose = true; break;
                    case "--windowed": borderless = false; break;
                    case "--borderless": borderless = true; break;
                    case "--patch":
                        if (++i == args.Length) throw new ArgumentException("--patch needs a patch file.");
                        passthrough.Add("--patch"); passthrough.Add(Path.GetFullPath(args[i])); break;
                    default:
                        if (args.Length == 1 && (Disc.IsGzip(args[i]) || args[i].EndsWith(".iso", StringComparison.OrdinalIgnoreCase)))
                            disc = args[i];
                        else passthrough.Add(args[i]);
                        break;
                }
            }
            string saved = Setting("iso");
            if (saved.Length > 0) saved = Path.Combine(Root, saved);
            if (choose || (disc == null && !File.Exists(saved)))
            {
                disc = Pick();
                if (disc == null) return 0;
            }
            else if (disc == null && Disc.IsGzip(saved)) disc = saved;
            if (disc != null)
            {
                passthrough.Insert(0, Prepare(disc));
                passthrough.Insert(0, "--iso");
            }
            var start = new ProcessStartInfo(Path.Combine(Root, "Yakuza-runtime.exe"),
                                            string.Join(" ", passthrough.Select(Quote).ToArray()));
            start.WorkingDirectory = Root;
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.EnvironmentVariables["PS2X_LAUNCHER_HOME"] = Root;
            start.EnvironmentVariables["PS2X_LAUNCHER_NAME"] = "Yakuza";
            start.EnvironmentVariables["PS2X_WINDOWED"] = "1";
            using (var game = Process.Start(start))
            {
                WindowLayout.Follow(game, borderless);
                return game.ExitCode;
            }
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "Yakuza PC Port", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
