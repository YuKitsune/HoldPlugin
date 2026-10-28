using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Windows;
using System.Windows.Forms;
using CommunityToolkit.Mvvm.Messaging;
using HoldPlugin.Contracts;
using HoldPlugin.Controls;
using HoldPlugin.ViewModels;
using vatsys;
using vatsys.Plugin;
using Track = vatsys.Track;

namespace HoldPlugin;

// TODO: Add DLE detection, and Delay menu

// TODO: Inhibit STCA, RAM, DAIW, MSAW, and ETO alerts
// TODO: Smaller label when in the hold
// TODO: Clean up

[Export(typeof(IPlugin))]
public class Plugin
    : IPlugin,
    IRecipient<DesignateAircraftCommand>,
    IRecipient<CancelHoldCommand>,
    IRecipient<OpenClearedLevelMenuCommand>,
    IRecipient<OpenHoldExitMenuCommand>,
    IRecipient<ClearHoldExitTimeCommand>,
    IRecipient<ChangeGlobalOpsCommand>,
    IRecipient<RemoveHoldItemCommand>,
    IRecipient<HoldPointAddedCommand>,
    IRecipient<HoldPointRemovedCommand>,
    IRecipient<RefreshHoldsCommand>
{
    const string HoldIndicatorStripItemTypePrefix = "HoldPlugin_Indicator_";
    
#if DEBUG
    public const string Name = "Hold Plugin - Debug";
#else
    public const string Name = "Hold Plugin";
#endif

    static readonly Dictionary<string, DateTimeOffset> ErrorMessages = new();

    readonly WorkQueue _workQueue = new(AddError);
    readonly FdrSemaphoreProvider _semaphoreProvider = new();

    private static readonly IHoldPointDescriptor[] _holdLists =
    [
        new Unallocated(),
        new Unallocated(),
        new Unallocated(),
        new Unallocated()
    ];

    static readonly List<HoldItem> _activeHolds = [];
    readonly Dictionary<string, FDP2.FDR> _subscribedFDRs = new();

    public static IReadOnlyCollection<IHoldPointDescriptor> HoldLists => _holdLists;
    public static IReadOnlyCollection<HoldItem> ActiveHolds => _activeHolds.ToArray();

    readonly WindowManager _windowManager;

    string IPlugin.Name => Name;

    public Plugin()
    {
        System.Diagnostics.Debug.WriteLine($"Plugin {Name} created");
        
        var guiInvoker = new GuiInvoker(MMI.InvokeOnGUI);
        _windowManager = new WindowManager(guiInvoker);
        
        WeakReferenceMessenger.Default.Register<DesignateAircraftCommand>(this);
        WeakReferenceMessenger.Default.Register<CancelHoldCommand>(this);
        WeakReferenceMessenger.Default.Register<OpenClearedLevelMenuCommand>(this);
        WeakReferenceMessenger.Default.Register<OpenHoldExitMenuCommand>(this);
        WeakReferenceMessenger.Default.Register<ClearHoldExitTimeCommand>(this);
        WeakReferenceMessenger.Default.Register<ChangeGlobalOpsCommand>(this);
        WeakReferenceMessenger.Default.Register<RemoveHoldItemCommand>(this);
        WeakReferenceMessenger.Default.Register<HoldPointAddedCommand>(this);
        WeakReferenceMessenger.Default.Register<HoldPointRemovedCommand>(this);
        WeakReferenceMessenger.Default.Register<RefreshHoldsCommand>(this);

        MMI.SelectedTrackChanged += OnSelectedTrackChanged;
        FDP2.FDRsChanged += OnFDRsChanged;

        CreateMenuItems();
    }

    public static void AddError(Exception exception)
    {
        System.Diagnostics.Debug.WriteLine(exception);
        
        lock (ErrorMessages)
        {
            // Don't flood the error window with the same message over and over again
            if (ErrorMessages.TryGetValue(exception.Message, out var lastShown) &&
                DateTimeOffset.Now - lastShown <= TimeSpan.FromMinutes(1))
                return;

            Errors.Add(exception, Name);
            ErrorMessages[exception.Message] = DateTimeOffset.Now;
        }
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
            _ =>
            {
                var viewModel = new HoldSetupViewModel(_holdLists, new ErrorReporter());
                var view = new HoldSetup(viewModel);
                return view;
            },
            canMinimise: false);
    }

    void EnsureHoldWindowsAreOpen()
    {
        foreach (var descriptor in HoldLists)
        {
            var holdPointName = descriptor.GetHoldPointName();

            if (holdPointName is null)
                continue;

            var itemsToDisplay = ActiveHolds
                .Where(h => h.HoldPoint == holdPointName && h.State != HoldItemState.Unconcerned)
                .ToArray();

            if (itemsToDisplay.Any())
            {
                _windowManager.TryCreateWindow(
                    WindowKeys.HoldFor(holdPointName),
                    $"HOLD {holdPointName} WINDOW",
                    handle =>
                    {
                        var viewModel = new HoldListViewModel(holdPointName, itemsToDisplay, handle, new GuiInvoker(MMI.InvokeOnGUI));
                        var view = new HoldList(viewModel);
                        return view;
                    },
                    shrinkToContent: false,
                    canClose: false,
                    canMinimise: false,
                    new Size(480, 150));
            }
        }

        var allocatedNames = GetAllocatedHoldPointNames();
        var otherHolds = ActiveHolds
            .Where(h => !allocatedNames.Contains(h.HoldPoint) && h.State != HoldItemState.Unconcerned)
            .ToArray();
        if (otherHolds.Any())
        {
            _windowManager.TryCreateWindow(
                WindowKeys.HoldOther(),
                "HOLD OTHER WINDOW",
                handle =>
                {
                    var viewModel = new HoldListViewModel(string.Empty, otherHolds, handle, new GuiInvoker(MMI.InvokeOnGUI));
                    var view = new HoldList(viewModel);
                    return view;
                },
                shrinkToContent: false,
                canClose: false,
                canMinimise: false,
                new Size(480, 150));
        }
    }

    static HashSet<string> GetAllocatedHoldPointNames() =>
        _holdLists
            .Select(d => d.GetHoldPointName())
            .Where(n => n is not null)
            .ToHashSet()!;

    void TryAutoAllocateSlot(string holdPoint)
    {
        var alreadyAllocated = _holdLists.Any(d => d.Matches(holdPoint));

        if (alreadyAllocated)
            return;

        var emptySlotIndex = Array.FindIndex(_holdLists, d => d is Unallocated);
        if (emptySlotIndex == -1)
            return;

        _holdLists[emptySlotIndex] = new AutoAllocated(holdPoint);
        WeakReferenceMessenger.Default.Send(new HoldSlotsUpdatedCommand());
    }

    void TryFreeAutoAllocatedSlot(string holdPoint)
    {
        if (_activeHolds.Any(h => h.HoldPoint == holdPoint))
            return;

        var slotIndex = Array.FindIndex(_holdLists, d => d is AutoAllocated && d.Matches(holdPoint));
        if (slotIndex == -1)
            return;

        _holdLists[slotIndex] = new Unallocated();
        _windowManager.CloseWindow(WindowKeys.HoldFor(holdPoint));
        WeakReferenceMessenger.Default.Send(new HoldSlotsUpdatedCommand());
    }

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

                var holdItem = _activeHolds.FirstOrDefault(h => h.Callsign == updated.CoupledFDR.Callsign);
                if (holdItem is not null)
                {
                    holdItem.Level = updated.CorrectedAltitude;
                    WeakReferenceMessenger.Default.Send(new RefreshHoldsCommand());
                }
            }
            catch (Exception ex)
            {
                AddError(ex);
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
                foreach (var hold in _activeHolds.ToArray())
                {
                    hold.IsDesignated = hold.Callsign == selectedCallsign;
                }

                WeakReferenceMessenger.Default.Send(new RefreshHoldsCommand());
            }
            catch (Exception ex)
            {
                AddError(ex);
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
                AddError(ex);
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
            var existingHold = _activeHolds.FirstOrDefault(h => h.Callsign == fdr.Callsign);
            if (existingHold is not null)
            {
                UpdateHoldItemFromFDR(fdr, existingHold);
                continue;
            }

            if (GetHoldItemState(fdr) == HoldItemState.Unconcerned)
                continue;

            if (!TryParseHoldAnnotation(fdr, out var holdPointPrefix, out var exitTimeMinutes))
                continue;

            InitiateHold(fdr, holdPointPrefix, exitTimeMinutes);
        }

        WeakReferenceMessenger.Default.Send(new RefreshHoldsCommand());
    }

    /// <summary>
    ///     Initiate, adopt, or cancel holds based on label data and route state.
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
        
        Debug.WriteLine($"[HOLD{DateTime.UtcNow:HH:mm:ss}] Queued FDR Update for {fdr.Callsign}");
        
        _workQueue.Enqueue(async () =>
        {
            var semaphore = _semaphoreProvider.Get(fdr.Callsign);
            await semaphore.WaitAsync();

            Debug.WriteLine($"[HOLD {DateTime.UtcNow:HH:mm:ss}] Executing FDR Update for {fdr.Callsign}");

            try
            {
                var existingHold = _activeHolds.FirstOrDefault(h => h.Callsign == fdr.Callsign);

                if (existingHold != null)
                {
                    // Do not match the annotation to the route. The aircraft can pass waypoints
                    // after the hold point, and the hold must stay while the annotation stays.
                    var hasHoldAnnotation = TryParseHoldAnnotation(fdr, out _, out var exitTimeMinutes);

                    if (!hasHoldAnnotation)
                    {
                        // H/XXX removed from label data - cancel if we own the FDR
                        if (fdr.IsTrackedByMe)
                        {
                            CancelHold(existingHold);
                        }
                    }
                    else if (exitTimeMinutes.HasValue)
                    {
                        UpdateHold(existingHold, exitTimeMinutes.Value);
                    }
                    else
                    {
                        // Hold designator still present but exit-time segment gone; clear the
                        // in-memory exit time so the label stays the source of truth.
                        existingHold.HoldExitTime = null;
                    }

                    // Update FDR-derived properties
                    UpdateHoldItemFromFDR(fdr, existingHold);
                }
                else
                {
                    var hasHoldAnnotation = TryParseHoldAnnotation(fdr, out var holdPointPrefix, out var exitTimeMinutes);
                    if (hasHoldAnnotation && GetHoldItemState(fdr) != HoldItemState.Unconcerned)
                    {
                        InitiateHold(fdr, holdPointPrefix, exitTimeMinutes);
                    }
                }

                WeakReferenceMessenger.Default.Send(new RefreshHoldsCommand());
            }
            catch (Exception ex)
            {
                AddError(ex);
            }
            finally
            {
                semaphore.Release();
            }
        });
    }

    void InitiateHold(FDP2.FDR fdr, string holdPointPrefix, int? exitTimeMinutes)
    {
        var holdSegment = FindHoldWaypoint(fdr, holdPointPrefix);
        if (holdSegment is null)
            return;

        DateTime? holdExitTime = exitTimeMinutes.HasValue
            ? CalculateExitTime(exitTimeMinutes.Value)
            : null;

        CreateHoldItem(fdr, holdSegment, holdExitTime);
    }

    // Find the hold-point waypoint on the route. Search forward from the current waypoint first.
    // Then search backward from the current waypoint, because the aircraft can pass the hold
    // point (e.g. aircraft is established in the hold, or a handoff is accepted after
    // overflight). The backward search starts at the current waypoint, so that it does not match
    // a waypoint at the start of the route before a nearer one.
    static FDP2.FDR.ExtractedRoute.Segment? FindHoldWaypoint(FDP2.FDR fdr, string holdPointPrefix)
    {
        var route = fdr.ParsedRoute.ToList();
        var currentIndex = Math.Max(fdr.ParsedRoute.OverflownIndex, 0);

        var waypointsAhead = route
            .Skip(currentIndex)
            .Where(s => s.Type == FDP2.FDR.ExtractedRoute.Segment.SegmentTypes.WAYPOINT)
            .ToArray();

        var match = FindMatchingWaypoint(waypointsAhead, holdPointPrefix);
        if (match is not null)
            return match;

        var waypointsBehind = route
            .Take(currentIndex)
            .Where(s => s.Type == FDP2.FDR.ExtractedRoute.Segment.SegmentTypes.WAYPOINT)
            .Reverse()
            .ToArray();

        return FindMatchingWaypoint(waypointsBehind, holdPointPrefix);
    }

    // Find the first waypoint with an exact name match. If there is no exact match, find the
    // first waypoint that starts with the prefix (3 characters or more).
    static FDP2.FDR.ExtractedRoute.Segment? FindMatchingWaypoint(
        FDP2.FDR.ExtractedRoute.Segment[] waypoints,
        string holdPointPrefix)
    {
        var match = waypoints.FirstOrDefault(s => s.Intersection.Name == holdPointPrefix);

        if (match is null && holdPointPrefix.Length >= 3)
            match = waypoints.FirstOrDefault(s => s.Intersection.Name.StartsWith(holdPointPrefix));

        return match;
    }

    void UpdateHoldItemFromFDR(FDP2.FDR fdr, HoldItem hold)
    {
        IClearedFlightLevel clearedFlightLevel = fdr.CFLLower <= 0
            ? new ClearedFlightLevel(fdr.CFLUpper)
            : new ClearedBlockLevel(fdr.CFLLower, fdr.CFLUpper);

        hold.ClearedFlightLevel = clearedFlightLevel;
        hold.RvsmApproved = fdr.RVSM;
        hold.GlobalOps = fdr.GlobalOpData;
        hold.State = GetHoldItemState(fdr);

        // ATO is only populated once the aircraft overflies the hold point, which may happen
        // after the hold item was created. Re-read it so the entry time reflects the actual
        // overflight time once available.
        var holdSegment = FindHoldWaypoint(fdr, hold.HoldPoint);
        if (holdSegment is not null && holdSegment.ATO != default)
            hold.HoldEntryTime = holdSegment.ATO;
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

    DateTime CalculateExitTime(int exitTimeMinutes)
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

    HoldItem CreateHoldItem(
        FDP2.FDR fdr,
        FDP2.FDR.ExtractedRoute.Segment holdSegment,
        DateTime? holdExitTime)
    {
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
            holdSegment.ATO,
            holdExitTime,
            isDesignated,
            level,
            clearedFlightLevel,
            fdr.RVSM,
            fdr.GlobalOpData,
            state);

        TryAutoAllocateSlot(holdItem.HoldPoint);
        _activeHolds.Add(holdItem);
        EnsureHoldWindowsAreOpen();

        WeakReferenceMessenger.Default.Send(new RefreshHoldsCommand());

        return holdItem;
    }

    void UntrackHold(HoldItem hold)
    {
        _activeHolds.Remove(hold);
        TryFreeAutoAllocatedSlot(hold.HoldPoint);
        WeakReferenceMessenger.Default.Send(new RefreshHoldsCommand());
    }

    void UpdateHold(HoldItem hold, int exitTimeMinutes)
    {
        var exitTime = CalculateExitTime(exitTimeMinutes);
        if (hold.HoldExitTime == exitTime)
            return;

        hold.HoldExitTime = exitTime;

        Debug.WriteLine($"[HOLD DEBUG] Hold updated for {hold.Callsign}, new exit time: {exitTime}");
    }

    void CancelHold(HoldItem hold)
    {
        UntrackHold(hold);
        Debug.WriteLine($"[HOLD DEBUG] Hold cancelled for {hold.Callsign}");
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
            var hold = _activeHolds.FirstOrDefault(h => h.Callsign == message.Callsign);
            if (hold == null)
                return;

            var semaphore = _semaphoreProvider.Get(hold.Callsign);
            await semaphore.WaitAsync();

            try
            {
                CancelHold(hold);
                WeakReferenceMessenger.Default.Send(new RefreshHoldsCommand());
            }
            finally
            {
                semaphore.Release();
            }
        });
    }

    public void Receive(RemoveHoldItemCommand message)
    {
        _workQueue.Enqueue(async () =>
        {
            var hold = _activeHolds.FirstOrDefault(h => h.Callsign == message.Callsign);
            if (hold == null)
                return;

            var semaphore = _semaphoreProvider.Get(hold.Callsign);
            await semaphore.WaitAsync();

            try
            {
                UntrackHold(hold);
                WeakReferenceMessenger.Default.Send(new RefreshHoldsCommand());
            }
            finally
            {
                semaphore.Release();
            }
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
            var holdItem = ActiveHolds.FirstOrDefault(h => h.Callsign == message.Callsign);
            if (holdItem is null)
                return;

            var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == message.Callsign);
            if (fdr is null)
                return;

            var seedTime = holdItem.HoldExitTime ?? DateTime.UtcNow.AddMinutes(10);

            // TimeMenu window = new TimeMenu(seedTime, TimeSelected: false);
            var window = Activator.CreateInstance(
                Type.GetType("vatsys.TimeMenu, vatsys"),
                [
                    seedTime, // Selected time
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
                    holdItem.HoldExitTime = selectedTime;

                    _workQueue.Enqueue(async () =>
                    {
                        var semaphore = _semaphoreProvider.Get(fdr.Callsign);
                        await semaphore.WaitAsync();

                        try
                        {
                            SetExitTimeOnLabel(fdr, selectedTime.Minute);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    WeakReferenceMessenger.Default.Send(new RefreshHoldsCommand());

                    Debug.WriteLine(
                        $"[HOLD DEBUG] Manual exit time update for {holdItem.Callsign}: {selectedTime}");
                }
            }

            window.Dispose();
        }
        catch (Exception ex)
        {
            AddError(ex);
        }
    }

    public void Receive(ClearHoldExitTimeCommand message)
    {
        var holdItem = ActiveHolds.FirstOrDefault(h => h.Callsign == message.Callsign);
        if (holdItem is null)
            return;

        var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == message.Callsign);
        if (fdr is null)
            return;

        holdItem.HoldExitTime = null;

        _workQueue.Enqueue(async () =>
        {
            var semaphore = _semaphoreProvider.Get(fdr.Callsign);
            await semaphore.WaitAsync();

            try
            {
                RemoveExitTimeFromLabel(fdr);
            }
            finally
            {
                semaphore.Release();
            }
        });

        WeakReferenceMessenger.Default.Send(new RefreshHoldsCommand());
    }

    /// <summary>
    ///     Remove the exit time segment from the hold section of the label data, leaving the
    ///     hold designation and waypoint name (including any shortening) and separators intact.
    ///     No-op when no exit time segment is present.
    /// </summary>
    void RemoveExitTimeFromLabel(FDP2.FDR fdr)
    {
        var parts = fdr.LabelOpData.Split(' ');
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (!part.StartsWith("H/") && !part.StartsWith("H\\"))
                continue;

            // Separator positions within the hold section ('/' or '\')
            var separatorIndices = new List<int>();
            for (var c = 0; c < part.Length; c++)
            {
                if (part[c] == '/' || part[c] == '\\')
                    separatorIndices.Add(c);
            }

            // Only strip when an exit time segment exists (H/RIVET/42)
            if (separatorIndices.Count < 2)
                return;

            // Drop the 2nd separator and everything after it (the exit time)
            parts[i] = part.Substring(0, separatorIndices[1]);
            fdr.LabelOpData = string.Join(" ", parts);
            return;
        }
    }

    /// <summary>
    ///     Write the exit time (minutes past the hour) into the hold section of the label,
    ///     replacing an existing exit-time segment or appending one when absent. Reuses the
    ///     separator style already present in the hold section.
    /// </summary>
    void SetExitTimeOnLabel(FDP2.FDR fdr, int minutes)
    {
        var parts = fdr.LabelOpData.Split(' ');
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (!part.StartsWith("H/") && !part.StartsWith("H\\"))
                continue;

            var separatorIndices = new List<int>();
            for (var c = 0; c < part.Length; c++)
            {
                if (part[c] == '/' || part[c] == '\\')
                    separatorIndices.Add(c);
            }

            if (separatorIndices.Count == 0)
                return;

            var minutesText = minutes.ToString();

            if (separatorIndices.Count >= 2)
            {
                // Replace everything after the 2nd separator with the new minutes.
                parts[i] = part.Substring(0, separatorIndices[1] + 1) + minutesText;
            }
            else
            {
                // Append using the same separator character that follows the H.
                parts[i] = part + part[separatorIndices[0]] + minutesText;
            }

            fdr.LabelOpData = string.Join(" ", parts);
            return;
        }
    }

    public void Receive(ChangeGlobalOpsCommand message)
    {
        _workQueue.Enqueue(async () =>
        {
            var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == message.Callsign);
            if (fdr is null)
                return;

            var semaphore = _semaphoreProvider.Get(fdr.Callsign);
            await semaphore.WaitAsync();

            try
            {
                fdr.GlobalOpData = message.GlobalOps;
            }
            finally
            {
                semaphore.Release();
            }
        });
    }

    public void Receive(HoldPointAddedCommand message)
    {
        var alreadyAllocated = _holdLists.Any(d => d.Matches(message.PointName));

        if (!alreadyAllocated)
        {
            _holdLists[message.Index] = new ManuallyAllocated(message.PointName);
        }

        WeakReferenceMessenger.Default.Send(new RefreshHoldsCommand());

        // Check if there are any aircraft that should still be in OTHER
        var otherHolds = _activeHolds
            .Where(h => !GetAllocatedHoldPointNames().Contains(h.HoldPoint) && h.State != HoldItemState.Unconcerned)
            .ToArray();

        // Close OTHER window only if no aircraft should be in it
        if (!otherHolds.Any())
        {
            _windowManager.CloseWindow(WindowKeys.HoldOther());
        }

        EnsureHoldWindowsAreOpen();
    }

    public void Receive(HoldPointRemovedCommand message)
    {
        var index = Array.FindIndex(_holdLists, d => d.Matches(message.PointName));
        if (index == -1)
            return;

        _holdLists[index] = new Unallocated();

        // Close the window for this hold point
        _windowManager.CloseWindow(WindowKeys.HoldFor(message.PointName));

        // Check if any aircraft were holding at this point
        var holdsAtRemovedPoint = _activeHolds.Where(h => h.HoldPoint == message.PointName).ToArray();

        // If aircraft exist, they need to be shown in OTHER window
        if (holdsAtRemovedPoint.Any())
        {
            WeakReferenceMessenger.Default.Send(new RefreshHoldsCommand());
            EnsureHoldWindowsAreOpen();
        }
    }

    public void Receive(RefreshHoldsCommand message)
    {
        EnsureHoldWindowsAreOpen();
    }
}

