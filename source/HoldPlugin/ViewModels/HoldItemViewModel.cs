using System.Windows.Forms;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoldPlugin.Contracts;
using MediatR;
using vatSysToolbox;

namespace HoldPlugin.ViewModels;

public enum HoldItemState
{
    Unconcerned,
    Handover,
    Jurisdiction
}

public partial class HoldItemViewModel : ObservableObject
{
    readonly IMediator _mediator;
    readonly IErrorReporter _errorReporter;

    [ObservableProperty] bool _isDesignated = false;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DisplayAircraftId))] string _aircraftId = "ACIDDDDD";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DisplayLevel))] int _level = 0;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DisplayClearedLevel))] IClearedFlightLevel _clearedFlightLevel = new ClearedFlightLevel(0);
    [ObservableProperty] bool _rvsmApproved = false;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DisplayHoldEntryTime))] DateTime? _holdEntryTime;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DisplayHoldExitTime))] DateTime _holdExitTime;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DisplayGlobalOps))] string _globalOps = "";
    [ObservableProperty] HoldItemState _state = HoldItemState.Jurisdiction;
    [ObservableProperty] bool _isEditingGlobalOps = false;
    [ObservableProperty] string _globalOpsEditValue = "";

    public HoldItemViewModel(
        string aircraftId,
        int level,
        IClearedFlightLevel clearedFlightLevel,
        bool rvsmApproved,
        DateTime? holdEntryTime,
        DateTime holdExitTime,
        string globalOps,
        HoldItemState state,
        IMediator mediator,
        IErrorReporter errorReporter)
    {
        _mediator = mediator;
        _errorReporter = errorReporter;
        AircraftId = aircraftId;
        Level = level;
        ClearedFlightLevel = clearedFlightLevel;
        RvsmApproved = rvsmApproved;
        HoldEntryTime = holdEntryTime;
        HoldExitTime = holdExitTime;
        GlobalOps =  globalOps;
        State = state;
    }

    public HoldItemViewModel(HoldItem item, IMediator mediator, IErrorReporter errorReporter)
    {
        _mediator = mediator;
        _errorReporter = errorReporter;
        AircraftId = item.Callsign;
        IsDesignated = item.IsDesignated;
        Level = item.Level;
        ClearedFlightLevel = item.ClearedFlightLevel;
        RvsmApproved = item.RvsmApproved;
        HoldEntryTime = item.HoldEntryTime;
        HoldExitTime = item.HoldExitTime;
        GlobalOps =  item.GlobalOps;
        State = item.State;
    }
    
    public string DisplayAircraftId => AircraftId.PadRight(8);
    public string DisplayLevel => (Level / 100).ToString("000");

    public string DisplayClearedLevel
    {
        get
        {
            return ClearedFlightLevel switch
            {
                ClearedFlightLevel clearedFlightLevel => (clearedFlightLevel.Level / 100).ToString("000").PadRight(7),
                ClearedBlockLevel clearedBlockLevel => $"{clearedBlockLevel.LowerLevel / 100:000}B{clearedBlockLevel.UpperLevel / 100:000}",
                _ => throw new ArgumentOutOfRangeException(nameof(ClearedFlightLevel))
            };
        }
    }

    public string DisplayHoldEntryTime => HoldEntryTime?.ToString("HHmm") ?? "    ";

    public string DisplayHoldExitTime => HoldExitTime.ToString("HHmm");

    public string DisplayGlobalOps => GlobalOps.PadRight(15);

    [RelayCommand]
    async Task DesignateAircraft()
    {
        try
        {
            await _mediator.Send(new DesignateAircraftCommand(AircraftId));
        }
        catch (Exception ex)
        {
            _errorReporter.ReportError(ex);
        }
    }

    [RelayCommand]
    async Task CancelHold()
    {
        try
        {
            await _mediator.Send(new CancelHoldCommand(AircraftId));
        }
        catch (Exception ex)
        {
            _errorReporter.ReportError(ex);
        }
    }

    [RelayCommand]
    async Task OpenClearedLevelMenu()
    {
        try
        {
            await _mediator.Send(new OpenClearedLevelMenuCommand(AircraftId));
        }
        catch (Exception ex)
        {
            _errorReporter.ReportError(ex);
        }
    }

    [RelayCommand]
    async Task OpenHoldExitMenu()
    {
        try
        {
            await _mediator.Send(new OpenHoldExitMenuCommand(AircraftId));
        }
        catch (Exception ex)
        {
            _errorReporter.ReportError(ex);
        }
    }

    [RelayCommand]
    void EditGlobalOps()
    {
        GlobalOpsEditValue = GlobalOps;
        IsEditingGlobalOps = true;
    }

    [RelayCommand]
    async Task CommitGlobalOpsEdit()
    {
        if (IsEditingGlobalOps)
        {
            try
            {
                await _mediator.Send(new ChangeGlobalOpsCommand(AircraftId, GlobalOpsEditValue));
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
            }

            IsEditingGlobalOps = false;
        }
    }

    [RelayCommand]
    void CancelGlobalOpsEdit()
    {
        IsEditingGlobalOps = false;
        GlobalOpsEditValue = "";
    }

    public void Update(HoldItem holdItem)
    {
        IsDesignated = holdItem.IsDesignated;
        Level =  holdItem.Level;
        ClearedFlightLevel = holdItem.ClearedFlightLevel;
        RvsmApproved = holdItem.RvsmApproved;
        HoldEntryTime = holdItem.HoldEntryTime;
        HoldExitTime = holdItem.HoldExitTime;
        GlobalOps = holdItem.GlobalOps;
        State = holdItem.State;
    }
}