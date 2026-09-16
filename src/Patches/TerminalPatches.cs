using HarmonyLib;

namespace Valhicle.Patches
{
    [HarmonyPatch(typeof(Terminal))]
    public static class TerminalPatches
    {
        [HarmonyPatch("InitTerminal")]
        [HarmonyPostfix]
        public static void InitTerminalPostfix()
        {
            new Terminal.ConsoleCommand("clearinventory", "Clears all items from your inventory", (Terminal.ConsoleEventArgs args) =>
            {
                if (Player.m_localPlayer != null)
                {
                    Player.m_localPlayer.GetInventory().RemoveAll();
                    Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, "Inventory completely cleared!");
                }
            });

            new Terminal.ConsoleCommand("clearunequipped", "Clears all unequipped items from your inventory", (Terminal.ConsoleEventArgs args) =>
            {
                if (Player.m_localPlayer != null)
                {
                    Player.m_localPlayer.GetInventory().RemoveUnequipped();
                    Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, "Unequipped items cleared!");
                }
            });
        }
    }
}
