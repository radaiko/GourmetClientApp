using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using GourmetClient.Behaviors;
using GourmetClientApp.Model;
using GourmetClientApp.Network;
using GourmetClientApp.Notifications;
using GourmetClientApp.Settings;
using GourmetClientApp.Utils;

namespace GourmetClientApp.ViewModels;

public class MenuOrderViewModel : ViewModelBase
{
    private readonly GourmetCacheService _cacheService;
    private readonly GourmetSettingsService _settingsService;
    private readonly NotificationService _notificationService;

    private bool _showWelcomeMessage;
    private IReadOnlyList<GourmetMenuDayViewModel> _menuDays;
    private IReadOnlyList<GourmetMenuCategory> _menuCategories;
    private bool _isMenuUpdating;
    private string _nameOfUser;
    private DateTime _lastMenuUpdate;
    private bool _isSettingsPopupOpened;

    public MenuOrderViewModel()
    {
        _cacheService = InstanceProvider.GourmetCacheService;
        _settingsService = InstanceProvider.SettingsService;
        _notificationService = InstanceProvider.NotificationService;

        _menuDays = [];
        _menuCategories = [];
        _nameOfUser = string.Empty;

        UpdateMenuCommand = new AsyncDelegateCommand(ForceUpdateMenu, () => !IsMenuUpdating);
        ExecuteSelectedOrderCommand = new AsyncDelegateCommand(() => ExecuteWithUpdateOverlay(ExecuteSelectedOrder), () => !IsMenuUpdating);
        ToggleMenuOrderedMarkCommand = new AsyncDelegateCommand<GourmetMenuViewModel>(ToggleMenuOrderedMark, CanToggleMenuOrderedMark);
    }

    public ICommand UpdateMenuCommand { get; }

    public ICommand ExecuteSelectedOrderCommand { get; }

    public ICommand ToggleMenuOrderedMarkCommand { get; }

