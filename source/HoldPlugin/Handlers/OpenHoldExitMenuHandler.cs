using System.Windows.Forms;
using HoldPlugin.Contracts;
using MediatR;
using vatsys;
using vatSysToolbox;
using vatSysToolbox.Windows;

namespace HoldPlugin.Handlers;

public class OpenHoldExitMenuHandler(
    HoldRepository holdRepository,
    HoldService holdService,
    WorkQueue workQueue,
    IErrorReporter errorReporter)
    : IRequestHandler<OpenHoldExitMenuCommand>
{
    public Task Handle(OpenHoldExitMenuCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var holdItem = holdRepository.Find(request.Callsign);
            if (holdItem is null)
                return Task.CompletedTask;

            var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == request.Callsign);
            if (fdr is null || !fdr.HavePermission)
                return Task.CompletedTask;

            // TimeMenu window = new TimeMenu(holdItem.HoldExitTime, TimeSelected: false);
            var window = Activator.CreateInstance(
                Type.GetType("vatsys.TimeMenu, vatsys"),
                [
                    holdItem.HoldExitTime, // Selected time
                    false, // bool TimeSelected
                    true, // bool CancelButton
                    -90, // int TimeCountMin = -90
                    90, //int TimeCountMax = 90
                ]) as BaseForm;

            if (window is null)
                return Task.CompletedTask;

            try
            {

                window.Text = holdItem.Callsign;
                window.StartPosition = FormStartPosition.Manual;
                window.Location = Cursor.Position;
                MMI.EnsureWindowVisible((Form)window);
                window.ShowDialog();

                if (window.DialogResult == DialogResult.OK)
                {
                    var selectedTime = (DateTime)window.GetType().GetField("SelectedTime").GetValue(window);
                    if (selectedTime >= DateTime.UtcNow)
                    {
                        workQueue.Enqueue(async () =>
                        {
                            // The remarks contain the hold, thus change the exit time to a delay
                            // from the arrival time.
                            var hold = holdRepository.Find(holdItem.Callsign);
                            var holdSegment = hold is not null ? fdr.FindHoldWaypoint(hold.HoldPoint) : null;
                            if (hold is null || holdSegment is null ||
                                !fdr.TryGetHoldArrivalTime(holdSegment, hold.HoldEntryTime, out var arrivalTime))
                                return;

                            holdService.SetDelay(fdr, DelayType.Hold, holdSegment.GetFlightPlanName(), selectedTime - arrivalTime);
                        });
                    }
                }
            }
            finally
            {
                window.Dispose();
            }
        }
        catch (Exception ex)
        {
            errorReporter.ReportError(ex);
        }

        return Task.CompletedTask;
    }
}
