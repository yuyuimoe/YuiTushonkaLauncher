using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Files;
using YuiTushonkaLauncher.Infrastructure.FileSystem;

namespace YuiTushonkaLauncher.Tests.Instances;

public sealed class PlatformErrorMapperTests
{
    [Theory]
    [InlineData(18, FileLinkError.CrossDevice)] // EXDEV
    [InlineData(1, FileLinkError.PermissionDenied)] // EPERM
    [InlineData(13, FileLinkError.PermissionDenied)] // EACCES
    [InlineData(30, FileLinkError.PermissionDenied)] // EROFS
    [InlineData(31, FileLinkError.LinkLimitReached)] // EMLINK
    [InlineData(28, FileLinkError.NoSpace)] // ENOSPC
    [InlineData(17, FileLinkError.AlreadyExists)] // EEXIST
    [InlineData(2, FileLinkError.NotFound)] // ENOENT
    [InlineData(9999, FileLinkError.Unknown)]
    public void FromUnix_MapsTheErrnosThatMatter(int errno, FileLinkError expected)
    {
        PlatformErrorMapper.FromUnix(errno).Should().Be(expected);
    }

    [Theory]
    [InlineData(17, FileLinkError.CrossDevice)] // ERROR_NOT_SAME_DEVICE
    [InlineData(5, FileLinkError.PermissionDenied)] // ERROR_ACCESS_DENIED
    [InlineData(19, FileLinkError.PermissionDenied)] // ERROR_WRITE_PROTECT
    [InlineData(32, FileLinkError.PermissionDenied)] // ERROR_SHARING_VIOLATION
    [InlineData(1, FileLinkError.PermissionDenied)] // ERROR_INVALID_FUNCTION (FAT32)
    [InlineData(1142, FileLinkError.LinkLimitReached)] // ERROR_TOO_MANY_LINKS
    [InlineData(112, FileLinkError.NoSpace)] // ERROR_DISK_FULL
    [InlineData(183, FileLinkError.AlreadyExists)] // ERROR_ALREADY_EXISTS
    [InlineData(3, FileLinkError.NotFound)] // ERROR_PATH_NOT_FOUND
    [InlineData(9999, FileLinkError.Unknown)]
    public void FromWindows_MapsTheErrorCodesThatMatter(int error, FileLinkError expected)
    {
        PlatformErrorMapper.FromWindows(error).Should().Be(expected);
    }

    [Fact]
    public void FileLinkException_ExplainsTheCrossDeviceCase()
    {
        var exception = new FileLinkException(FileLinkError.CrossDevice, "failed");

        exception.ToUserMessage().Should().Contain("same disk");
    }
}
