using CommunityToolkit.Mvvm.Messaging;
using HoldPlugin.Contracts;
using HoldPlugin.ViewModels;
using MediatR;
using vatsys;
using vatSysToolbox.Windows;

namespace HoldPlugin;

/// <summary>
///     Initiates, updates, and stops the holds, and changes the delays and holds in the remarks.
///     Call the methods from the work queue.
/// </summary>
public class HoldService(
    HoldRepository holdRepository,
    HoldSlotRepository holdSlotRepository,
    HoldWindows holdWindows,
    WindowManager windowManager,
    IMediator mediator)
{
    // The hold duration that the hold key and the Hold item of the waypoint menu give.
    static readonly TimeSpan DefaultHoldDuration = TimeSpan.FromMinutes(10);

    // vatSys does not calculate an estimate from radar below this ground speed (knots).
    const double MinimumGroundSpeed = 10;

    // A hold that the FDR has in the remarks. HoldPoint is the name of the intersection.
    record HoldSource(FDP2.FDR.ExtractedRoute.Segment HoldSegment, DateTime ExitTime)
    {
        public string HoldPoint => HoldSegment.Intersection.Name;
    }

    /// <summary>
    ///     Tell the hold lists to show the holds again, and open the hold lists that have holds.
    /// </summary>
    public void RefreshHolds()
    {
        _ = mediator.Publish(new RefreshHoldsCommand());
    }

    /// <summary>
    ///     Initiate, update, or cancel the hold for the FDR, based on its remarks.
    ///     The remarks have only one hold. The delays (DLE) in the remarks are not holds. HoldPatches reads them from the remarks.
    /// </summary>
    public void SyncHold(FDP2.FDR fdr)
    {
        var existingHold = holdRepository.Find(fdr.Callsign);

        if (!TryFindHold(fdr, out var source))
        {
            if (existingHold is not null)
                RemoveHold(fdr, existingHold);
        }
        else if (existingHold is null)
        {
            InitiateHold(fdr, source);
        }
        else if (source.HoldPoint != existingHold.HoldPoint)
        {
            // The hold point changed, e.g. another controller changed the hold in the remarks.
            RemoveHold(fdr, existingHold);
            InitiateHold(fdr, source);
        }
        else
        {
            UpdateHold(fdr, existingHold, source);
        }

        RefreshHolds();
    }

    /// <summary>
    ///     Find the hold (HLD) in the remarks on the route. The remarks have only one hold. Search
    ///     the route ahead of the aircraft first, then the route behind the aircraft. In a hold,
    ///     SetOverflownIndexPatch keeps the hold point ahead of the aircraft.
    /// </summary>
    static bool TryFindHold(FDP2.FDR fdr, out HoldSource source)
    {
        source = null!;

        var holdRemark = DelayRemarks.ParseHold(fdr);
        if (holdRemark is null)
            return false;

        var holdSegment = fdr.FindWaypoint(holdRemark.WaypointName);
        if (holdSegment is null)
            return false;

        source = new HoldSource(holdSegment, holdRemark.ExitTime);
        return true;
    }

    /// <summary>
    ///     Start the hold. If the aircraft passed the hold point, the aircraft enters the hold
    ///     now. Then move the route back to the hold point. The ETO of the hold point is then the
    ///     entry time, thus vatSys moves a flight plan track back to the hold point.
    /// </summary>
    void InitiateHold(FDP2.FDR fdr, HoldSource source)
    {
        var holdSegment = source.HoldSegment;

        DateTime? holdEntryTime = null;
        if (!fdr.GetWaypointsAhead().Contains(holdSegment))
        {
            holdEntryTime = DateTime.UtcNow;
            RewindRoute(fdr, holdSegment);
        }

        var selectedCallsign = MMI.SelectedTrack?.GetFDR()?.Callsign;
        var isDesignated = fdr.Callsign == selectedCallsign;

        var level = fdr.CoupledTrack?.CorrectedAltitude ?? fdr.PRL;
        IClearedFlightLevel clearedFlightLevel = fdr.CFLLower <= 0
            ? new ClearedFlightLevel(fdr.CFLUpper)
            : new ClearedBlockLevel(fdr.CFLLower, fdr.CFLUpper);

        var state = GetHoldItemState(fdr);

        var holdItem = new HoldItem(
            fdr.Callsign,
            holdSegment.Intersection.Name,
            holdEntryTime,
            source.ExitTime,
            isDesignated,
            level,
            clearedFlightLevel,
            fdr.RVSM,
            fdr.GlobalOpData,
            state);

        holdRepository.Add(holdItem);

        if (state != HoldItemState.Unconcerned)
            AllocateSlot(holdItem.HoldPoint);

        UpdateEstimates(fdr);

        holdWindows.EnsureHoldWindowsAreOpen();
    }

    void UpdateHold(FDP2.FDR fdr, HoldItem hold, HoldSource source)
    {
        IClearedFlightLevel clearedFlightLevel = fdr.CFLLower <= 0
            ? new ClearedFlightLevel(fdr.CFLUpper)
            : new ClearedBlockLevel(fdr.CFLLower, fdr.CFLUpper);

        var state = GetHoldItemState(fdr);

        // CalculateETOsPatch calculates the ETOs after the hold point from the exit time.
        var exitTimeChanged = source.ExitTime != hold.HoldExitTime;

        holdRepository.Update(hold.Callsign, h => h with
        {
            ClearedFlightLevel = clearedFlightLevel,
            RvsmApproved = fdr.RVSM,
            GlobalOps = fdr.GlobalOpData,
            State = state,
            HoldExitTime = source.ExitTime
        });

        if (exitTimeChanged)
        {
            UpdateEstimates(fdr);
        }

        if (state == hold.State)
            return;

        if (state == HoldItemState.Unconcerned)
        {
            FreeSlotIfUnused(hold.HoldPoint);
        }
        else
        {
            AllocateSlot(hold.HoldPoint);
        }
    }

    /// <summary>
    ///     Stop the hold in the plugin. Do not change the remarks.
    /// </summary>
    void RemoveHold(FDP2.FDR? fdr, HoldItem hold)
    {
        if (!holdRepository.TryRemove(hold.Callsign))
            return;

        if (fdr is not null)
        {
            ResetEstimates(fdr, hold);

            // CheckForRAMPatch does not stop a RAM alert any more. Request an APR, so that vatSys
            // finds the aircraft on its route again and removes a RAM alert. The APR goes on the
            // task queue of the FDR, after the processing that ResetEstimates requested.
            RDP.RequestAPR(fdr);
        }

        FreeSlotIfUnused(hold.HoldPoint);
    }

    /// <summary>
    ///     Remove the hold at the hold point from the remarks, then stop the hold. Without permission, the plugin cannot remove the hold from the
    ///     remarks, and the hold would initiate again. Then do nothing.
    /// </summary>
    public void CancelHold(FDP2.FDR? fdr, HoldItem hold)
    {
        if (fdr is not null)
        {
            var holdPointName = fdr.FindHoldWaypoint(hold.HoldPoint)?.GetFlightPlanName() ?? hold.HoldPoint;
            if (DelayRemarks.HasHold(fdr, holdPointName))
            {
                if (!fdr.HavePermission)
                    return;

                DelayRemarks.RemoveHold(fdr, holdPointName);
            }
        }

        RemoveHold(fdr, hold);
    }

    /// <summary>
    ///     Set a hold at the waypoint with the default length. The exit time is the arrival time
    ///     at the waypoint plus the default length. If the aircraft passed the waypoint, the
    ///     arrival time is now. <paramref name="waypointName"/> is the name in the flight plan
    ///     route.
    /// </summary>
    public void SetDefaultHold(FDP2.FDR fdr, string waypointName)
    {
        var holdSegment = fdr.FindWaypoint(waypointName);
        if (holdSegment is null)
            return;

        // vatSys sets the ETO to DateTime.MaxValue when it has no estimate.
        var now = DateTime.UtcNow;
        var arrivalTime = holdSegment.ETO != DateTime.MaxValue && holdSegment.ETO > now ? holdSegment.ETO : now;

        SetHold(fdr, waypointName, arrivalTime + DefaultHoldDuration);
    }

    /// <summary>
    ///     Set the hold at the waypoint in the remarks, then initiate or update the hold.
    ///     <paramref name="waypointName"/> is the name in the flight plan route. Do nothing when
    ///     the controller does not have permission to change the flight plan.
    /// </summary>
    public void SetHold(FDP2.FDR fdr, string waypointName, DateTime exitTime)
    {
        if (!fdr.HavePermission)
            return;

        DelayRemarks.SetHold(fdr, waypointName, exitTime);
        SyncHold(fdr);
    }

    /// <summary>
    ///     Set the delay (DLE) at the waypoint in the remarks. FDRPropertyChangedHandler calculates
    ///     the ETOs again when the remarks change. <paramref name="waypointName"/> is the name in
    ///     the flight plan route. Do nothing when the controller does not have permission to
    ///     change the flight plan.
    /// </summary>
    public void SetDelay(FDP2.FDR fdr, string waypointName, TimeSpan delay)
    {
        if (!fdr.HavePermission || delay <= TimeSpan.Zero)
            return;

        DelayRemarks.SetDelay(fdr, waypointName, delay);
        SyncHold(fdr);
    }

    /// <summary>
    ///     Remove the delay (DLE) at the waypoint from the remarks. FDRPropertyChangedHandler calculates
    ///     the ETOs again when the remarks change. Do nothing when the controller does not have
    ///     permission to change the flight plan.
    /// </summary>
    public void RemoveDelay(FDP2.FDR fdr, string waypointName)
    {
        if (!fdr.HavePermission)
            return;

        DelayRemarks.RemoveDelay(fdr, waypointName);
    }

    /// <summary>
    ///     Move the route back to the hold point. The hold point becomes the next waypoint, and the
    ///     hold point and the segments after it have no ATO. Remove the position reports after the
    ///     hold point. Add the EET and the distance of each removed position report to the next
    ///     segment, the same as vatSys does.
    /// </summary>
    static void RewindRoute(FDP2.FDR fdr, FDP2.FDR.ExtractedRoute.Segment holdSegment)
    {
        var route = fdr.ParsedRoute.ToList();
        var holdPointIndex = route.IndexOf(holdSegment);

        FDP2.FDR.ExtractedRoute.Segment? nextSegment = null;
        for (var index = route.Count - 1; index >= holdPointIndex; index--)
        {
            var segment = route[index];
            if (segment.Type == FDP2.FDR.ExtractedRoute.Segment.SegmentTypes.APRUPR && nextSegment is not null)
            {
                nextSegment.EET += segment.EET;
                nextSegment.Distance += segment.Distance;
                fdr.ParsedRoute.Remove(segment);
                continue;
            }

            segment.ATONoEvent = DateTime.MaxValue;
            nextSegment = segment;
        }

        fdr.ParsedRoute.OverflownIndex = holdPointIndex - 1;
        fdr.ParsedRoute.FirePropertyChanged("OverflownIndex");
    }

    // FDR property flags don't cleanly distinguish jurisdiction from incoming/outgoing handoff
    // after acceptance (HandoffController can linger), so we derive state from the track's HMI
    // state instead. Tracks may not exist for every FDR; treat missing tracks as Unconcerned.
    static HoldItemState GetHoldItemState(FDP2.FDR fdr)
    {
        var track = MMI.FindTrack(fdr);
        if (track is null)
            return HoldItemState.Unconcerned;

        return track.State switch
        {
            MMI.HMIStates.Jurisdiction => HoldItemState.Jurisdiction,
            MMI.HMIStates.HandoverIn => HoldItemState.Handover,
            MMI.HMIStates.HandoverOut => HoldItemState.Handover,
            _ => HoldItemState.Unconcerned
        };
    }

    public void UpdateEstimates(FDP2.FDR fdr)
    {
        FDP2.Process(fdr);
    }

    /// <summary>
    ///     Call this after terminating a hold to recalculate the ETO of the hold point, then reprocess the FDR.
    /// </summary>
    void ResetEstimates(FDP2.FDR fdr, HoldItem hold)
    {
        var holdSegment = fdr.FindHoldWaypoint(hold.HoldPoint);
        if (holdSegment is not null)
        {
            // Before the hold point is the next waypoint, the aircraft is not near the hold point.
            // vatSys then only calculates the estimates again.
            var didEnterHold = hold.HoldEntryTime is not null;
            if (didEnterHold || fdr.GetNextWaypoint() == holdSegment)
            {
                if (!TryAddPositionReportBeforeHoldPoint(fdr, holdSegment) && didEnterHold)
                {
                    // There is no radar position (e.g. a flight plan track). The track is at the hold point, thus it
                    // leaves the hold point now. vatSys calculates the ETOs after the hold point from this ATO.
                    holdSegment.ATONoEvent = DateTime.UtcNow;
                }
            }
        }

        FDP2.Process(fdr);

        // Add a position report at the radar position before the hold point, the same as vatSys does for an APR.
        // vatSys calculates the ETOs from the last ATO. Thus the ETO of the hold point is the time of the position
        // report plus the time to fly from the radar position to the hold point at the ground speed.
        static bool TryAddPositionReportBeforeHoldPoint(FDP2.FDR fdr, FDP2.FDR.ExtractedRoute.Segment holdSegment)
        {
            var radarTrack = fdr.CoupledTrack;
            if (radarTrack is null || radarTrack.GroundSpeed < MinimumGroundSpeed)
                return false;

            var holdPointIndex = fdr.ParsedRoute.IndexOf(holdSegment);
            if (holdPointIndex < 1)
                return false;

            var previousSegment = fdr.ParsedRoute[holdPointIndex - 1];
            var distanceToHoldPoint = Conversions.CalculateDistance(radarTrack.LatLong, holdSegment.Intersection.LatLong);
            var eetToHoldPoint = TimeSpan.FromHours(distanceToHoldPoint / radarTrack.GroundSpeed);

            var positionReport = new FDP2.FDR.ExtractedRoute.Segment(fdr.ParsedRoute)
            {
                Type = FDP2.FDR.ExtractedRoute.Segment.SegmentTypes.APRUPR,
                Intersection = new Airspace2.Intersection("APR", radarTrack.LatLong),
                ATONoEvent = radarTrack.Timestamp,
                PCL = radarTrack.CorrectedAltitude,
                GroundSpeed = radarTrack.GroundSpeed,
                Distance = Conversions.CalculateDistance(previousSegment.Intersection.LatLong, radarTrack.LatLong),
                Track = Conversions.CalculateTrack(previousSegment.Intersection.LatLong, radarTrack.LatLong),

                // Keep the total EET from the previous segment to the hold point the same, if possible.
                EET = holdSegment.EET > eetToHoldPoint ? holdSegment.EET - eetToHoldPoint : TimeSpan.Zero
            };
            positionReport.ETONoEvent = positionReport.ATO;

            holdSegment.EET = eetToHoldPoint;
            holdSegment.Distance = distanceToHoldPoint;

            fdr.ParsedRoute.Insert(holdPointIndex, positionReport);
            fdr.PRL = radarTrack.CorrectedAltitude;
            return true;
        }
    }

    /// <summary>
    ///     Put the hold point in an empty slot, if no slot has it.
    /// </summary>
    void AllocateSlot(string holdPoint)
    {
        if (holdSlotRepository.TryAutoAllocateSlot(holdPoint))
            WeakReferenceMessenger.Default.Send(new HoldSlotsUpdatedCommand());
    }

    /// <summary>
    ///     Free the automatically allocated slot of the hold point, if the hold list shows no
    ///     aircraft at the hold point.
    /// </summary>
    void FreeSlotIfUnused(string holdPoint)
    {
        var isUsed = holdRepository.GetHolds()
            .Any(h => h.HoldPoint == holdPoint && h.State != HoldItemState.Unconcerned);

        if (isUsed)
            return;

        if (!holdSlotRepository.TryFreeAutoAllocatedSlot(holdPoint))
            return;

        windowManager.CloseWindow(WindowKeys.HoldFor(holdPoint));
        WeakReferenceMessenger.Default.Send(new HoldSlotsUpdatedCommand());
    }
}