    public bool ShowWelcomeMessage
    {
        get => _showWelcomeMessage;
        private set
        {
            if (_showWelcomeMessage != value)
            {
                _showWelcomeMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public IReadOnlyList<GourmetMenuDayViewModel> MenuDays
    {
        get => _menuDays;
        private set
        {
            _menuDays = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<GourmetMenuCategory> MenuCategories
    {
        get => _menuCategories;
        private set
        {
            _menuCategories = value;
            OnPropertyChanged();
        }
    }

    public bool IsMenuUpdating
    {
        get => _isMenuUpdating;
        private set
        {
            if (_isMenuUpdating != value)
            {
                _isMenuUpdating = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string NameOfUser
    {
        get => _nameOfUser;
        private set
        {
            if (_nameOfUser != value)
            {
                _nameOfUser = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime LastMenuUpdate
    {
        get => _lastMenuUpdate;
        private set
        {
            if (_lastMenuUpdate != value)
            {
                _lastMenuUpdate = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsSettingsPopupOpened
    {
        get => _isSettingsPopupOpened;
        set
        {
            if (_isSettingsPopupOpened != value)
            {
                _isSettingsPopupOpened = value;
                OnPropertyChanged();
            }
        }
    }

    public override async void Initialize()
    {
        _settingsService.SettingsSaved += SettingsServiceOnSettingsSaved;

        UserSettings userSettings = _settingsService.GetCurrentUserSettings();

        if (!string.IsNullOrEmpty(userSettings.GourmetLoginUsername))
        {
            await ExecuteWithUpdateOverlay(UpdateMenu);
        }
        else
        {
            ShowWelcomeMessage = true;
        }
    }

    private async Task ForceUpdateMenu()
    {
        await ExecuteWithUpdateOverlay(async () =>
        {
            _cacheService.InvalidateCache();
            await UpdateMenu();
        });
    }

    private async Task UpdateMenu()
    {
        GourmetCache cache = await _cacheService.GetCache();

        LastMenuUpdate = cache.Timestamp;
        NameOfUser = cache.UserInformation.NameOfUser;

        int maxMenuCountPerDay = 0;
        var dayViewModels = new List<GourmetMenuDayViewModel>();

        if (cache.Menus.Count > 0)
        {
            IGrouping<DateTime, GourmetMenu>[] dayGroups = cache.Menus.GroupBy(menu => menu.Day).ToArray();
            maxMenuCountPerDay = dayGroups.Max(dayGroup => dayGroup.Count());

            foreach (IGrouping<DateTime, GourmetMenu> dayGroup in dayGroups)
            {
                DateTime day = dayGroup.Key;
                GourmetMenuViewModel? menu1ViewModel = null;
                GourmetMenuViewModel? menu2ViewModel = null;
                GourmetMenuViewModel? menu3ViewModel = null;
                GourmetMenuViewModel? menuSoupAndSaladViewModel = null;
                var additionalMenuViewModels = new List<GourmetMenuViewModel>();

                foreach (var menu in dayGroup.OrderBy(menu => menu.MenuName))
                {
                    var menuViewModel = new GourmetMenuViewModel(menu);
                    GourmetOrderedMenu? orderedMenu = cache.OrderedMenus.FirstOrDefault(orderedMenu => orderedMenu.MatchesMenu(menu));

                    if (orderedMenu is not null)
                    {
                        menuViewModel.MenuState = GourmetMenuState.Ordered;
                        menuViewModel.IsOrdered = true;
                        menuViewModel.IsOrderApproved = orderedMenu.IsOrderApproved;
                        menuViewModel.IsOrderCancelable = orderedMenu.IsOrderCancelable;
                    }
                    else if (!menu.IsAvailable)
                    {
                        menuViewModel.MenuState = GourmetMenuState.NotAvailable;
                    }

                    switch (menu.Category)
                    {
                        case GourmetMenuCategory.Menu1:
                            SetOrAddToAdditionalMenus(menuViewModel, ref menu1ViewModel, additionalMenuViewModels);
                            break;
                        case GourmetMenuCategory.Menu2:
                            SetOrAddToAdditionalMenus(menuViewModel, ref menu2ViewModel, additionalMenuViewModels);
                            break;
                        case GourmetMenuCategory.Menu3:
                            SetOrAddToAdditionalMenus(menuViewModel, ref menu3ViewModel, additionalMenuViewModels);
                            break;
                        case GourmetMenuCategory.SoupAndSalad:
                            SetOrAddToAdditionalMenus(menuViewModel, ref menuSoupAndSaladViewModel, additionalMenuViewModels);
                            break;
                        default:
                            additionalMenuViewModels.Add(menuViewModel);
                            break;
                    }
                }

                List<GourmetMenuViewModel?> menuViewModels = new[]
                {
                    menu1ViewModel,
                    menu2ViewModel,
                    menu3ViewModel,
                    menuSoupAndSaladViewModel
                }.Concat(additionalMenuViewModels).ToList();

                while (menuViewModels.Count < maxMenuCountPerDay)
                {
                    // Fill up the view models for each day to match the day with the maximum amount of menus
                    // This is so that all days in the UI have the same amount of columns
                    menuViewModels.Add(null);
                }

                dayViewModels.Add(new GourmetMenuDayViewModel(day, menuViewModels));
            }
        }

        var categories = new List<GourmetMenuCategory>
        {
            GourmetMenuCategory.Menu1,
            GourmetMenuCategory.Menu2,
            GourmetMenuCategory.Menu3,
            GourmetMenuCategory.SoupAndSalad
        };

        while (categories.Count < maxMenuCountPerDay)
        {
            // Add Unknown category for each additional menu so that the header row has the same amount of columns
            categories.Add(GourmetMenuCategory.Unknown);
        }

        MenuDays = dayViewModels.OrderBy(viewModel => viewModel.Date).ToArray();
        MenuCategories = categories;

        NotifyAboutConflictingOrderedMenus(cache.OrderedMenus);
    }

    private void SetOrAddToAdditionalMenus(
        GourmetMenuViewModel menuViewModel,
        ref GourmetMenuViewModel? categorizedMenuViewModel,
        List<GourmetMenuViewModel> additionalMenuViewModels)
    {
        if (categorizedMenuViewModel is null)
        {
            categorizedMenuViewModel = menuViewModel;
        }
        else
        {
            additionalMenuViewModels.Add(menuViewModel);
        }
    }

    private void NotifyAboutConflictingOrderedMenus(IReadOnlyCollection<GourmetOrderedMenu> orderedMenus)
    {
        IEnumerable<GourmetOrderedMenu> duplicateOrderedMenus = orderedMenus
            .GroupBy(orderedMenu => orderedMenu)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        IEnumerable<DateTime> daysWithMultipleOrderedMenus = orderedMenus
            .GroupBy(orderedMenu => orderedMenu.Day)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (GourmetOrderedMenu duplicateOrderedMenu in duplicateOrderedMenus)
        {
            _notificationService.Send(
                new Notification(
                    NotificationType.Warning,
                    $"Das Menü '{duplicateOrderedMenu.MenuName}' am {duplicateOrderedMenu.Day:dd.MM.yyyy} ist mehrfach bestellt"));
        }

        foreach (DateTime dayWithMultipleOrderedMenus in daysWithMultipleOrderedMenus)
        {
            _notificationService.Send(
                new Notification(
                    NotificationType.Warning,
                    $"Am {dayWithMultipleOrderedMenus:dd.MM.yyyy} sind mehrere Menüs bestellt"));
        }
    }

    private async Task ExecuteSelectedOrder()
    {
        _cacheService.InvalidateCache();
        GourmetCache currentData = await _cacheService.GetCache();

        var errorDays = new List<DateTime>();
        var menusToOrder = new List<GourmetMenu>();
        var menusToCancel = new List<GourmetOrderedMenu>();

        foreach (GourmetMenuDayViewModel dayViewModel in _menuDays)
        {
            GourmetMenuViewModel? menuToOrder = dayViewModel.Menus.FirstOrDefault(menu => menu?.MenuState == GourmetMenuState.MarkedForOrder);

            if (menuToOrder is not null)
            {
                GourmetMenu menuModel = menuToOrder.GetModel();
                GourmetMenu? actualMenu = currentData.Menus.FirstOrDefault(menu => menu.Equals(menuModel));

                if (actualMenu is { IsAvailable: true })
                {
                    menusToOrder.Add(menuToOrder.GetModel());
                }
                else
                {
                    errorDays.Add(dayViewModel.Date);
                    _notificationService.Send(
                        new Notification(
                            NotificationType.Error,
                            $"{menuToOrder.MenuName} für den {dayViewModel.Date:dd.MM.yyyy} ist nicht mehr verfügbar"));
                }
            }
        }

        foreach (GourmetMenuDayViewModel dayViewModel in _menuDays.Where(day => !errorDays.Contains(day.Date)))
        {
            foreach (GourmetMenuViewModel? menuViewModel in dayViewModel.Menus)
            {
                if (menuViewModel?.MenuState == GourmetMenuState.MarkedForCancel)
                {
                    GourmetMenu menuModel = menuViewModel.GetModel();
                    IEnumerable<GourmetOrderedMenu> matchingOrderedMenus = currentData
                        .OrderedMenus
                        .Where(orderedMenu => orderedMenu.MatchesMenu(menuModel));

                    int menusToCancelCount = menusToCancel.Count;

                    // Cancel all orders in case the menu has been ordered multiple times
                    foreach (GourmetOrderedMenu actualOrderedMenu in matchingOrderedMenus)
                    {
                        if (!actualOrderedMenu.IsOrderCancelable)
                        {
                            // Assume that in case of multiple orders of the same menu, if one of the order can't be cancelled, then none
                            // of the orders can be cancelled.
                            break;
                        }

                        menusToCancel.Add(actualOrderedMenu);
                    }

                    if (menusToCancelCount == menusToCancel.Count)
                    {
                        // Nothing was added
                        _notificationService.Send(
                            new Notification(
                                NotificationType.Error,
                                $"{menuViewModel.MenuName} für den {dayViewModel.Date:dd.MM.yyyy} kann nicht storniert werden"));
                    }
                }
            }
        }

        GourmetUpdateOrderResult updateOrderResult;
        try
        {
            updateOrderResult = await _cacheService.UpdateOrderedMenu(currentData.UserInformation, menusToOrder, menusToCancel);
        }
        catch (Exception exception) when (exception is GourmetRequestException || exception is GourmetParseException)
        {
            _notificationService.Send(new ExceptionNotification("Das Ausführen der Bestellung ist fehlgeschlagen", exception));
            return;
        }

        NotifyAboutFailedOrders(updateOrderResult);
        await UpdateMenu();
    }

    private void NotifyAboutFailedOrders(GourmetUpdateOrderResult updateOrderResult)
    {
        foreach (FailedMenuToOrderInformation information in updateOrderResult.FailedMenusToOrder)
        {
            _notificationService.Send(
                new Notification(
                    NotificationType.Warning,
                    $"Das Menü '{information.Menu.MenuName}' am {information.Menu.Day:dd.MM.yyyy} konnte nicht bestellt werden. Ursache: {information.Message}"));
        }
    }

    private async Task ExecuteWithUpdateOverlay(Func<Task> action)
    {
        IsMenuUpdating = true;

        try
        {
            await action.Invoke();
        }
        finally
        {
            IsMenuUpdating = false;
        }
    }

    private bool CanToggleMenuOrderedMark(GourmetMenuViewModel? menuViewModel)
    {
        if (menuViewModel is null)
        {
            return false;
        }

        if (!menuViewModel.IsAvailable)
        {
            if (menuViewModel is { IsOrdered: true, IsOrderCancelable: true })
            {
                // Menu can no longer be ordered, but it is ordered and the order can be canceled
                return true;
            }

            // Menu can no longer be ordered
            return false;
        }

        if (menuViewModel is { IsOrdered: true, IsOrderCancelable: false })
        {
            // Menu is ordered and the order cannot be canceled
            return false;
        }

        return true;
    }

    private Task ToggleMenuOrderedMark(GourmetMenuViewModel? menuViewModel)
    {
        if (menuViewModel is null)
        {
            return Task.CompletedTask;
        }

        if (menuViewModel.MenuState == GourmetMenuState.Ordered)
        {
            menuViewModel.MenuState = GourmetMenuState.MarkedForCancel;
        }
        else if (menuViewModel.MenuState == GourmetMenuState.MarkedForOrder)
        {
            menuViewModel.MenuState = GourmetMenuState.None;

            GourmetMenuViewModel? orderedMenu = GetDayViewModel(menuViewModel).Menus.FirstOrDefault(menu => menu?.IsOrdered ?? false);

            if (orderedMenu is not null)
            {
                orderedMenu.MenuState = GourmetMenuState.Ordered;
            }
        }
        else
        {
            GourmetMenuDayViewModel dayViewModel = GetDayViewModel(menuViewModel);

            foreach (var menuOfDay in GetMenusWhereOrderCanBeChanged(dayViewModel))
            {
                if (menuOfDay == menuViewModel)
                {
                    menuOfDay.MenuState = menuOfDay.IsOrdered ? GourmetMenuState.Ordered : GourmetMenuState.MarkedForOrder;
                }
                else
                {
                    menuOfDay.MenuState = menuOfDay.IsOrdered ? GourmetMenuState.MarkedForCancel : GourmetMenuState.None;
                }
            }
        }

        return Task.CompletedTask;
    }

    private IEnumerable<GourmetMenuViewModel> GetMenusWhereOrderCanBeChanged(GourmetMenuDayViewModel dayViewModel)
    {
        foreach (var menuViewModel in dayViewModel.Menus)
        {
            if (menuViewModel is not null
                && menuViewModel.MenuState != GourmetMenuState.NotAvailable
                && (!menuViewModel.IsOrdered || menuViewModel.IsOrderCancelable))
            {
                yield return menuViewModel;
            }
        }
    }

    private GourmetMenuDayViewModel GetDayViewModel(GourmetMenuViewModel menuViewModel)
    {
        return _menuDays.First(day => day.Menus.Contains(menuViewModel));
    }

    private async void SettingsServiceOnSettingsSaved(object? sender, EventArgs e)
    {
        IsSettingsPopupOpened = false;
        ShowWelcomeMessage = false;

        await ForceUpdateMenu();
    }
}