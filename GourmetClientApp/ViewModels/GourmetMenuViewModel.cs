using GourmetClientApp.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GourmetClientApp.ViewModels;

public class GourmetMenuViewModel : ObservableObject
{
    private readonly GourmetMenu _menu;

    private bool _isOrdered;
    private bool _isOrderApproved;
    private bool _isOrderCancelable;
    private GourmetMenuState _menuState;

    public GourmetMenuViewModel(GourmetMenu menu)
    {
        _menu = menu;
    }

    public string MenuName => _menu.MenuName;

    public string MenuDescription => _menu.Description;

    public char[] Allergens => _menu.Allergens;

    public bool IsAvailable => _menu.IsAvailable;

    public bool IsOrdered
    {
        get => _isOrdered;
        set
        {
            if (_isOrdered != value)
            {
                _isOrdered = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsOrderApproved
    {
        get => _isOrderApproved;
        set
        {
            if (_isOrderApproved != value)
            {
                _isOrderApproved = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsOrderCancelable
    {
        get => _isOrderCancelable;
        set
        {
            if (_isOrderCancelable != value)
            {
                _isOrderCancelable = value;
                OnPropertyChanged();
            }
        }
    }

    public GourmetMenuState MenuState
    {
        get => _menuState;
        set
        {
            if (_menuState != value)
            {
                _menuState = value;
                OnPropertyChanged();
            }
        }
    }

    public GourmetMenu GetModel()
    {
        return _menu;
    }
}