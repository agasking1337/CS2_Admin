using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Menu;

public interface IAdminMenuHandler
{
    IMenuAPI CreateMenu(IPlayer player);
}
