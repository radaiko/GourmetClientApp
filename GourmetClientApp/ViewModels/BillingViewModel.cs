using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GourmetClientApp.Model;
using GourmetClientApp.Network;
using GourmetClientApp.Utils;

namespace GourmetClientApp.ViewModels;

public class BillingViewModel : ViewModelBase
{
    private const string MenuNameMenu1 = "MENÜ I";
    private const string MenuNameMenu2 = "MENÜ II";
    private const string MenuNameMenu3 = "MENÜ III";
    private const string MenuNameSoupAndSalad = "SUPPE & SALAT";

    private readonly BillingCacheService _billingCacheService;
    private readonly ObservableCollection<DateTime> _availableMonths;
    private readonly ObservableCollection<GroupedBillingPositionsViewModel> _menuBillingPositions;
    private readonly ObservableCollection<GroupedBillingPositionsViewModel> _drinkBillingPositions;
    private readonly ObservableCollection<GroupedBillingPositionsViewModel> _unknownBillingPositions;

    private DateTime _selectedMonth;
    private double _sumCostMenuBillingPositions;
    private double _sumCostDrinkBillingPositions;
    private double _sumCostUnknownBillingPositions;
    private bool _updating;
    private int _updateProgress;

    public BillingViewModel()
    {
        _billingCacheService = InstanceProvider.BillingCacheService;

        _availableMonths = [];
        _menuBillingPositions = [];
        _drinkBillingPositions = [];
        _unknownBillingPositions = [];
    }

    public IReadOnlyList<DateTime> AvailableMonths => _availableMonths;

    public IReadOnlyList<GroupedBillingPositionsViewModel> MenuBillingPositions => _menuBillingPositions;

    public IReadOnlyList<GroupedBillingPositionsViewModel> DrinkBillingPositions => _drinkBillingPositions;

    public IReadOnlyList<GroupedBillingPositionsViewModel> UnknownBillingPositions => _unknownBillingPositions;

    public DateTime SelectedMonth
    {
        get => _selectedMonth;
        set
        {
            if (_selectedMonth != value)
            {
                _selectedMonth = value;
                OnPropertyChanged();
                UpdateBillingPositions();
            }
        }
    }

    public double SumCostMenuBillingPositions
    {
        get => _sumCostMenuBillingPositions;
        private set
        {
            _sumCostMenuBillingPositions = value;
            OnPropertyChanged();
        }
    }

    public double SumCostDrinkBillingPositions
    {
        get => _sumCostDrinkBillingPositions;
        private set
        {
            _sumCostDrinkBillingPositions = value;
            OnPropertyChanged();
        }
    }

    public double SumCostUnknownBillingPositions
    {
        get => _sumCostUnknownBillingPositions;
        private set
        {
            _sumCostUnknownBillingPositions = value;
            OnPropertyChanged();
        }
    }

    public bool IsUpdating
    {
        get => _updating;
        private set
        {
            _updating = value;
            OnPropertyChanged();
        }
    }

    public int UpdateProgress
    {
        get => _updateProgress;
        private set
        {
            _updateProgress = value;
            OnPropertyChanged();
        }
    }

    public override void Initialize()
    {
        var currentMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

        _availableMonths.Clear();
        _availableMonths.Add(DateTime.MinValue);

        // The Gourmet website only provides information for the last three months.
        for (int i = 0; i <= 3; i++)
        {
            _availableMonths.Add(currentMonth.AddMonths(i * (-1)));
        }

        SelectedMonth = DateTime.MinValue;
    }

    public void OnActivated()
    {
    }

