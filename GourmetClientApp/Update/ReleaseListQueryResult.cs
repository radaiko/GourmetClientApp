using System.Collections.Generic;

namespace GourmetClientApp.Update;

public record ReleaseListQueryResult(string ETagHeaderValue, bool IsWeakETag, IReadOnlyList<ReleaseDescription> Releases);