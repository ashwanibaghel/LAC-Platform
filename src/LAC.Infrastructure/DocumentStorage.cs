using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace LAC.Infrastructure;
public sealed record DocumentStorageWriteResult(string StoragePath, string Sha256Hash, long FileSize);
public sealed record StorageHealth(string RootName, bool Writable, long? FreeBytes, long? TotalBytes);


public sealed class LocalStoragePaths
{
    public string DocumentRoot { get; }
    public string ExtractionRoot { get; }
    public string BackupRoot { get; }

    public LocalStoragePaths(IWebHostEnvironment env, IConfiguration configuration)
    {
        if (env.IsProduction())
        {
            DocumentRoot = ValidateAndResolveProductionPath("Storage:DocumentRoot", configuration["Storage:DocumentRoot"], env.ContentRootPath);
            ExtractionRoot = ValidateAndResolveProductionPath("Storage:ExtractionRoot", configuration["Storage:ExtractionRoot"], env.ContentRootPath);
            BackupRoot = ValidateAndResolveProductionPath("Storage:BackupRoot", configuration["Storage:BackupRoot"], env.ContentRootPath);

            Directory.CreateDirectory(DocumentRoot);
            Directory.CreateDirectory(ExtractionRoot);
            Directory.CreateDirectory(BackupRoot);

            var probe = Path.Combine(DocumentRoot, $".lac-prod-probe-{Guid.NewGuid():N}");
            try
            {
                using (File.Create(probe)) { }
                File.Delete(probe);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Production document storage root is not writable: '{DocumentRoot}'. {ex.Message}", ex);
            }
        }
        else
        {
            DocumentRoot = Resolve(configuration["Storage:DocumentRoot"], Path.Combine(env.ContentRootPath, "App_Data", "documents"));
            ExtractionRoot = Resolve(configuration["Storage:ExtractionRoot"], Path.Combine(env.ContentRootPath, "App_Data", "extraction"));
            BackupRoot = Resolve(configuration["Storage:BackupRoot"], Path.Combine(env.ContentRootPath, "App_Data", "backups"));
            Directory.CreateDirectory(DocumentRoot);
            Directory.CreateDirectory(ExtractionRoot);
            Directory.CreateDirectory(BackupRoot);
        }
    }

    private static string ValidateAndResolveProductionPath(string key, string? configured, string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException($"Production requires explicit configuration for '{key}'. Configure machine environment variable or app configuration.");
        }

        var trimmed = configured.Trim();
        if (!Path.IsPathRooted(trimmed))
        {
            throw new InvalidOperationException($"Production storage root '{key}' must be an absolute path: '{configured}'.");
        }

        var fullPath = Path.GetFullPath(trimmed);
        var contentRoot = Path.GetFullPath(contentRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalized = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (normalized.StartsWith(contentRoot, StringComparison.OrdinalIgnoreCase) || string.Equals(normalized, contentRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Production storage root '{key}' must not be inside application content/publish directory: '{configured}'.");
        }

        return fullPath;
    }

    private static string Resolve(string? configured, string fallback) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? fallback : configured);
}
public interface IDocumentStorage { Task<string> SaveAsync(Stream content,string fileName,CancellationToken ct); Task<DocumentStorageWriteResult> SaveAndHashAsync(Stream content,string fileName,CancellationToken ct); Task DeleteAsync(string storagePath,CancellationToken ct); Task<Stream?> OpenReadAsync(string storagePath,CancellationToken ct); StorageHealth GetHealth(); }
public sealed class LocalDocumentStorage : IDocumentStorage { private readonly string _root; public LocalDocumentStorage(LocalStoragePaths paths) { _root=paths.DocumentRoot; } public async Task<string> SaveAsync(Stream content,string fileName,CancellationToken ct)=>(await SaveCoreAsync(content,fileName,false,ct)).StoragePath; public Task<DocumentStorageWriteResult> SaveAndHashAsync(Stream content,string fileName,CancellationToken ct)=>SaveCoreAsync(content,fileName,true,ct); public Task DeleteAsync(string storagePath,CancellationToken ct) { var safe=Path.GetFileName(storagePath); if(string.Equals(safe,storagePath,StringComparison.Ordinal)) { var p=Path.Combine(_root,safe); if(File.Exists(p)) File.Delete(p); } return Task.CompletedTask; } public Task<Stream?> OpenReadAsync(string storagePath,CancellationToken ct) { var safe=Path.GetFileName(storagePath); if(!string.Equals(safe,storagePath,StringComparison.Ordinal)) return Task.FromResult<Stream?>(null); var p=Path.Combine(_root,safe); return Task.FromResult<Stream?>(File.Exists(p)?File.Open(p,FileMode.Open,FileAccess.Read,FileShare.Read):null); } public StorageHealth GetHealth() { try { Directory.CreateDirectory(_root); var probe=Path.Combine(_root,$".lac-write-{Guid.NewGuid():N}"); using(File.Create(probe)){} File.Delete(probe); var drive=new DriveInfo(Path.GetPathRoot(_root)!); return new(_root,true,drive.AvailableFreeSpace,drive.TotalSize); } catch { return new(_root,false,null,null); } } private async Task<DocumentStorageWriteResult> SaveCoreAsync(Stream content,string fileName,bool hash,CancellationToken ct) { Directory.CreateDirectory(_root); var safe=$"{Guid.NewGuid():N}-{Path.GetFileName(fileName)}"; var destination=Path.Combine(_root,safe); long size=0; using var sha=hash?IncrementalHash.CreateHash(HashAlgorithmName.SHA256):null; await using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,131072,FileOptions.Asynchronous|FileOptions.SequentialScan); var buffer=new byte[131072]; int read; while((read=await content.ReadAsync(buffer.AsMemory(0,buffer.Length),ct))>0) { sha?.AppendData(buffer,0,read); await output.WriteAsync(buffer.AsMemory(0,read),ct); size+=read; } return new(safe,sha is null?"":Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant(),size); } }
