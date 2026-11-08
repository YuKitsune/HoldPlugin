using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using HoldPlugin.Contracts;

namespace HoldPlugin.ViewModels;

public partial class HoldListViewModel : ObservableObject, IRecipient<RefreshHoldsCommand>
{
    readonly HoldRepository _holdRepository;
    readonly IWindowHandle _windowHandle;
    readonly GuiInvoker _guiInvoker;
    
    [ObservableProperty] string _holdPointName = "";
    [ObservableProperty] ObservableCollection<HoldItemViewModel> _items = [];
    
#if DEBUG
    // Designer ctor
    public HoldListViewModel()
    {
        _holdRepository = null!;
        _guiInvoker = null!;
        _windowHandle = null!;
        
        Items =
        [
            new HoldItemViewModel(
                "QFA1",
                38000,
                new ClearedFlightLevel(38000),
                rvsmApproved: true,
                DateTime.MaxValue,
                DateTime.Now.AddMinutes(30),
                "H/RIV",
                HoldItemState.Jurisdiction)
            {
                IsDesignated = true
            },
            new HoldItemViewModel(
                "QFA2",
                28000,
                new ClearedBlockLevel(25000, 28000),
                rvsmApproved: false,
                DateTime.Now.AddMinutes(20),
                DateTime.Now.AddMinutes(30),
                "H/TAR",
                HoldItemState.Handover)
        ];
    }
#endif

    public HoldListViewModel(string pointName, HoldItem[] items, HoldRepository holdRepository, IWindowHandle windowHandle, GuiInvoker guiInvoker)
    {
        _holdRepository = holdRepository;
        _windowHandle = windowHandle;
        _guiInvoker = guiInvoker;

        HoldPointName = pointName;
        UpdateItems(items);

        WeakReferenceMessenger.Default.Register(this);
    }

    public void Receive(RefreshHoldsCommand message)
    {
        _guiInvoker.InvokeOnUiThread(_ => Refresh(_holdRepository.GetHolds()));
    }

    // The holds can change between the time the window is requested and the time it is shown.
    public void OnViewLoaded()
    {
        Refresh(_holdRepository.GetHolds());
    }

    void Refresh(IEnumerable<HoldItem> holdItems)
    {
        UpdateItems(holdItems);

        if (Items.Count == 0)
            _windowHandle.Close();
    }

    void UpdateItems(IEnumerable<HoldItem> holdItems)
    {
        var slots = _holdRepository.GetSlots();

        var relevantHoldItems = holdItems
            .Where(ShouldDisplay)
            .OrderByDescending(LowestLevel)
            .ToArray();

        var callsigns = Items.Select(x => x.AircraftId).ToList();
        
        foreach (var relevantHoldItem in relevantHoldItems)
        {
            var viewModel = Items.FirstOrDefault(vm => vm.AircraftId == relevantHoldItem.Callsign);
            if (viewModel is not null)
            {
                viewModel.Update(relevantHoldItem);
            }
            else
            {
                Items.Add(new HoldItemViewModel(relevantHoldItem));
            }

            callsigns.Remove(relevantHoldItem.Callsign);
        }
        
        foreach (var callsignToRemove in callsigns)
        {
            var vm = Items.FirstOrDefault(x => x.AircraftId ==  callsignToRemove);
            if (vm is not null)
                Items.Remove(vm);
        }

        for (var i = 0; i < relevantHoldItems.Length; i++)
        {
            var currentIndex = Items.IndexOf(Items.First(x => x.AircraftId == relevantHoldItems[i].Callsign));
            if (currentIndex != i)
                Items.Move(currentIndex, i);
        }

        return;

        bool ShouldDisplay(HoldItem item)
        {
            if (item.State == HoldItemState.Unconcerned)
                return false;
            
            if (string.IsNullOrEmpty(HoldPointName))
                return !slots.Any(d => d.Matches(item.HoldPoint));

            return HoldPointName == item.HoldPoint;
        }

        int LowestLevel(HoldItem item)
        {
            return item.ClearedFlightLevel switch
            {
                ClearedFlightLevel clearedFlightLevel => clearedFlightLevel.Level,
                ClearedBlockLevel clearedBlockLevel => clearedBlockLevel.LowerLevel,
                _ => throw new ArgumentOutOfRangeException()
            };
        }
    }
}