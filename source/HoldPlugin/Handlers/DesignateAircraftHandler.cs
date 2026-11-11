using HoldPlugin.Contracts;
using MediatR;
using vatsys;

namespace HoldPlugin.Handlers;

public class DesignateAircraftHandler : IRequestHandler<DesignateAircraftCommand>
{
    public Task Handle(DesignateAircraftCommand request, CancellationToken cancellationToken)
    {
        var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == request.Callsign);
        if (fdr is null)
            return Task.CompletedTask;

        var track = MMI.FindTrack(fdr);
        if (track is null)
            return Task.CompletedTask;

        MMI.SelectOrDeselectTrack(track);
        return Task.CompletedTask;
    }
}
