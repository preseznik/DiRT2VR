using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace DiRT2VR.CustomTracks;

public sealed record TrackProgress(int Percent, string Message);

public static class PackageDownload
{
    internal static Func<HttpMessageHandler> Transport = () => new HttpClientHandler { AllowAutoRedirect = false };
    static HttpClient Client()
    {
        var client = new HttpClient(Transport()) { Timeout = TimeSpan.FromMinutes(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DiRT2VR-CustomTracks/1.0");
        return client;
    }
    static async Task<HttpResponseMessage> Get(HttpClient client, Uri uri, CancellationToken cancel)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo != "" ||
                !new[] { "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com" }.Contains(uri.Host))
                throw new IOException("The package download redirected to an unsupported address.");
            var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null) throw new IOException("Package download redirect is missing its destination.");
                uri = new Uri(uri, location); continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode; response.Dispose();
                throw new IOException(status == HttpStatusCode.NotFound ? "Aspen downloads are not available yet. Installed tracks still work offline." :
                    "Could not reach the custom-track downloads. Check your connection and try again.");
            }
            return response;
        }
        throw new IOException("Too many package download redirects.");
    }
    public static async Task<PackageOffer> CatalogAsync(string launcherVersion, CancellationToken cancel)
    {
        using var client = Client();
        using var response = await Get(client, new Uri(AspenPack.CatalogUrl), cancel).ConfigureAwait(false);
        using var input = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        using var memory = new MemoryStream();
        await BoundedCopy(input, memory, 4 * 1024 * 1024, null, cancel).ConfigureAwait(false);
        Catalog catalog;
        try { catalog = JsonSerializer.Deserialize<Catalog>(System.Text.Encoding.UTF8.GetString(memory.ToArray()).TrimStart('\uFEFF'), SafeFiles.Json) ?? throw new IOException("Custom-track catalog is empty."); }
        catch (JsonException e) { throw new IOException("Custom-track catalog is invalid.", e); }
        if (catalog.Schema != 1 || catalog.Tracks is null || catalog.Tracks.Length != 1) throw new IOException("Unsupported custom-track catalog.");
        var offer = catalog.Tracks.Single(); AspenPack.Validate(offer, launcherVersion); return offer;
    }
    public static async Task DownloadAsync(PackageOffer offer, string destination, string launcherVersion, IProgress<TrackProgress>? progress, CancellationToken cancel)
    {
        AspenPack.Validate(offer, launcherVersion); SafeFiles.NoLinks(destination);
        using var client = Client();
        using var response = await Get(client, new Uri(offer.DownloadUrl), cancel).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is long length && length != offer.DownloadBytes) throw new IOException("Package download size differs from the catalog.");
        using var input = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await BoundedCopy(input, output, offer.DownloadBytes, bytes => progress?.Report(new((int)(bytes * 100 / offer.DownloadBytes), "Downloading Aspen conversion tools")), cancel).ConfigureAwait(false);
            if (output.Length != offer.DownloadBytes) throw new IOException("Package download is incomplete. Try again.");
            output.Flush(true);
        }
        cancel.ThrowIfCancellationRequested();
        if (!SafeFiles.Hash(destination).Equals(offer.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Package checksum failed. Try downloading again.");
    }
    static async Task BoundedCopy(Stream input, Stream output, long limit, Action<long>? progress, CancellationToken cancel)
    {
        byte[] buffer = new byte[131072]; long total = 0;
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancel).ConfigureAwait(false);
            if (read == 0) break;
            total = checked(total + read);
            if (total > limit) throw new IOException("Download exceeds the expected size.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
            progress?.Invoke(total);
        }
    }
    public static void Extract(string zip, string output, CancellationToken cancel)
    {
        SafeFiles.NoLinks(zip); SafeFiles.NoLinks(output);
        if (Directory.Exists(output)) throw new IOException("Extraction directory must be new.");
        using var archive = ZipFile.OpenRead(zip);
        if (archive.Entries.Count is < 1 or > 512) throw new IOException("Invalid package file count.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var entry in archive.Entries)
        {
            cancel.ThrowIfCancellationRequested();
            string name = entry.FullName;
            SafeFiles.Relative(name);
            if (!paths.Add(name) || (entry.ExternalAttributes & 0x400) != 0 || ((entry.ExternalAttributes >> 16) & 0xF000) is 0xA000 or 0x4000 ||
                !(name == "AspenConverter.exe" || name == "README.txt" || name == "package.json" ||
                    (name.StartsWith("licenses/", StringComparison.Ordinal) && new[] { ".txt", ".md" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))) ||
                entry.Length < 0 || entry.Length > 512L * 1024 * 1024)
                throw new IOException("Unsupported file or link in conversion package: " + name);
            total = checked(total + entry.Length);
        }
        if (!paths.Contains("AspenConverter.exe") || total > 768L * 1024 * 1024) throw new IOException("Incomplete or oversized conversion package.");
        Directory.CreateDirectory(output);
        foreach (var entry in archive.Entries)
        {
            cancel.ThrowIfCancellationRequested();
            string target = SafeFiles.Inside(output, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var file = new FileStream(target, FileMode.CreateNew);
            BoundedCopy(input, file, entry.Length, null, cancel).GetAwaiter().GetResult();
            if (file.Length != entry.Length) throw new IOException("Incomplete package file.");
        }
    }
    public static void VerifySources(PackageOffer offer, string dirt2, string dirt3, IProgress<TrackProgress>? progress, CancellationToken cancel)
    {
        AspenPack.ValidateSources(offer.Sources);
        for (int i = 0; i < offer.Sources.Length; i++)
        {
            cancel.ThrowIfCancellationRequested(); var source = offer.Sources[i];
            string path = SafeFiles.Inside(source.Game == "dirt2" ? dirt2 : dirt3, source.Path);
            if (!File.Exists(path) || SafeFiles.Hash(path) != source.Sha256)
                throw new IOException($"Missing or unsupported {source.Game} source file: {source.Path}. Choose the correct game folder or verify the original files in Steam.");
            progress?.Report(new((i + 1) * 100 / offer.Sources.Length, "Checking original game files"));
        }
    }
}
