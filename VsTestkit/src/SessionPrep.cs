using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VsTestkit
{
    /// <summary>
    /// Gets a fresh session into a state a test can actually drive.
    ///
    /// The blocker is character creation. On a new world, VSSurvivalMod's
    /// CharacterSystem opens GuiDialogCreateCharacter for a player who has not
    /// picked a class, and explicitly calls capi.PauseGame(true) while it is open.
    /// In singleplayer a paused client suspends the internal server's ticking, so
    /// EnqueueMainThreadTask never drains and every verb times out - the session
    /// looks alive and answers /ping, but nothing that touches the world works.
    ///
    /// The server decides purely from the player's "createCharacter" mod data, so
    /// marking it before CharacterSystem reads it makes the dialog never open.
    /// TestkitModSystem.ExecuteOrder is 0.01 against CharacterSystem's default, so
    /// our PlayerJoin handler is registered - and therefore runs - first.
    /// </summary>
    public static class SessionPrep
    {
        public const string CharacterCreatedKey = "createCharacter";

        public static void Install(ICoreServerAPI sapi)
        {
            // PlayerCreate covers a brand new player, PlayerJoin an existing one.
            sapi.Event.PlayerCreate += MarkCharacterChosen;
            sapi.Event.PlayerJoin += MarkCharacterChosen;
        }

        static void MarkCharacterChosen(IServerPlayer player)
        {
            if (player.GetModData<bool>(CharacterCreatedKey, false)) return;

            player.SetModData(CharacterCreatedKey, true);
            Hub.Logger?.Notification(
                "[vstestkit] marked '{0}' as having chosen a character, so the creation " +
                "dialog does not open and pause the game", player.PlayerName);
        }
    }
}
