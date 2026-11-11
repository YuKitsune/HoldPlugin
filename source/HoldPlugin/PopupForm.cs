using System.Windows;
using System.Windows.Controls;
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

        // A Canvas measures its child with no size limit. The view then always has its natural size,
        // and SizeChanged occurs when WPF has done the layout of the view.
        var canvas = new Canvas();
        canvas.Children.Add(child);

        var elementHost = new ElementHost
        {
            Dock = DockStyle.Fill,
            Child = canvas
        };
        Controls.Add(elementHost);

        child.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        ResizeToContent(child.DesiredSize);
        child.SizeChanged += (_, e) => ResizeToContent(e.NewSize);
    }

    void ResizeToContent(System.Windows.Size contentSize)
    {
        ClientSize = new System.Drawing.Size(
            (int)Math.Ceiling(contentSize.Width),
            (int)Math.Ceiling(contentSize.Height));
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
