using System.Windows.Forms;
using HoldPlugin.Contracts;
using MediatR;
using vatsys;

namespace HoldPlugin.Handlers;

public class OpenClearedLevelMenuHandler : IRequestHandler<OpenClearedLevelMenuCommand>
{
    public Task Handle(OpenClearedLevelMenuCommand request, CancellationToken cancellationToken)
    {
        var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == request.Callsign);
        if (fdr is null)
            return Task.CompletedTask;

        var track = MMI.FindTrack(fdr);
        if (track is null)
            return Task.CompletedTask;

        MMI.OpenCFLMenu(track, Control.MousePosition);
        return Task.CompletedTask;
    }
}
