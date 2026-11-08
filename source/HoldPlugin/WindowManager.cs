using System.Windows;
using System.Windows.Forms;

namespace HoldPlugin;

public class WindowManager(GuiInvoker guiInvoker)
{
    readonly IDictionary<string, VatSysForm> _windows = new Dictionary<string, VatSysForm>();
    readonly IDictionary<string, System.Drawing.Point> _lastLocations = new Dictionary<string, System.Drawing.Point>();

    public void TryCreateWindow(
        string key,
        string title,
        Func<IWindowHandle, FrameworkElement> createView,
        bool shrinkToContent = true,
        bool canClose = true,
        bool canMinimise = false,
        Size? size = null,
        Action<VatSysForm>? configureForm = null)
    {
        guiInvoker.InvokeOnUiThread(mainForm =>
        {
            if (_windows.TryGetValue(key, out var existingWindowHandle))
            {
                return;
            }

            var form = new VatSysForm(title, createView, shrinkToContent, canClose, canMinimise)
            {
                StartPosition = FormStartPosition.CenterParent
            };

            // If the view closes the form before it has a handle, the form is disposed and cannot be shown.
            if (form.IsDisposed)
                return;

            if (_lastLocations.TryGetValue(key, out var lastLocation))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = lastLocation;
            }

            if (size.HasValue)
            {
                form.Width = (int)size.Value.Width;
                form.Height = (int)size.Value.Height;
            }

            form.PerformLayout();
            form.Closed += (_, _) =>
            {
                // If the window is minimised or maximised, Location is not the normal position of the window
                _lastLocations[key] = form.WindowState == FormWindowState.Normal
                    ? form.Location
                    : form.RestoreBounds.Location;

                RemoveWindow(key);
            };
            _windows[key] = form;
            form.Show(mainForm);

            configureForm?.Invoke(form);
        });
    }

    public bool TryGetWindow(string key, out IWindowHandle? windowHandle)
    {
        if (_windows.TryGetValue(key, out var form))
        {
            windowHandle = form.WindowHandle;
            return true;
        }

        windowHandle = null;
        return false;
    }

    public void CloseWindow(string key)
    {
        guiInvoker.InvokeOnUiThread(mainForm =>
        {
            if (_windows.TryGetValue(key, out var form))
            {
                form.ForceClose();
            }
        });
    }

    void RemoveWindow(string key)
    {
        _windows.Remove(key);
    }
}
