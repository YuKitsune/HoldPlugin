using HoldPlugin.Contracts;
using MediatR;
using vatsys;
using vatSysToolbox;

namespace HoldPlugin.Handlers;

public class DesignateAircraftHandler(IErrorReporter errorReporter) : IRequestHandler<DesignateAircraftCommand>
{
    public Task Handle(DesignateAircraftCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == request.Callsign);
            if (fdr is null)
                return Task.CompletedTask;

            var track = MMI.FindTrack(fdr);
            if (track is null)
                return Task.CompletedTask;

            MMI.SelectOrDeselectTrack(track);
        }
        catch (Exception ex)
        {
            errorReporter.ReportError(ex);
        }

        return Task.CompletedTask;
    }
}
