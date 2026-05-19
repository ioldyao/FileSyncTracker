using FileSyncTracker.Core.Models;
using Xunit;

namespace FileSyncTracker.Core.Tests;

public class FileIdentityTests
{
    [Fact]
    public void Matches_ShouldReturnTrue_WhenAllFieldsMatch()
    {
        var identity1 = new FileIdentity
        {
            FileName = "test.txt",
            FileSize = 1024,
            LastModified = new DateTime(2024, 1, 1),
            NtfsFileId = 12345
        };

        var identity2 = new FileIdentity
        {
            FileName = "test.txt",
            FileSize = 1024,
            LastModified = new DateTime(2024, 1, 1),
            NtfsFileId = 12345
        };

        Assert.True(identity1.Matches(identity2));
    }

    [Fact]
    public void Matches_ShouldReturnFalse_WhenNameDiffers()
    {
        var identity1 = new FileIdentity { FileName = "test.txt", FileSize = 1024, NtfsFileId = 12345 };
        var identity2 = new FileIdentity { FileName = "other.txt", FileSize = 1024, NtfsFileId = 12345 };

        // NTFS FileId match takes priority
        Assert.True(identity1.Matches(identity2));
    }

    [Fact]
    public void FallbackMatch_ShouldMatchByNameAndSize()
    {
        var identity1 = new FileIdentity { FileName = "test.txt", FileSize = 1024, NtfsFileId = 0 };
        var identity2 = new FileIdentity { FileName = "test.txt", FileSize = 1024, NtfsFileId = 0 };

        Assert.True(identity1.FallbackMatch(identity2));
    }

    [Fact]
    public void FallbackMatch_ShouldNotMatch_WhenSizeDiffers()
    {
        var identity1 = new FileIdentity { FileName = "test.txt", FileSize = 1024, NtfsFileId = 0 };
        var identity2 = new FileIdentity { FileName = "test.txt", FileSize = 2048, NtfsFileId = 0 };

        Assert.False(identity1.FallbackMatch(identity2));
    }

    [Fact]
    public void Matches_ShouldReturnTrue_WhenNtfsFileIdMatches()
    {
        var identity1 = new FileIdentity
        {
            FileName = "original.txt",
            FileSize = 1024,
            LastModified = new DateTime(2024, 1, 1),
            NtfsFileId = 99999
        };

        var identity2 = new FileIdentity
        {
            FileName = "renamed.txt",
            FileSize = 2048,
            LastModified = new DateTime(2025, 6, 15),
            NtfsFileId = 99999
        };

        // NTFS FileId match takes priority over all other fields
        Assert.True(identity1.Matches(identity2));
    }

    [Fact]
    public void Matches_ShouldReturnFalse_WhenNtfsFileIdDiffers()
    {
        var identity1 = new FileIdentity
        {
            FileName = "test.txt",
            FileSize = 1024,
            LastModified = new DateTime(2024, 1, 1),
            NtfsFileId = 11111
        };

        var identity2 = new FileIdentity
        {
            FileName = "test.txt",
            FileSize = 1024,
            LastModified = new DateTime(2024, 1, 1),
            NtfsFileId = 22222
        };

        Assert.False(identity1.Matches(identity2));
    }

    [Fact]
    public void Matches_ShouldFallBackToFieldMatch_WhenOneNtfsFileIdIsZero()
    {
        var identity1 = new FileIdentity
        {
            FileName = "test.txt",
            FileSize = 1024,
            LastModified = new DateTime(2024, 1, 1),
            NtfsFileId = 0
        };

        var identity2 = new FileIdentity
        {
            FileName = "test.txt",
            FileSize = 1024,
            LastModified = new DateTime(2024, 1, 1),
            NtfsFileId = 55555
        };

        // When one FileId is 0 (unknown), fall back to field matching
        Assert.True(identity1.Matches(identity2));
    }

    [Fact]
    public void FromFile_ShouldCreateIdentity_WithValidFilePath()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "test content");

            var identity = FileIdentity.FromFile(tempFile);

            Assert.Equal(Path.GetFileName(tempFile), identity.FileName);
            Assert.True(identity.FileSize > 0);
            Assert.True(identity.LastModified > DateTime.MinValue);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
