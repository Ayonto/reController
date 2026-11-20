using System.IO;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace ReController.App.Infrastructure;

/// <summary>Notification-area icon with a small menu. Uses WinForms NotifyIcon (no extra dependency).</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _toggle;

    public TrayIcon(Action show, Action<bool> setRemap, Action exit)
    {
        var menu = new WinForms.ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add("Open reController", null, (_, _) => show());
        _toggle = new WinForms.ToolStripMenuItem("Remap enabled") { CheckOnClick = true };
        _toggle.Click += (_, _) => setRemap(_toggle.Checked);
        menu.Items.Add(_toggle);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/reController.ico"))!.Stream;
        _icon = new WinForms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream),
            Text = "reController",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) show(); };
    }

    public void SetRemapState(bool enabled)
    {
        _toggle.Checked = enabled;
        _icon.Text = enabled ? "reController — remapping" : "reController — idle";
    }

    public void Notify(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(2500);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
