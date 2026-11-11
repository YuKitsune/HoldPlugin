using HoldPlugin.Contracts;
using HoldPlugin.Controls;
using HoldPlugin.ViewModels;
using MediatR;
using vatSysToolbox;
using vatSysToolbox.Windows;

namespace HoldPlugin.Handlers;

public class OpenDelayPopupHandler(WindowManager windowManager, IMediator mediator, IErrorReporter errorReporter)
    : IRequestHandler<OpenDelayPopupCommand>
{
    public Task Handle(OpenDelayPopupCommand request, CancellationToken cancellationToken)
    {
        windowManager.ShowPopup(windowHandle =>
        {
            var viewModel = new DelayPopupViewModel(request.Callsign, request.WaypointName, request.Delay, windowHandle, mediator, errorReporter);
            return new DelayPopup(viewModel, windowHandle);
        });

        return Task.CompletedTask;
    }
}
