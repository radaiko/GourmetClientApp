using System;
using System.Collections.Generic;

namespace GourmetClientApp.Model;

public record GourmetCache(
    DateTime Timestamp,
    GourmetUserInformation UserInformation,
    IReadOnlyCollection<GourmetMenu> Menus,
    IReadOnlyCollection<GourmetOrderedMenu> OrderedMenus);

public record InvalidatedGourmetCache()
    : GourmetCache(
        Timestamp: DateTime.MinValue,
        UserInformation: new GourmetUserInformation(
            NameOfUser: string.Empty,
            ShopModelId: string.Empty,
            EaterId: string.Empty,
            StaffGroupId: string.Empty),
        Menus: [],
        OrderedMenus: []);