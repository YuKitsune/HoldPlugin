using System.Windows.Forms;
using HoldPlugin.Contracts;
using MediatR;
using vatsys;
using vatSysToolbox;

namespace HoldPlugin.Handlers;

public class OpenClearedLevelMenuHandler(IErrorReporter errorReporter) : IRequestHandler<OpenClearedLevelMenuCommand>
{
    public Task Handle(OpenClearedLevelMenuCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == request.Callsign);
            if (fdr is null)
                return Task.CompletedTask;

            var track = MMI.FindTrack(fdr);
            if (track is null)
                return Task.CompletedTask;

            MMI.OpenCFLMenu(track, Control.MousePosition);
        }
        catch (Exception ex)
        {
            errorReporter.ReportError(ex);
        }

        return Task.CompletedTask;
    }
}
