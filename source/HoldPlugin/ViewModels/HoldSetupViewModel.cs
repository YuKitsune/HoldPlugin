using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HoldPlugin.Contracts;

namespace HoldPlugin.ViewModels;

public partial class HoldSetupViewModel : ObservableObject, IRecipient<HoldSlotsUpdatedCommand>
{
    readonly HoldSlotRepository _holdSlotRepository;
    readonly GuiInvoker _guiInvoker;
    readonly IErrorReporter _errorReporter;

    [ObservableProperty] HoldButtonViewModel _button1;
    [ObservableProperty] HoldButtonViewModel _button2;
    [ObservableProperty] HoldButtonViewModel _button3;
    [ObservableProperty] HoldButtonViewModel _button4;

    public HoldSetupViewModel(HoldSlotRepository holdSlotRepository, GuiInvoker guiInvoker, IErrorReporter errorReporter)
    {
        _holdSlotRepository = holdSlotRepository;
        _guiInvoker = guiInvoker;
        _errorReporter = errorReporter;

        var holds = _holdSlotRepository.GetSlots();

        Button1 = new HoldButtonViewModel(0, holds[0].GetHoldPointName() ?? string.Empty, this, _errorReporter);
        Button2 = new HoldButtonViewModel(1, holds[1].GetHoldPointName() ?? string.Empty, this, _errorReporter);
        Button3 = new HoldButtonViewModel(2, holds[2].GetHoldPointName() ?? string.Empty, this, _errorReporter);
        Button4 = new HoldButtonViewModel(3, holds[3].GetHoldPointName() ?? string.Empty, this, _errorReporter);

        WeakReferenceMessenger.Default.Register(this);
    }

    public void Receive(HoldSlotsUpdatedCommand message)
    {
        _guiInvoker.InvokeOnUiThread(_ =>
        {
            var descriptors = _holdSlotRepository.GetSlots();
            Button1.HoldPointName = descriptors[0].GetHoldPointName();
            Button2.HoldPointName = descriptors[1].GetHoldPointName();
            Button3.HoldPointName = descriptors[2].GetHoldPointName();
            Button4.HoldPointName = descriptors[3].GetHoldPointName();
        });
    }

    public bool IsHoldPointNameTaken(string pointName, int excludeIndex)
    {
        if (string.IsNullOrWhiteSpace(pointName))
            return false;

        var buttons = new[] { Button1, Button2, Button3, Button4 };
        for (int i = 0; i < buttons.Length; i++)
        {
            if (i != excludeIndex &&
                !string.IsNullOrWhiteSpace(buttons[i].HoldPointName) &&
                buttons[i].HoldPointName.Equals(pointName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public partial class HoldButtonViewModel : ObservableObject
    {
        readonly int _index;
        readonly HoldSetupViewModel _parent;
        readonly IErrorReporter _errorReporter;

        [ObservableProperty] string? _holdPointName;
        [ObservableProperty] bool _isEditing;
        [ObservableProperty] string _editValue = "";

        public HoldButtonViewModel(int index, string pointName, HoldSetupViewModel parent, IErrorReporter errorReporter)
        {
            _index = index;
            _parent = parent;
            _errorReporter = errorReporter;
            
            HoldPointName = pointName;
        }

        public string DisplayText => string.IsNullOrWhiteSpace(HoldPointName)
            ? "           "
            : HoldPointName;

        partial void OnHoldPointNameChanged(string? value) => OnPropertyChanged(nameof(DisplayText));

        [RelayCommand]
        void Edit()
        {
            EditValue = HoldPointName ?? "";
            IsEditing = true;
        }

        [RelayCommand]
        void CommitEdit()
        {
            if (!IsEditing)
                return;

            var trimmedValue = EditValue.Trim().ToUpperInvariant();

            // Check for duplicates
            if (!string.IsNullOrWhiteSpace(trimmedValue) &&
                _parent.IsHoldPointNameTaken(trimmedValue, _index))
            {
                _errorReporter.ReportError($"Hold Window for {trimmedValue} already exists");
                
                IsEditing = false;
                EditValue = "";
                return;
            }

            // If there was a previous hold point, delete it
            if (!string.IsNullOrWhiteSpace(HoldPointName))
            {
                WeakReferenceMessenger.Default.Send(new HoldPointRemovedCommand(HoldPointName));
            }

            // Update the hold point name
            var previousName = HoldPointName;
            HoldPointName = string.IsNullOrWhiteSpace(trimmedValue) ? null : trimmedValue;
            OnPropertyChanged(nameof(DisplayText));

            // If the new name is not empty, create the hold list
            if (!string.IsNullOrWhiteSpace(HoldPointName))
            {
                WeakReferenceMessenger.Default.Send(new HoldPointAddedCommand(_index, HoldPointName));
            }

            IsEditing = false;
            EditValue = "";
        }

        [RelayCommand]
        void CancelEdit()
        {
            IsEditing = false;
            EditValue = "";
        }
    }
}