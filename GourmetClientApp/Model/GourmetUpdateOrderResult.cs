using System.Collections.Generic;

namespace GourmetClientApp.Model;

public record GourmetUpdateOrderResult(IReadOnlyCollection<FailedMenuToOrderInformation> FailedMenusToOrder);