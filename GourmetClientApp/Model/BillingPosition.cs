using System;

namespace GourmetClientApp.Model;

public record BillingPosition(
    DateTime Date,
    BillingPositionType PositionType,
    string PositionName,
    int Count,
    double SumCost);