    private async void UpdateBillingPositions()
    {
        IsUpdating = true;
        UpdateProgress = 0;

        _menuBillingPositions.Clear();
        _drinkBillingPositions.Clear();
        _unknownBillingPositions.Clear();

        SumCostMenuBillingPositions = 0;
        SumCostDrinkBillingPositions = 0;
        SumCostUnknownBillingPositions = 0;

        if (_selectedMonth != DateTime.MinValue)
        {
            var progress = new Progress<int>();
            progress.ProgressChanged += OnUpdateProgressChanged;

            IReadOnlyCollection<BillingPosition> billingPositions;
            try
            {
                billingPositions = await _billingCacheService.GetBillingPositions(_selectedMonth.Month, _selectedMonth.Year, progress);
            }
            finally
            {
                progress.ProgressChanged -= OnUpdateProgressChanged;
            }

            BillingPosition[] menuBillingPositions = FindMenusBillingPositions(billingPositions).ToArray();
            BillingPosition[] remainingBillingPositions = billingPositions.Except(menuBillingPositions).ToArray();

            foreach (GroupedBillingPositionsViewModel viewModel in GroupMenusBillingPositions(menuBillingPositions))
            {
                _menuBillingPositions.Add(viewModel);
            }

            foreach (GroupedBillingPositionsViewModel viewModel in GroupBillingPositions(BillingPositionType.Menu, remainingBillingPositions))
            {
                _menuBillingPositions.Add(viewModel);
            }

            foreach (GroupedBillingPositionsViewModel viewModel in GroupBillingPositions(BillingPositionType.Drink, remainingBillingPositions))
            {
                _drinkBillingPositions.Add(viewModel);
            }

            foreach (GroupedBillingPositionsViewModel viewModel in GroupBillingPositions(BillingPositionType.Unknown, remainingBillingPositions))
            {
                _unknownBillingPositions.Add(viewModel);
            }
        }

        SumCostMenuBillingPositions = _menuBillingPositions.Sum(p => p.SumCost);
        SumCostDrinkBillingPositions = _drinkBillingPositions.Sum(p => p.SumCost);
        SumCostUnknownBillingPositions = _unknownBillingPositions.Sum(p => p.SumCost);

        UpdateProgress = 100;
        IsUpdating = false;
    }

    private IEnumerable<BillingPosition> FindMenusBillingPositions(IEnumerable<BillingPosition> billingPositions)
    {
        string[] menuNames = [MenuNameMenu1, MenuNameMenu2, MenuNameMenu3, MenuNameSoupAndSalad];

        foreach (var billingPosition in billingPositions.Where(p => p.PositionType == BillingPositionType.Menu))
        {
            if (menuNames.Contains(billingPosition.PositionName.ToUpperInvariant()))
            {
                yield return billingPosition;
            }
        }
    }

    private IEnumerable<GroupedBillingPositionsViewModel> GroupMenusBillingPositions(IReadOnlyCollection<BillingPosition> billingPositions)
    {
        IEnumerable<BillingPosition> menu1Positions = billingPositions.Where(p => p.PositionName == MenuNameMenu1);
        IEnumerable<BillingPosition> menu2Positions = billingPositions.Where(p => p.PositionName == MenuNameMenu2);
        IEnumerable<BillingPosition> menu3Positions = billingPositions.Where(p => p.PositionName == MenuNameMenu3);
        IEnumerable<BillingPosition> soupAndSaladPositions = billingPositions.Where(p => p.PositionName == MenuNameSoupAndSalad);

        var groupedPositions = new List<GroupedBillingPositionsViewModel>();

        groupedPositions.AddRange(GroupMenusBillingPositions(menu1Positions, "Menü 1"));
        groupedPositions.AddRange(GroupMenusBillingPositions(menu2Positions, "Menü 2"));
        groupedPositions.AddRange(GroupMenusBillingPositions(menu3Positions, "Menü 3"));
        groupedPositions.AddRange(GroupMenusBillingPositions(soupAndSaladPositions, "Suppe & Salat"));

        return groupedPositions;
    }

    private IEnumerable<GroupedBillingPositionsViewModel> GroupMenusBillingPositions(
        IEnumerable<BillingPosition> billingPositions,
        string groupName)
    {
        foreach (IGrouping<double, BillingPosition> singleCostGroup in billingPositions.GroupBy(position => position.SumCost / position.Count))
        {
            yield return new GroupedBillingPositionsViewModel(
                PositionType: BillingPositionType.Menu,
                PositionName: groupName,
                Count: singleCostGroup.Sum(p => p.Count),
                SingleCost: singleCostGroup.Key,
                SumCost: singleCostGroup.Sum(p => p.SumCost));
        }
    }

    private IEnumerable<GroupedBillingPositionsViewModel> GroupBillingPositions(
        BillingPositionType positionType,
        IReadOnlyCollection<BillingPosition> billingPositions)
    {
        IEnumerable<BillingPosition> filteredPositions = billingPositions.Where(p => p.PositionType == positionType);

        foreach (IGrouping<string, BillingPosition> nameGroup in filteredPositions.GroupBy(p => p.PositionName))
        {
            foreach (IGrouping<double, BillingPosition> singleCostGroup in nameGroup.GroupBy(position => position.SumCost / position.Count))
            {
                yield return new GroupedBillingPositionsViewModel(
                    PositionType: positionType,
                    PositionName: nameGroup.Key,
                    Count: singleCostGroup.Sum(p => p.Count),
                    SingleCost: singleCostGroup.Key,
                    SumCost: singleCostGroup.Sum(p => p.SumCost));
            }
        }
    }

    private void OnUpdateProgressChanged(object? sender, int e)
    {
        UpdateProgress = e;
    }
}