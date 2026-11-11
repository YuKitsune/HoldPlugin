using System.Windows;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace HoldPlugin;

/// <summary>
///     A form with no border or title bar, at the mouse cursor. The form closes when it is not
///     the active form, e.g. when the user clicks outside it.
/// </summary>
public class PopupForm : Form
{
    bool _isClosing;

    public PopupForm(FrameworkElement child)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Location = Cursor.Position;

        var elementHost = new ElementHost
        {
            Dock = DockStyle.Fill,
            Child = child
        };
        Controls.Add(elementHost);

        child.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        ClientSize = new System.Drawing.Size(
            (int)Math.Ceiling(child.DesiredSize.Width),
            (int)Math.Ceiling(child.DesiredSize.Height));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        _isClosing = !e.Cancel;
    }

    // The form also deactivates while it closes. Do not close it a second time.
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);

        if (!_isClosing && !IsDisposed)
            Close();
    }
}
