using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;
using OpenQuest.Api.Config;

namespace OpenQuest.Api.Storage;

/// <summary>Reads the raw downloads the importer keeps (<c>sync_run.snapshot_key</c>). Read only: the importer writes them.</summary>
public interface ISnapshotReader
{
    /// <summary>The object of the key as the importer stored it, or null if the store does not have it.</summary>
    Task<byte[]?> GetAsync(string key, CancellationToken ct = default);
}

/// <summary>
/// The importer's S3 snapshot store (<c>[snapshots] backend = "s3"</c>): same server as the photos, bucket <c>Storage:SnapshotBucket</c>, keys
/// under <c>Storage:SnapshotPrefix</c>. With the importer's default (local directory) there is nothing to read here.
/// </summary>
public sealed class S3SnapshotReader : ISnapshotReader, IDisposable
{
    private readonly AmazonS3Client _s3;
    private readonly string _bucket;
    private readonly string _prefix;

    public S3SnapshotReader(IOptions<StorageOptions> options)
    {
        var o = options.Value;
        _bucket = o.SnapshotBucket;
        _prefix = o.SnapshotPrefix;
        _s3 = new AmazonS3Client(
            new BasicAWSCredentials(o.AccessKey, o.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = o.ServiceUrl, ForcePathStyle = true, AuthenticationRegion = "us-east-1",
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            using var res = await _s3.GetObjectAsync(_bucket, _prefix + key, ct);
            using var ms = new MemoryStream();
            await res.ResponseStream.CopyToAsync(ms, ct);
            return ms.ToArray();
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public void Dispose() => _s3.Dispose();
}
