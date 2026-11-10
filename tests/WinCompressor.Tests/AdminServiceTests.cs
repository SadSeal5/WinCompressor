using WinCompressor.Core.Services;
using Xunit;

namespace WinCompressor.Tests;

public class AdminServiceTests
{
    [Fact]
    public void IsAdministrator_ShouldReturnBooleanWithoutThrowing()
    {
        var adminService = new AdminService();
        var isAdmin = adminService.IsAdministrator();

        // Must succeed without throwing
        Assert.True(isAdmin || !isAdmin);
    }
}
