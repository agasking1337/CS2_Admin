using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Menu;

public interface IAdminMenuHandler
{
    IMenu CreateMenu(IPlayer player, IMenu? parent = null);
}
