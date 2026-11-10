namespace WinCompressor.Core.Services;

public interface IAdminService
{
    bool IsAdministrator();
    bool RestartAsAdmin(string? arguments = null);
}
