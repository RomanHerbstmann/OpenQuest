using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using OpenQuest.Api.Config;

namespace OpenQuest.Api.Storage;

// Consumers depend only on the capability they need (interface segregation).
public interface IBlobWriter
{
    Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct = default);
}

public interface IBlobReader
{
    Task<byte[]?> GetAsync(string key, CancellationToken ct = default);
}

public interface IBlobDeleter
{
    Task DeleteAsync(string key, CancellationToken ct = default);
}

/// <summary>S3-compatible object storage (MinIO locally).</summary>
public sealed class S3BlobStore : IBlobWriter, IBlobReader, IBlobDeleter, IDisposable
{
    private readonly AmazonS3Client _s3;
    private readonly string _bucket;
    private readonly SemaphoreSlim _bucketLock = new(1, 1);
    private bool _bucketReady;

    public S3BlobStore(IOptions<StorageOptions> options)
    {
        var o = options.Value;
        _bucket = o.Bucket;
        _s3 = new AmazonS3Client(
            new BasicAWSCredentials(o.AccessKey, o.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = o.ServiceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
    }

    public async Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct = default)
    {
        await EnsureBucketAsync(ct);
        using var ms = new MemoryStream(content);
        await _s3.PutObjectAsync(new PutObjectRequest { BucketName = _bucket, Key = key, InputStream = ms, ContentType = contentType }, ct);
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            using var res = await _s3.GetObjectAsync(_bucket, key, ct);
            using var ms = new MemoryStream();
            await res.ResponseStream.CopyToAsync(ms, ct);
            return ms.ToArray();
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
        => await _s3.DeleteObjectAsync(_bucket, key, ct);

    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        if (_bucketReady) return;
        await _bucketLock.WaitAsync(ct);
        try
        {
            if (_bucketReady) return;
            var buckets = await _s3.ListBucketsAsync(ct);
            if (!buckets.Buckets.Any(b => b.BucketName == _bucket))
                await _s3.PutBucketAsync(new PutBucketRequest { BucketName = _bucket }, ct);
            _bucketReady = true;
        }
        finally { _bucketLock.Release(); }
    }

    public void Dispose() => _s3.Dispose();
}
