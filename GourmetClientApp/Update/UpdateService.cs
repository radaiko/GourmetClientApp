using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GourmetClient.Serialization;
using GourmetClientApp.Update.GitHubApi;
using GourmetClient.Utils;
using Semver;

namespace GourmetClientApp.Update;

public class UpdateService
{
    private const string ReleaseListUri = "https://api.github.com/repos/patrickl92/GourmetClient/releases";

    private readonly string _releaseListQueryResultFilePath;
    private readonly SemaphoreSlim _availableReleasesSemaphore;

    private IReadOnlyList<ReleaseDescription>? _availableReleases;

    public UpdateService()
    {
        AssemblyInformationalVersionAttribute? assemblyInformationalVersionAttribute =
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        Debug.Assert(assemblyInformationalVersionAttribute is not null);
        CurrentVersion = SemVersion.Parse(assemblyInformationalVersionAttribute.InformationalVersion, SemVersionStyles.Strict);

        _releaseListQueryResultFilePath = Path.Combine(App.LocalAppDataPath, "ReleaseListQueryResult.json");
        _availableReleasesSemaphore = new SemaphoreSlim(1, 1);
    }

    public SemVersion CurrentVersion { get; }

    public async Task<ReleaseDescription?> CheckForUpdate(bool acceptPreReleases)
    {
        ReleaseDescription? latestRelease = null;
        try
        {
            latestRelease = await GetLatestRelease(acceptPreReleases);
        }
        catch (GourmetUpdateException)
        {
            // Ignore if the update check failed. The check is executed again during the next start of the application.
        }

        if (latestRelease?.Version.CompareSortOrderTo(CurrentVersion) is > 0)
        {
            // Version of latest release is newer than current version.
            return latestRelease;
        }

        return null;
    }

    public async Task<bool> CanJoinNextPreReleaseVersion()
    {
        ReleaseDescription? latestRelease = await CheckForUpdate(true);
        return latestRelease is { Version.IsPrerelease: true } && !CurrentVersion.IsPrerelease;
    }

    public async Task<string> DownloadUpdatePackage(
        ReleaseDescription updateRelease,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        string tempFolderPath = GetTempUpdateFolderPath();
        string packagePath = GetLocalUpdatePackageFilePath();
        string signedChecksumFilePath = GetLocalSignedChecksumFilePath();

        try
        {
            Directory.CreateDirectory(tempFolderPath);
        }
        catch (IOException exception)
        {
            throw new GourmetUpdateException("Could not create temporary update directory", exception);
        }

        try
        {
            // No need to check if these files actually exists. 'File.Delete' does nothing if the file does not exist. However, it would
            // throw a DirectoryNotFoundException if the directory of the file does not exist. Therefore, the parent directory is created
            // beforehand.
            File.Delete(packagePath);
            File.Delete(signedChecksumFilePath);
        }
        catch (IOException exception)
        {
            throw new GourmetUpdateException("Could not delete local update package files", exception);
        }

        try
        {
            await using var packageFileStream = new FileStream(packagePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await using var checksumFileStream = new FileStream(signedChecksumFilePath, FileMode.Create, FileAccess.Write, FileShare.None);

            long totalBytesCount = updateRelease.UpdatePackageSize + updateRelease.ChecksumSize;
            long totalReadBytes = 0L;

            HttpClientResult<Stream> clientResult = await CreateHttpClient(
                updateRelease.UpdatePackageDownloadUrl,
                client => client.GetStreamAsync(updateRelease.UpdatePackageDownloadUrl, cancellationToken));

            using HttpClient client = clientResult.Client;

            await using Stream packageSourceStream = clientResult.ResponseResult;
            await DownloadFile(packageSourceStream, packageFileStream, totalBytesCount, totalReadBytes, progress, cancellationToken);
            totalReadBytes += updateRelease.UpdatePackageSize;

            await using Stream checksumSourceStream = await client.GetStreamAsync(updateRelease.ChecksumDownloadUrl, cancellationToken);
            await DownloadFile(checksumSourceStream, checksumFileStream, totalBytesCount, totalReadBytes, progress, cancellationToken);
        }
        catch (TaskCanceledException exception)
        {
            if (exception.InnerException is TimeoutException)
            {
                // Timeout occurred
                throw new GourmetUpdateException("Could not download update package files", exception);
            }

            // Exception was caused by the cancellation token, so rethrow it
            throw;
        }
        catch (Exception exception) when (exception is IOException || exception is HttpRequestException)
        {
            throw new GourmetUpdateException("Could not download update package files", exception);
        }

        await VerifyChecksum(packagePath, signedChecksumFilePath, cancellationToken);

        return packagePath;
    }

    public async Task<string> ExtractUpdatePackage(string packagePath, CancellationToken cancellationToken)
    {
        string tempFolderPath = GetTempUpdateFolderPath();
        string targetLocation = Path.Combine(tempFolderPath, "UpdatePackage");

        try
        {
            await Task.Run(() =>
            {
                if (Directory.Exists(targetLocation))
                {
                    Directory.Delete(targetLocation, true);
                }

                // Only ensure that the parent directory exists. The target location for extracting the ZIP file must not exist, otherwise
                // an exception is thrown.
                Directory.CreateDirectory(tempFolderPath);

                ZipFile.ExtractToDirectory(packagePath, targetLocation);
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException || exception is InvalidDataException)
        {
            throw new GourmetUpdateException("Could not extract update package", exception);
        }

        return targetLocation;
    }

    public bool StartUpdate(string updateLocation)
    {
        string gourmetClientExePath = Path.Combine(updateLocation, "GourmetClient.exe");

        if (File.Exists(gourmetClientExePath))
        {
            string assemblyDirectoryPath = GetEntryAssemblyDirectoryPath();
            Process.Start(gourmetClientExePath, $"/update \"{assemblyDirectoryPath}\"");

            Environment.Exit(0);
            return true;
        }

        return false;
    }

    public async Task RemovePreviousVersion(string path, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(() =>
            {
                foreach (var filePath in Directory.GetFiles(path))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Delete(filePath);
                }

                foreach (var directoryPath in Directory.GetDirectories(path))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Directory.Delete(directoryPath, true);
                }
            }, cancellationToken);
        }
        catch (IOException exception)
        {
            throw new GourmetUpdateException("Could not remove previous version", exception);
        }
    }

