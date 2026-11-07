using System.Windows.Forms;

namespace HoldPlugin;

public class GuiInvoker(Action<MethodInvoker> invoker, Action<Exception> onError)
{
    public void InvokeOnUiThread(Action<Form> action)
    {
        var mainForm = Application.OpenForms["MainForm"];
        if (mainForm == null)
            return;

        // If already on UI thread, execute directly to avoid deadlock
        if (!mainForm.InvokeRequired)
        {
            Run(action, mainForm);
            return;
        }

        invoker.Invoke(delegate { Run(action, mainForm); });
    }

    // An exception that is not caught on the UI thread stops vatSys.
    void Run(Action<Form> action, Form mainForm)
    {
        try
        {
            action(mainForm);
        }
        catch (Exception ex)
        {
            try
            {
                onError(ex);
            }
            catch
            {
                // Ignore errors during error reporting
            }
        }
    }
}
