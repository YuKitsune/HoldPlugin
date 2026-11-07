using System.Windows;
using System.Windows.Forms;

namespace HoldPlugin;

public class WindowManager(GuiInvoker guiInvoker)
{
    readonly IDictionary<string, VatSysForm> _windows = new Dictionary<string, VatSysForm>();

    public void TryCreateWindow(
        string key,
        string title,
        Func<IWindowHandle, UIElement> createView,
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

            if (size.HasValue)
            {
                form.Width = (int)size.Value.Width;
                form.Height = (int)size.Value.Height;
            }

            form.PerformLayout();
            form.Closed += (_, _) => RemoveWindow(key);
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
