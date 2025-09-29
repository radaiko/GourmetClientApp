using System.Collections.Generic;

namespace GourmetClientApp.Model;

public record GourmetMenuResult(GourmetUserInformation UserInformation, IReadOnlyCollection<GourmetMenu> Menus);