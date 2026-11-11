using HoldPlugin.Contracts;
using MediatR;
using vatsys;
using vatSysToolbox;
using vatSysToolbox.Windows;

namespace HoldPlugin.Handlers;

public class ChangeGlobalOpsHandler(WorkQueue workQueue) : IRequestHandler<ChangeGlobalOpsCommand>
{
    public Task Handle(ChangeGlobalOpsCommand request, CancellationToken cancellationToken)
    {
        workQueue.Enqueue(async () =>
        {
            var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == request.Callsign);
            if (fdr is null)
                return;

            FDP2.SetGlobalOps(fdr, request.GlobalOps);
        });

        return Task.CompletedTask;
    }
}
