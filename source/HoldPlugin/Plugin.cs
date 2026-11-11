using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Forms;
using CommunityToolkit.Mvvm.Messaging;
using HoldPlugin.Contracts;
using HoldPlugin.Controls;
using HoldPlugin.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using vatsys;
using vatsys.Plugin;
using vatSysToolbox;
using vatSysToolbox.Extensions;
using vatSysToolbox.Messaging;
using vatSysToolbox.Windows;
using Track = vatsys.Track;

namespace HoldPlugin;

// TODO: Add DLE detection, and Delay menu
// TODO: Smaller label when in the hold
// TODO: Clean up

[Export(typeof(IPlugin))]
public class Plugin
    : IPlugin,
    IRecipient<DesignateAircraftCommand>,
    IRecipient<CancelHoldCommand>,
    IRecipient<OpenClearedLevelMenuCommand>,
    IRecipient<OpenHoldExitMenuCommand>,
    IRecipient<ChangeGlobalOpsCommand>,
    IRecipient<OpenDelayPopupCommand>,
    IRecipient<SetHoldDelayCommand>,
    IRecipient<HoldCompletedCommand>,
    IRecipient<HoldKeyPressedCommand>,
    IRecipient<HoldPointAddedCommand>,
    IRecipient<HoldPointRemovedCommand>,
    IRecipient<RefreshHoldsCommand>
{
    // The hold duration to use when the label data has no exit time.
    static readonly TimeSpan DefaultHoldDuration = TimeSpan.FromMinutes(10);

    // vatSys does not calculate an estimate from radar below this ground speed (knots).
    const double MinimumGroundSpeed = 10;
    
#if DEBUG
    public const string Name = "Hold Plugin - Debug";
#else
    public const string Name = "Hold Plugin";
#endif

    /// <summary>
    /// The name that other plugins use to send messages to this plugin.
    /// </summary>
    public const string MailboxName = "dev.eoinmotherway.hold";

    // The messages that the plugin sends have no data
    static readonly MessageContent EmptyContent = new("application/json", "{}"u8.ToArray());

    readonly IErrorReporter _errorReporter;
    readonly IGuiInvoker _guiInvoker;
    readonly IPluginMessenger _messenger;

    // Do all work that changes the plugin state on this queue. The queue does one work item at a
    // time, so the work items do not need a lock.
    readonly WorkQueue _workQueue;

    readonly HoldRepository _holdRepository = new();
    readonly HoldSlotRepository _holdSlotRepository = new();
    readonly KeySettings _keySettings = new();
    readonly Dictionary<string, FDP2.FDR> _subscribedFDRs = new();

    readonly WindowManager _windowManager;

    string IPlugin.Name => Name;

    public Plugin()
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .AddVatSysToolbox(Name, MailboxName)
            .BuildServiceProvider();

        _errorReporter = serviceProvider.GetRequiredService<IErrorReporter>();
        _guiInvoker = serviceProvider.GetRequiredService<IGuiInvoker>();
        _messenger = serviceProvider.GetRequiredService<IPluginMessenger>();
        _workQueue = serviceProvider.GetRequiredService<WorkQueue>();
        _windowManager = serviceProvider.GetRequiredService<WindowManager>();

        WeakReferenceMessenger.Default.Register<DesignateAircraftCommand>(this);
        WeakReferenceMessenger.Default.Register<CancelHoldCommand>(this);
        WeakReferenceMessenger.Default.Register<OpenClearedLevelMenuCommand>(this);
        WeakReferenceMessenger.Default.Register<OpenHoldExitMenuCommand>(this);
        WeakReferenceMessenger.Default.Register<ChangeGlobalOpsCommand>(this);
        WeakReferenceMessenger.Default.Register<OpenDelayPopupCommand>(this);
        WeakReferenceMessenger.Default.Register<SetHoldDelayCommand>(this);
        WeakReferenceMessenger.Default.Register<HoldCompletedCommand>(this);
        WeakReferenceMessenger.Default.Register<HoldKeyPressedCommand>(this);
        WeakReferenceMessenger.Default.Register<HoldPointAddedCommand>(this);
        WeakReferenceMessenger.Default.Register<HoldPointRemovedCommand>(this);
        WeakReferenceMessenger.Default.Register<RefreshHoldsCommand>(this);

        HoldKeyPatches.Initialize(_keySettings, _errorReporter);
        HoldPatches.Apply(Name, _holdRepository, _errorReporter);

        MMI.SelectedTrackChanged += OnSelectedTrackChanged;
        FDP2.FDRsChanged += OnFDRsChanged;

        CreateMenuItems();

        // The messenger receives the messages on a background thread
        _messenger.Subscribe(HoldMessages.ToggleSetupWindow, (_, _) =>
        {
            ToggleHoldSetup();
            return Task.CompletedTask;
        });
        _messenger.Start();
    }

    void CreateMenuItems()
    {
        var menuItem = new CustomToolStripMenuItem(
            CustomToolStripMenuItemWindowType.Main,
            CustomToolStripMenuItemCategory.Tools,
            new ToolStripMenuItem("Hold Setup"));
        menuItem.Item.Click += (s, e) => OpenHoldSetup();
        
        MMI.AddCustomMenuItem(menuItem);
    }

    void OpenHoldSetup()
    {
        _windowManager.TryCreateWindow(
            WindowKeys.HoldSetup,
            "Hold Setup",
            windowHandle =>
            {
                windowHandle.Closed += (_, _) => _ = Broadcast(HoldMessages.SetupWindowClosed);
                _ = Broadcast(HoldMessages.SetupWindowOpened);

                var viewModel = new HoldSetupViewModel(_holdSlotRepository, _guiInvoker, _errorReporter);
                var view = new HoldSetup(viewModel);
                return view;
            },
            canMinimise: false);
    }

    void ToggleHoldSetup()
    {
        _guiInvoker.InvokeOnGUI(_ =>
        {
            if (_windowManager.TryGetWindow(WindowKeys.HoldSetup, out _))
                _windowManager.CloseWindow(WindowKeys.HoldSetup);
            else
                OpenHoldSetup();
        });
    }

    // Tells the other plugins (for example, the HLD SET button of the Eurocat Menus plugin) that a window opened or
    // closed
    async Task Broadcast(string messageType)
    {
        try
        {
            await _messenger.Broadcast(messageType, EmptyContent);
        }
        catch (Exception ex)
        {
            _errorReporter.ReportError(ex, $"Cannot send the message {messageType}");
        }
    }

    void EnsureHoldWindowsAreOpen()
    {
        var slots = _holdSlotRepository.GetSlots();
        var holds = _holdRepository.GetHolds();

        foreach (var descriptor in slots)
        {
            var holdPointName = descriptor.GetHoldPointName();

            if (holdPointName is null)
                continue;

            var itemsToDisplay = holds
                .Where(h => h.HoldPoint == holdPointName && h.State != HoldItemState.Unconcerned)
                .ToArray();

            if (itemsToDisplay.Any())
            {
                _windowManager.TryCreateWindow(
                    WindowKeys.HoldFor(holdPointName),
                    $"HOLD {holdPointName} WINDOW",
                    handle =>
                    {
                        var viewModel = new HoldListViewModel(holdPointName, itemsToDisplay, _holdRepository, _holdSlotRepository, handle, _guiInvoker);
                        var view = new HoldList(viewModel);
                        return view;
                    },
                    sizeToContent: false,
                    canClose: false,
                    canMinimise: false,
                    new Size(480, 150));
            }
        }

        var allocatedNames = GetAllocatedHoldPointNames(slots);
        var otherHolds = holds
            .Where(h => !allocatedNames.Contains(h.HoldPoint) && h.State != HoldItemState.Unconcerned)
            .ToArray();
        if (otherHolds.Any())
        {
            _windowManager.TryCreateWindow(
                WindowKeys.HoldOther(),
                "HOLD OTHER WINDOW",
                handle =>
                {
                    var viewModel = new HoldListViewModel(string.Empty, otherHolds, _holdRepository, _holdSlotRepository, handle, _guiInvoker);
                    var view = new HoldList(viewModel);
                    return view;
                },
                sizeToContent: false,
                canClose: false,
                canMinimise: false,
                new Size(480, 150));
        }
    }

    static HashSet<string> GetAllocatedHoldPointNames(IHoldPointDescriptor[] slots) =>
        slots
            .Select(d => d.GetHoldPointName())
            .Where(n => n is not null)
            .ToHashSet()!;

    public void OnFDRUpdate(FDP2.FDR updated)
    {
        // Unused
        // Hold detection is handled by OnFDRPropertyChanged for immediate response
    }

    public void OnRadarTrackUpdate(RDP.RadarTrack updated)
    {
        _workQueue.Enqueue(async () =>
        {
            try
            {
                if (updated.CoupledFDR is null)
                    return;

                var holdItem = _holdRepository.Find(updated.CoupledFDR.Callsign);
                if (holdItem is not null)
                {
                    _holdRepository.Update(holdItem.Callsign, h => h with { Level = updated.CorrectedAltitude });
                    WeakReferenceMessenger.Default.Send(new RefreshHoldsCommand());
                }
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
            }
        });
    }

    /// <summary>
    ///     Find a hold annotation (H/XXX or H/XXX/42) in the label data. This does not match the
    ///     waypoint to the route.
    /// </summary>
    static bool TryParseHoldAnnotation(FDP2.FDR fdr, out string holdPointPrefix, out int? exitTimeMinutes)
    {
        holdPointPrefix = string.Empty;
        exitTimeMinutes = null;

        var parts = fdr.LabelOpData.Split(' ');
        foreach (var part in parts)
        {
            // Valid formats:
            // H/RIVET or H\RIVET - initiate only
            // H/RIVET/42 or H\RIVET\42 - can be adopted
            if (!part.StartsWith("H/") && !part.StartsWith("H\\"))
                continue;

            var segments = part.Split('/', '\\');
            if (segments.Length < 2)
                continue;

            holdPointPrefix = segments[1];

            if (segments.Length >= 3 && int.TryParse(segments[2], out var minutes))
            {
                exitTimeMinutes = minutes;
            }

            return true;
        }

        return false;
    }

    void OnSelectedTrackChanged(object? sender, EventArgs e)
    {
        _workQueue.Enqueue(async () =>
        {
            try
            {
                var selectedCallsign = MMI.SelectedTrack?.GetFDR()?.Callsign;
                foreach (var hold in _holdRepository.GetHolds())
                {
                    _holdRepository.Update(hold.Callsign, h => h with { IsDesignated = h.Callsign == selectedCallsign });
                }

                WeakReferenceMessenger.Default.Send(new RefreshHoldsCommand());
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
            }
        });
    }

    void OnFDRsChanged(object? sender, EventArgs e)
    {
        _workQueue.Enqueue(async () =>
        {
            try
            {
                SubscribeToAllFDRs();
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
            }
        });
    }

    void SubscribeToAllFDRs()
    {
        // Unsubscribe from all previous FDRs (snapshot to avoid enumeration issues)
        foreach (var fdr in _subscribedFDRs.Values.ToArray())
        {
            fdr.PropertyChanged -= OnFDRPropertyChanged;
        }

        _subscribedFDRs.Clear();

        // Subscribe to all current FDRs (snapshot to avoid enumeration issues)
        foreach (var fdr in FDP2.GetFDRs.ToArray())
        {
            _subscribedFDRs[fdr.Callsign] = fdr;
            fdr.PropertyChanged += OnFDRPropertyChanged;
        }

        // Sync hold states in case FDR objects were replaced (e.g. after assuming a tag from NONE
        // or accepting a handoff, FDRsChanged fires with a new FDR that already has
        // IsTrackedByMe=true, so no PropertyChanged transition fires for that property).
        foreach (var fdr in _subscribedFDRs.Values.ToArray())
        {
            SyncHold(fdr);
        }
    }

    /// <summary>
    ///     Initiate, adopt, or cancel holds based on the remarks, the label data, and route state.
    ///     Tracks all holds regardless of jurisdiction.
    /// </summary>
    void OnFDRPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not FDP2.FDR fdr)
            return;

        // Only process relevant property changes to avoid queue backlog
        string[] relevantPropertyNames =
        [
            nameof(FDP2.FDR.LabelOpData),
            nameof(FDP2.FDR.Remarks),
            nameof(FDP2.FDR.GlobalOpData),
            nameof(FDP2.FDR.CFLUpper),
            nameof(FDP2.FDR.CFLLower),
            nameof(FDP2.FDR.RVSM),
            nameof(FDP2.FDR.IsTrackedByMe),
            nameof(FDP2.FDR.IsHandoff),
            nameof(FDP2.FDR.HandoffController),
            nameof(FDP2.FDR.ParsedRoute)
        ];

        if (!relevantPropertyNames.Contains(e.PropertyName))
            return;
        
        _workQueue.Enqueue(async () =>
        {
            try
            {
                SyncHold(fdr);
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
            }
        });
    }

    /// <summary>
    ///     Initiate, update, or cancel the hold for the FDR, based on its remarks and label data.
    ///     If the remarks and the label data do not agree, use the remarks.
    /// </summary>
    void SyncHold(FDP2.FDR fdr)
    {
        var existingHold = _holdRepository.Find(fdr.Callsign);

        var delayRemarks = DelayRemarks.Parse(fdr);
        _holdRepository.ForgetCompletedHolds(fdr.Callsign, delayRemarks.Select(d => d.WaypointName));

        var holdPointPrefix = string.Empty;
        int? exitTimeMinutes = null;
        TimeSpan? delay = null;
        bool hasHold;
        if (TryFindActiveDelay(fdr, delayRemarks, out var activeDelay, out var delayWaypoint))
        {
            hasHold = true;
            holdPointPrefix = delayWaypoint.Intersection.Name;
            delay = activeDelay.Delay;
        }
        else
        {
            hasHold = TryParseHoldAnnotation(fdr, out holdPointPrefix, out exitTimeMinutes) &&
                !HasDelayAtHoldPoint(fdr, delayRemarks, holdPointPrefix);
        }

        DateTime? holdExitTime = exitTimeMinutes.HasValue ? CalculateExitTime(exitTimeMinutes.Value) : null;

        if (!hasHold)
        {
            if (existingHold is not null)
                RemoveHold(fdr, existingHold);
        }
        else if (existingHold is null)
        {
            InitiateHold(fdr, holdPointPrefix, holdExitTime, delay);
        }
        else if (fdr.FindHoldWaypoint(holdPointPrefix) is { } holdSegment && holdSegment.Intersection.Name != existingHold.HoldPoint)
        {
            // The hold point changed, e.g. another controller changed the delay in the remarks.
            RemoveHold(fdr, existingHold);
            InitiateHold(fdr, holdPointPrefix, holdExitTime, delay);
        }
        else
        {
            UpdateHold(fdr, existingHold, exitTimeMinutes, delay);
        }

        WeakReferenceMessenger.Default.Send(new RefreshHoldsCommand());
    }

    /// <summary>
    ///     Find the delay in the remarks at the next waypoint. A delay is active only when its
    ///     waypoint is the next waypoint, or the current waypoint in a hold (SetOverflownIndexPatch
    ///     keeps the hold point as the next waypoint). The first delay along the route from the
    ///     present position is then always at the next waypoint. Ignore the delays at hold points
    ///     that the aircraft left.
    /// </summary>
    bool TryFindActiveDelay(
        FDP2.FDR fdr,
        DelayRemark[] delayRemarks,
        out DelayRemark activeDelay,
        out FDP2.FDR.ExtractedRoute.Segment delayWaypoint)
    {
        activeDelay = null!;
        delayWaypoint = null!;

        var nextWaypoint = fdr.ParsedRoute.ToList()
            .Skip(Math.Max(fdr.ParsedRoute.OverflownIndex + 1, 0))
            .FirstOrDefault(s => s.Type == FDP2.FDR.ExtractedRoute.Segment.SegmentTypes.WAYPOINT);
        if (nextWaypoint is null)
            return false;

        var nextWaypointName = nextWaypoint.GetFlightPlanName();
        var delayRemark = delayRemarks.FirstOrDefault(d => d.WaypointName == nextWaypointName);
        if (delayRemark is null || _holdRepository.IsCompleted(fdr.Callsign, delayRemark.WaypointName))
            return false;

        activeDelay = delayRemark;
        delayWaypoint = nextWaypoint;
        return true;
    }

    // When the remarks have a delay at the hold point of the label data, the remarks win, also
    // when the delay is not active.
    static bool HasDelayAtHoldPoint(FDP2.FDR fdr, DelayRemark[] delayRemarks, string holdPointPrefix)
    {
        var holdSegment = fdr.FindHoldWaypoint(holdPointPrefix);
        return holdSegment is not null && delayRemarks.Any(d => d.WaypointName == holdSegment.GetFlightPlanName());
    }

    void InitiateHold(FDP2.FDR fdr, string holdPointPrefix, DateTime? holdExitTime, TimeSpan? delay)
    {
        var holdSegment = fdr.FindHoldWaypoint(holdPointPrefix);
        if (holdSegment is null)
            return;

        if (holdExitTime is null)
        {
            // vatSys sets the ETO to DateTime.MaxValue when it has no estimate. With a delay,
            // CalculateETOsPatch calculates the exit time again from the arrival time.
            var holdPointEto = holdSegment.ETO == DateTime.MaxValue ? DateTime.UtcNow : holdSegment.ETO;
            holdExitTime = holdPointEto + (delay ?? DefaultHoldDuration);
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
            GetHoldEntryTime(holdSegment),
            holdExitTime.Value,
            isDesignated,
            level,
            clearedFlightLevel,
            fdr.RVSM,
            fdr.GlobalOpData,
            state,
            delay);

        _holdRepository.Add(holdItem);

        if (state != HoldItemState.Unconcerned)
            AllocateSlot(holdItem.HoldPoint);

        UpdateEstimates(fdr);

        EnsureHoldWindowsAreOpen();
    }

    void UpdateHold(FDP2.FDR fdr, HoldItem hold, int? exitTimeMinutes, TimeSpan? delay)
    {
        IClearedFlightLevel clearedFlightLevel = fdr.CFLLower <= 0
            ? new ClearedFlightLevel(fdr.CFLUpper)
            : new ClearedBlockLevel(fdr.CFLLower, fdr.CFLUpper);

        var state = GetHoldItemState(fdr);

        // In a hold, vatSys does not set the ATO of the hold point, and HoldPatches records the entry time.
        // The hold point has an ATO only when the hold started after the aircraft crossed the hold point.
        var holdSegment = fdr.FindHoldWaypoint(hold.HoldPoint);
        var entryTime = holdSegment is not null ? GetHoldEntryTime(holdSegment) : null;

        // Keep the existing exit time while the minutes do not change. Otherwise, the exit time
        // moves to the next hour when it is in the past. With a delay, CalculateETOsPatch
        // calculates the exit time.
        var exitTimeChanged = delay is null && exitTimeMinutes.HasValue && hold.HoldExitTime.Minute != exitTimeMinutes.Value;
        var exitTime = exitTimeChanged ? CalculateExitTime(exitTimeMinutes!.Value) : hold.HoldExitTime;
        var delayChanged = delay != hold.Delay;

        _holdRepository.Update(hold.Callsign, h => h with
        {
            ClearedFlightLevel = clearedFlightLevel,
            RvsmApproved = fdr.RVSM,
            GlobalOps = fdr.GlobalOpData,
            State = state,
            HoldEntryTime = entryTime ?? h.HoldEntryTime,
            HoldExitTime = exitTime,
            Delay = delay
        });

        if (exitTimeChanged || delayChanged)
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
    ///     Stop the hold in the plugin. Do not change the remarks or the label data.
    /// </summary>
    void RemoveHold(FDP2.FDR? fdr, HoldItem hold)
    {
        if (!_holdRepository.TryRemove(hold.Callsign))
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
    ///     Remove the delay at the hold point from the remarks and the hold from the label data,
    ///     then stop the hold. Without permission, the plugin cannot remove the delay from the
    ///     remarks, and the delay would initiate the hold again. Then do nothing.
    /// </summary>
    void CancelHold(FDP2.FDR? fdr, HoldItem hold)
    {
        if (fdr is not null)
        {
            var holdPointName = fdr.FindHoldWaypoint(hold.HoldPoint)?.GetFlightPlanName() ?? hold.HoldPoint;
            if (DelayRemarks.HasDelay(fdr, holdPointName))
            {
                if (!fdr.HavePermission)
                    return;

                DelayRemarks.Remove(fdr, holdPointName);
            }

            RemoveHoldFromLabel(fdr);
        }

        RemoveHold(fdr, hold);
    }

    /// <summary>
    ///     Set the delay at the waypoint in the remarks, then initiate or update the hold from it.
    ///     <paramref name="waypointName"/> is the name in the flight plan route. Do nothing when
    ///     the controller does not have permission to change the flight plan.
    /// </summary>
    void SetDelay(FDP2.FDR fdr, string waypointName, TimeSpan delay)
    {
        if (!fdr.HavePermission || delay <= TimeSpan.Zero)
            return;

        DelayRemarks.Set(fdr, waypointName, delay);
        SyncHold(fdr);
    }

    // vatSys sets the ATO to DateTime.MaxValue until the aircraft overflies the hold point.
    static DateTime? GetHoldEntryTime(FDP2.FDR.ExtractedRoute.Segment holdSegment)
    {
        return holdSegment.ATO != DateTime.MaxValue ? holdSegment.ATO : null;
    }

    static DateTime CalculateExitTime(int exitTimeMinutes)
    {
        // Calculate target exit time based on minutes past the hour
        var targetExitTime = new DateTime(
            DateTime.UtcNow.Year,
            DateTime.UtcNow.Month,
            DateTime.UtcNow.Day,
            DateTime.UtcNow.Hour,
            exitTimeMinutes,
            0);

        // If target time is before entry time, it's next hour
        if (targetExitTime <= DateTime.UtcNow)
        {
            targetExitTime = targetExitTime.AddHours(1);
        }

        return targetExitTime;
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

    void UpdateEstimates(FDP2.FDR fdr)
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
            // The hold started after the aircraft crossed the hold point. Remove the position reports
            // that vatSys made after the hold point.
            if (holdSegment.ATO != DateTime.MaxValue)
            {
                RemovePositionReportsAfter(fdr, holdSegment.ATO);
                holdSegment.ATONoEvent = DateTime.MaxValue;
            }

            var didEnterHold = hold.HoldEntryTime is not null;
            if (!TryAddPositionReportBeforeHoldPoint(fdr, holdSegment) && didEnterHold)
            {
                // There is no radar position. Thus the aircraft leaves the hold point now, if it got to the hold point.
                holdSegment.ATONoEvent = DateTime.UtcNow;
            }
        }

        FDP2.Process(fdr);

        // Remove the APR segments with an ATO after the given time.
        // Add the EET and the distance of each removed segment to the next segment, the same as vatSys does.
        static void RemovePositionReportsAfter(FDP2.FDR fdr, DateTime time)
        {
            foreach (var segment in fdr.ParsedRoute.ToList())
            {
                if (segment.Type != FDP2.FDR.ExtractedRoute.Segment.SegmentTypes.APRUPR)
                    continue;

                if (segment.ATO == DateTime.MaxValue || segment.ATO <= time)
                    continue;

                var index = fdr.ParsedRoute.IndexOf(segment);
                if (index < 1 || index > fdr.ParsedRoute.Count - 2)
                    continue;

                var nextSegment = fdr.ParsedRoute[index + 1];
                nextSegment.EET += segment.EET;
                nextSegment.Distance += segment.Distance;
                fdr.ParsedRoute.Remove(segment);
            }
        }

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
        if (_holdSlotRepository.TryAutoAllocateSlot(holdPoint))
            WeakReferenceMessenger.Default.Send(new HoldSlotsUpdatedCommand());
    }

    /// <summary>
    ///     Free the automatically allocated slot of the hold point, if no aircraft in the
    ///     handover or jurisdiction state holds at the hold point.
    /// </summary>
    void FreeSlotIfUnused(string holdPoint)
    {
        var isUsed = _holdRepository.GetHolds()
            .Any(h => h.HoldPoint == holdPoint && h.State != HoldItemState.Unconcerned);
        
        if (isUsed)
            return;

        if (!_holdSlotRepository.TryFreeAutoAllocatedSlot(holdPoint))
            return;

        _windowManager.CloseWindow(WindowKeys.HoldFor(holdPoint));
        WeakReferenceMessenger.Default.Send(new HoldSlotsUpdatedCommand());
    }

    public void Receive(DesignateAircraftCommand message)
    {
        var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == message.Callsign);
        if (fdr is null)
            return;

        var track = MMI.FindTrack(fdr);
        if (track is null)
            return;

        MMI.SelectOrDeselectTrack(track);
    }

    public void Receive(CancelHoldCommand message)
    {
        _workQueue.Enqueue(async () =>
        {
            var hold = _holdRepository.Find(message.Callsign);
            if (hold == null)
                return;

            var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == hold.Callsign);
            CancelHold(fdr, hold);

            WeakReferenceMessenger.Default.Send(new RefreshHoldsCommand());
        });
    }

    public void Receive(OpenClearedLevelMenuCommand message)
    {
        var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == message.Callsign);
        if (fdr is null)
            return;
        
        var track = MMI.FindTrack(fdr);
        if (track is null)
            return;
        
        MMI.OpenCFLMenu(track, Control.MousePosition);
    }

    public void Receive(OpenHoldExitMenuCommand message)
    {
        try
        {
            var holdItem = _holdRepository.Find(message.Callsign);
            if (holdItem is null)
                return;

            var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == message.Callsign);
            if (fdr is null || !fdr.HavePermission)
                return;

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
                    _workQueue.Enqueue(async () =>
                    {
                        // The remarks contain the delay, thus change the exit time to a delay
                        // from the arrival time.
                        var hold = _holdRepository.Find(holdItem.Callsign);
                        var holdSegment = hold is not null ? fdr.FindHoldWaypoint(hold.HoldPoint) : null;
                        if (hold is null || holdSegment is null ||
                            !fdr.TryGetHoldArrivalTime(holdSegment, hold.HoldEntryTime, out var arrivalTime))
                            return;

                        SetDelay(fdr, holdSegment.GetFlightPlanName(), selectedTime - arrivalTime);
                    });
                }
            }

            window.Dispose();
        }
        catch (Exception ex)
        {
            _errorReporter.ReportError(ex);
        }
    }

    /// <summary>
    ///     Remove the hold section (H/XXX or H/XXX/42) from the label data. Keep the other parts
    ///     of the label data. No-op when no hold section is present.
    /// </summary>
    void RemoveHoldFromLabel(FDP2.FDR fdr)
    {
        var parts = fdr.LabelOpData.Split(' ');
        var remainingParts = parts
            .Where(p => !p.StartsWith("H/") && !p.StartsWith("H\\"))
            .ToArray();

        if (remainingParts.Length == parts.Length)
            return;

        FDP2.SetLabelData(fdr, string.Join(" ", remainingParts));
    }

    public void Receive(ChangeGlobalOpsCommand message)
    {
        _workQueue.Enqueue(async () =>
        {
            var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == message.Callsign);
            if (fdr is null)
                return;

            FDP2.SetGlobalOps(fdr, message.GlobalOps);
        });
    }

    public void Receive(OpenDelayPopupCommand message)
    {
        _guiInvoker.InvokeOnGUI(mainForm =>
        {
            var viewModel = new DelayPopupViewModel(message.Callsign, message.WaypointName, message.Delay);
            var view = new DelayPopup(viewModel);
            var form = new PopupForm(view);

            view.CloseRequested += (_, _) => form.Close();
            form.Shown += (_, _) => view.FocusDelayDuration();

            MMI.EnsureWindowVisible(form);
            form.Show(mainForm);
        });
    }

    public void Receive(SetHoldDelayCommand message)
    {
        _workQueue.Enqueue(async () =>
        {
            try
            {
                var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == message.Callsign);
                if (fdr is null)
                    return;

                SetDelay(fdr, message.WaypointName, message.Delay);
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
            }
        });
    }

    /// <summary>
    ///     HoldPatches ended a hold, because the aircraft left the hold point.
    /// </summary>
    public void Receive(HoldCompletedCommand message)
    {
        _workQueue.Enqueue(async () =>
        {
            try
            {
                FreeSlotIfUnused(message.HoldPoint);
                WeakReferenceMessenger.Default.Send(new RefreshHoldsCommand());
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
            }
        });
    }

    /// <summary>
    ///     If the aircraft is in a hold, cancel the hold. Otherwise, initiate a hold with the
    ///     default delay at the waypoint near the position. If no waypoint ahead is near the
    ///     position, route the aircraft direct to the position, and hold there.
    /// </summary>
    public void Receive(HoldKeyPressedCommand message)
    {
        _workQueue.Enqueue(async () =>
        {
            try
            {
                var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == message.Callsign);
                if (fdr is null)
                    return;

                var hold = _holdRepository.Find(message.Callsign);
                if (hold is not null)
                {
                    CancelHold(fdr, hold);
                    WeakReferenceMessenger.Default.Send(new RefreshHoldsCommand());
                    return;
                }

                if (!fdr.HavePermission)
                    return;

                var holdPointName = FindWaypointAhead(fdr, message.Position, message.DistanceTolerance)?.GetFlightPlanName()
                    ?? RerouteDirectTo(fdr, message.Position, message.DistanceTolerance);
                if (holdPointName is null)
                    return;

                SetDelay(fdr, holdPointName, DefaultHoldDuration);
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
            }
        });
    }

    static FDP2.FDR.ExtractedRoute.Segment[] GetWaypointsAhead(FDP2.FDR fdr) =>
        fdr.ParsedRoute.ToList()
            .Skip(Math.Max(fdr.ParsedRoute.OverflownIndex + 1, 0))
            .Where(s => s.Type == FDP2.FDR.ExtractedRoute.Segment.SegmentTypes.WAYPOINT)
            .ToArray();

    // Find the nearest waypoint ahead of the aircraft that is in the distance tolerance of the position.
    static FDP2.FDR.ExtractedRoute.Segment? FindWaypointAhead(FDP2.FDR fdr, Coordinate position, double distanceTolerance) =>
        GetWaypointsAhead(fdr)
            .Select(s => (Segment: s, Distance: Conversions.CalculateDistance(s.Intersection.LatLong, position)))
            .Where(s => s.Distance < distanceTolerance)
            .OrderBy(s => s.Distance)
            .Select(s => s.Segment)
            .FirstOrDefault();

    /// <summary>
    ///     Route the aircraft direct to the position, then to the waypoint ahead that is nearest to
    ///     the position. This is the same as the graphical reroute tool, when the reroute starts at
    ///     the aircraft (see MMI.FinishGraphicReroute). Returns the name of the new waypoint at the
    ///     position in the flight plan route, or null if there is no waypoint ahead.
    /// </summary>
    static string? RerouteDirectTo(FDP2.FDR fdr, Coordinate position, double distanceTolerance)
    {
        var rejoinWaypoint = GetWaypointsAhead(fdr)
            .OrderBy(s => Conversions.CalculateDistance(s.Intersection.LatLong, position))
            .FirstOrDefault();
        if (rejoinWaypoint is null)
            return null;

        var newRoute = new FDP2.FDR.ExtractedRoute(fdr);

        var aircraftPosition = fdr.GetLocation();
        var startSegment = new FDP2.FDR.ExtractedRoute.Segment(newRoute)
        {
            Type = FDP2.FDR.ExtractedRoute.Segment.SegmentTypes.REROUTE,
            Intersection = new Airspace2.Intersection(Conversions.ConvertToStripLatLong(aircraftPosition), aircraftPosition)
        };
        startSegment.Intersection.FullName = Conversions.ConvertToFlightplanLatLong(aircraftPosition);
        startSegment.ATO = DateTime.UtcNow;
        startSegment.PCL = fdr.PRL != -1 || fdr.CFLUpper == -1
            ? (fdr.PRL == -1 ? fdr.RFL : fdr.PRL)
            : fdr.CFLUpper;
        newRoute.Add(startSegment);

        // vatSys uses a named waypoint near the position, if there is one.
        var holdIntersection = Airspace2.FindIntersection(position, distanceTolerance)
            ?? new Airspace2.Intersection(Conversions.ConvertToStripLatLong(position), position);
        var holdSegment = new FDP2.FDR.ExtractedRoute.Segment(newRoute)
        {
            Type = FDP2.FDR.ExtractedRoute.Segment.SegmentTypes.REROUTE,
            Intersection = holdIntersection
        };
        newRoute.Add(holdSegment);

        FDP2.ModifyRoute(fdr, newRoute, null, rejoinWaypoint);
        FlightPlanNetwork.SendFlightPlanChange(fdr);

        // Make a position report at the aircraft position. The hold point is then the next waypoint.
        var routeStartSegment = fdr.ParsedRoute.FirstOrDefault(s => s.Intersection.Name == startSegment.Intersection.Name);
        if (routeStartSegment is not null)
            fdr.TaskQueue.QueueTask(() => FDP2.PositionReport(fdr, routeStartSegment, DateTime.UtcNow, true));

        return holdSegment.GetFlightPlanName();
    }

    public void Receive(HoldPointAddedCommand message)
    {
        _workQueue.Enqueue(async () =>
        {
            _holdSlotRepository.TryAllocateSlot(message.Index, message.PointName);

            WeakReferenceMessenger.Default.Send(new RefreshHoldsCommand());

            // Check if there are any aircraft that should still be in OTHER
            var allocatedNames = GetAllocatedHoldPointNames(_holdSlotRepository.GetSlots());
            var otherHolds = _holdRepository.GetHolds()
                .Where(h => !allocatedNames.Contains(h.HoldPoint) && h.State != HoldItemState.Unconcerned)
                .ToArray();

            // Close OTHER window only if no aircraft should be in it
            if (!otherHolds.Any())
            {
                _windowManager.CloseWindow(WindowKeys.HoldOther());
            }

            EnsureHoldWindowsAreOpen();
        });
    }

    public void Receive(HoldPointRemovedCommand message)
    {
        _workQueue.Enqueue(async () =>
        {
            if (!_holdSlotRepository.TryFreeSlot(message.PointName))
                return;

            // Close the window for this hold point
            _windowManager.CloseWindow(WindowKeys.HoldFor(message.PointName));

            // Check if any aircraft were holding at this point
            var holdsAtRemovedPoint = _holdRepository.GetHolds().Where(h => h.HoldPoint == message.PointName).ToArray();

            // If aircraft exist, they need to be shown in OTHER window
            if (holdsAtRemovedPoint.Any())
            {
                WeakReferenceMessenger.Default.Send(new RefreshHoldsCommand());
                EnsureHoldWindowsAreOpen();
            }
        });
    }

    public void Receive(RefreshHoldsCommand message)
    {
        _workQueue.Enqueue(async () => EnsureHoldWindowsAreOpen());
    }
}

