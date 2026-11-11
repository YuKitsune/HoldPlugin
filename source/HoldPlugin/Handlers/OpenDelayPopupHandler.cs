using HoldPlugin.Contracts;
using HoldPlugin.Controls;
using HoldPlugin.ViewModels;
using MediatR;
using vatsys;
using vatSysToolbox;
using vatSysToolbox.Windows;

namespace HoldPlugin.Handlers;

public class OpenDelayPopupHandler(IGuiInvoker guiInvoker, IMediator mediator, IErrorReporter errorReporter)
    : IRequestHandler<OpenDelayPopupCommand>
{
    public Task Handle(OpenDelayPopupCommand request, CancellationToken cancellationToken)
    {
        guiInvoker.InvokeOnGUI(mainForm =>
        {
            var viewModel = new DelayPopupViewModel(request.Callsign, request.WaypointName, request.Delay, mediator, errorReporter);
            var view = new DelayPopup(viewModel);
            var form = new PopupForm(view);

            // TODO: We should move this into the popup itself
            view.CloseRequested += (_, _) => form.Close();
            form.Shown += (_, _) => view.FocusDelayDuration();

            MMI.EnsureWindowVisible(form);
            form.Show(mainForm);
        });

        return Task.CompletedTask;
    }
}
