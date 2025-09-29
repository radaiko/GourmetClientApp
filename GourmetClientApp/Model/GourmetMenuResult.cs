using System.Collections.Generic;

namespace GourmetClientApp.Model;

public record GourmetOrderedMenuResult(
    bool IsOrderChangeForTodayPossible,
    IReadOnlyCollection<GourmetOrderedMenu> OrderedMenus);