    public async Task CopyCurrentVersion(string targetPath, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            string assemblyDirectoryPath = GetEntryAssemblyDirectoryPath();
            await CopyDirectory(assemblyDirectoryPath, targetPath, cancellationToken);
        }
        catch (IOException exception)
        {
            throw new GourmetUpdateException("Could not create backup", exception);
        }
    }

    public async Task RemoveUpdateFiles(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(() =>
            {
                File.Delete(GetLocalUpdatePackageFilePath());
                File.Delete(GetLocalSignedChecksumFilePath());
            }, cancellationToken);
        }
        catch (IOException)
        {
            // Ignore errors at this stage of the update. The files will be removed by the next update.
        }
    }

    public bool StartNewVersion(string path)
    {
        string gourmetClientExePath = Path.Combine(path, "GourmetClient.exe");

        if (File.Exists(gourmetClientExePath))
        {
            Process.Start(gourmetClientExePath, "/force");

            Environment.Exit(0);
            return true;
        }

        return false;
    }

    private async Task DownloadFile(
        Stream sourceStream,
        Stream targetStream,
        long totalBytesCount,
        long totalReadBytesOffset,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        long totalReadBytes = totalReadBytesOffset;

        while (true)
        {
            int readBytes = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (readBytes == 0)
            {
                break;
            }

            await targetStream.WriteAsync(buffer, 0, readBytes, cancellationToken);

            totalReadBytes += readBytes;
            progress.Report((int)((totalReadBytes * 100) / totalBytesCount));
        }
    }

    private async Task<ReleaseDescription?> GetLatestRelease(bool acceptPreReleases)
    {
        IEnumerable<ReleaseDescription> releaseDescriptions = await GetAvailableReleases();

        if (!acceptPreReleases)
        {
            releaseDescriptions = releaseDescriptions.Where(description => !description.Version.IsPrerelease);
        }

        return releaseDescriptions.MaxBy(description => description.Version, SemVersion.SortOrderComparer);
    }

    private async Task<IReadOnlyList<ReleaseDescription>> GetAvailableReleases()
    {
        await _availableReleasesSemaphore.WaitAsync();

        try
        {
            // Only query the available releases once.
            return _availableReleases ??= await QueryAvailableReleases();
        }
        finally
        {
            _availableReleasesSemaphore.Release();
        }
    }

    private async Task<IReadOnlyList<ReleaseDescription>> QueryAvailableReleases()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseListUri);
        IReadOnlyList<ReleaseDescription> releaseDescriptions = [];

        ReleaseListQueryResult? cachedQueryResult = await GetCachedReleaseListQueryResult();
        if (cachedQueryResult is not null)
        {
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(cachedQueryResult.ETagHeaderValue, cachedQueryResult.IsWeakETag));
            releaseDescriptions = cachedQueryResult.Releases;
        }

        try
        {
            HttpClientResult<HttpResponseMessage> clientResult =
                await CreateHttpClient(request.RequestUri!.AbsoluteUri, client => client.SendAsync(request));

            // Not needed anymore.
            clientResult.Client.Dispose();

            using HttpResponseMessage response = clientResult.ResponseResult;

            if (response.StatusCode != HttpStatusCode.NotModified)
            {
                response.EnsureSuccessStatusCode();

                await using Stream contentStream = await response.Content.ReadAsStreamAsync();
                var releases = await JsonSerializer.DeserializeAsync<ReleaseEntry[]>(contentStream) ?? [];

                releaseDescriptions = ReleaseEntriesToDescriptions(releases);

                if (response.Headers.ETag is not null)
                {
                    var queryResult = new ReleaseListQueryResult(
                        response.Headers.ETag.Tag,
                        response.Headers.ETag.IsWeak,
                        releaseDescriptions);

                    await SaveReleaseListQueryResult(queryResult);
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException || exception is JsonException)
        {
            throw new GourmetUpdateException("Error while trying to receive the list of releases", exception);
        }

        return releaseDescriptions;
    }

    private Task<HttpClientResult<T>> CreateHttpClient<T>(string requestUrl, Func<HttpClient, Task<T>> proxyTestRequestFunc)
    {
        return HttpClientHelper.CreateHttpClient(requestUrl, ExecuteProxyTestRequest, new CookieContainer());

        Task<T> ExecuteProxyTestRequest(HttpClient client)
        {
            // Github requires an user agent
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GourmetClient", CurrentVersion.ToString()));
            return proxyTestRequestFunc(client);
        }
    }

    private async Task<ReleaseListQueryResult?> GetCachedReleaseListQueryResult()
    {
        if (File.Exists(_releaseListQueryResultFilePath))
        {
            try
            {
                await using var fileStream = new FileStream(_releaseListQueryResultFilePath, FileMode.Open, FileAccess.Read, FileShare.None);
                var serializedResult = await JsonSerializer.DeserializeAsync<SerializableReleaseListQueryResult>(fileStream);

                return serializedResult?.ToReleaseListQueryResult();
            }
            catch (Exception exception)
                when (exception is IOException || exception is JsonException || exception is InvalidOperationException)
            {
                // Cached result could not be read. Ignore this case, since the latest information will then be read from the server again.
            }
        }

        return null;
    }

    private async Task SaveReleaseListQueryResult(ReleaseListQueryResult queryResult)
    {
        SerializableReleaseListQueryResult serializedQueryResult = SerializableReleaseListQueryResult.FromReleaseListQueryResult(queryResult);

        try
        {
            string? parentDirectory = Path.GetDirectoryName(_releaseListQueryResultFilePath);
            Debug.Assert(parentDirectory is not null);

            Directory.CreateDirectory(parentDirectory);

            await using var fileStream = new FileStream(_releaseListQueryResultFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(fileStream, serializedQueryResult, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (IOException)
        {
            // Latest result could not be written to cache file. Ignore this case, since the latest information will then be read from the
            // server again.
        }
    }

    private static string GetEntryAssemblyDirectoryPath()
    {
        // GetEntryAssembly only returns null if called from unmanaged code.
        Assembly? entryAssembly = Assembly.GetEntryAssembly();
        Debug.Assert(entryAssembly is not null);

        string? assemblyDirectoryPath = Path.GetDirectoryName(entryAssembly.Location);
        Debug.Assert(assemblyDirectoryPath is not null);

        return assemblyDirectoryPath;
    }

    private static IReadOnlyList<ReleaseDescription> ReleaseEntriesToDescriptions(IEnumerable<ReleaseEntry> entries)
    {
        var releaseDescriptions = new List<ReleaseDescription>();

        foreach (ReleaseEntry entry in entries.Where(entry => !entry.IsDraft))
        {
            if (SemVersion.TryParse(entry.Name, SemVersionStyles.AllowLowerV, out SemVersion? semVersion))
            {
                ReleaseAsset? updatePackageAsset = entry.Assets.FirstOrDefault(asset => asset.Name == "GourmetClient.zip");
                ReleaseAsset? checksumPackageAsset = entry.Assets.FirstOrDefault(asset => asset.Name == "checksum.txt");

                if (updatePackageAsset is not null && checksumPackageAsset is not null)
                {
                    releaseDescriptions.Add(
                        new ReleaseDescription(
                            semVersion,
                            updatePackageAsset.DownloadUrl,
                            updatePackageAsset.Size,
                            checksumPackageAsset.DownloadUrl,
                            checksumPackageAsset.Size));
                }
            }
        }

        return releaseDescriptions;
    }

    private async Task VerifyChecksum(string packagePath, string signedChecksumFilePath, CancellationToken cancellationToken)
    {
        byte[] signedChecksum;
        byte[] calculatedChecksum;

        try
        {
            string signedChecksumBase64 = await File.ReadAllTextAsync(signedChecksumFilePath, cancellationToken);
            signedChecksum = Convert.FromBase64String(signedChecksumBase64);

            calculatedChecksum = await CalculateChecksum(packagePath);
        }
        catch (Exception exception) when (exception is IOException || exception is FormatException)
        {
            throw new GourmetUpdateException("Could not read/calculate checksum of update package", exception);
        }

        await using Stream? publicKeyXmlStream = Assembly
            .GetExecutingAssembly()
            .GetManifestResourceStream("GourmetClient.Resources.UpdatePackageSignaturePublicKey.xml");

        if (publicKeyXmlStream is null)
        {
            throw new GourmetUpdateException("Public key for signature of update package could not be found");
        }

        using var publicKeyXmlStreamReader = new StreamReader(publicKeyXmlStream);
        using RSA rsa = RSA.Create();

        string publicKeyXml = await publicKeyXmlStreamReader.ReadToEndAsync(cancellationToken);
        rsa.FromXmlString(publicKeyXml);

        var rsaDeformatter = new RSAPKCS1SignatureDeformatter(rsa);
        rsaDeformatter.SetHashAlgorithm(nameof(SHA256));

        if (!rsaDeformatter.VerifySignature(calculatedChecksum, signedChecksum))
        {
            throw new GourmetUpdateException("Checksum of update package is invalid");
        }
    }

    private async Task<byte[]> CalculateChecksum(string path)
    {
        using SHA256 sha256 = SHA256.Create();
        await using FileStream stream = File.OpenRead(path);

        return await sha256.ComputeHashAsync(stream);
    }

    private async Task CopyDirectory(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            Directory.CreateDirectory(targetPath);

            foreach (string filePath in Directory.GetFiles(sourcePath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fileName = Path.GetFileName(filePath);
                string targetFilePath = Path.Combine(targetPath, fileName);

                File.Copy(filePath, targetFilePath);
            }
        }, cancellationToken);

        foreach (string directoryPath in Directory.GetDirectories(sourcePath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string directoryName = new DirectoryInfo(directoryPath).Name;
            string targetDirectoryPath = Path.Combine(targetPath, directoryName);

            await CopyDirectory(directoryPath, targetDirectoryPath, cancellationToken);
        }
    }

    private static string GetTempUpdateFolderPath()
    {
        return Path.Combine(Path.GetTempPath(), "GourmetClientUpdate");
    }

    private static string GetLocalUpdatePackageFilePath()
    {
        return Path.Combine(GetTempUpdateFolderPath(), "GourmetClient.zip");
    }

    private static string GetLocalSignedChecksumFilePath()
    {
        return Path.Combine(GetTempUpdateFolderPath(), "checksum.txt");
    }
}