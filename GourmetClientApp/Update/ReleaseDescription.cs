using Semver;

namespace GourmetClientApp.Update;

public record ReleaseDescription(
    SemVersion Version,
    string UpdatePackageDownloadUrl,
    long UpdatePackageSize,
    string ChecksumDownloadUrl,
    long ChecksumSize);