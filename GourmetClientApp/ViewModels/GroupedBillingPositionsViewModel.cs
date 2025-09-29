using GourmetClientApp.Model;

namespace GourmetClientApp.ViewModels;

public record GroupedBillingPositionsViewModel(
    BillingPositionType PositionType,
    string PositionName,
    int Count,
    double SingleCost,
    double SumCost);