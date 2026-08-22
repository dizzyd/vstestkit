// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
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

            // After the weather system exists.
            sapi.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () =>
            {
                StopTheWeather(sapi);
                PinTheDate(sapi);
                StopTheClock(sapi);
            });
        }

        /// <summary>
        /// Turns precipitation off, unless VSTK_WEATHER=1.
        ///
        /// Rain is not background scenery in a test: sky-exposed farmland pulls in
        /// every hour of precipitation since its last update
        /// (BlockEntitySoilNutrition.updateMoistureLevel walks back through them),
        /// so advancing the calendar wets soil whether or not anything the test
        /// did was responsible. A moisture assertion then passes or fails on
        /// simulated weather, which is a miserable thing to debug.
        ///
        /// Set VSTK_WEATHER=1 when the weather is the thing under test.
        /// </summary>
        /// <summary>
        /// Puts the world at a fixed date before anything renders it.
        ///
        /// Season is baked into chunk meshes when they are tesselated, so the
        /// grass takes its colour from whatever the date was as each chunk was
        /// built. Setting the date later re-colours nothing already drawn: a
        /// fresh boot then renders yellow ground where a warm session renders
        /// green, from the same seed at the same calendar time. Doing it here, at
        /// RunGame and before any client has chunks, means everything is meshed
        /// under one date.
        ///
        /// Midday on day 500 by default; VSTK_WORLD_HOURS overrides. Time of day
        /// is total hours modulo 24.
        /// </summary>
        static void PinTheDate(ICoreServerAPI sapi)
        {
            var target = 500.0 * 24 + 12;

            var configured = Environment.GetEnvironmentVariable("VSTK_WORLD_HOURS");
            if (!string.IsNullOrEmpty(configured) &&
                double.TryParse(configured, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                target = parsed;
            }

            var calendar = sapi.World.Calendar;
            calendar.Add((float)(target - calendar.TotalHours));

            Hub.Logger?.Notification(
                "[vstestkit] world date pinned to {0} total hours (hour {1} of the day)",
                target, target % calendar.HoursPerDay);
        }

        /// <summary>
        /// Freezes the passage of time, unless VSTK_TIME=1.
        ///
        /// A world where the clock runs is a world where the answer changes while
        /// you are not looking: lighting shifts, and over a longer run so does the
        /// season, which recolours the grass. That last one is not hypothetical -
        /// it made two captures of an identical scene differ by 42% of their
        /// pixels, all of it ground.
        ///
        /// This is the same thing /time stop does. Hours() still works, because it
        /// calls Calendar.Add directly: time passes when a test says so and not
        /// otherwise, which is what a test wants.
        /// </summary>
        static void StopTheClock(ICoreServerAPI sapi)
        {
            if (Environment.GetEnvironmentVariable("VSTK_TIME") == "1")
            {
                Hub.Logger?.Notification("[vstestkit] leaving the clock running (VSTK_TIME=1)");
                return;
            }

            sapi.World.Calendar.SetTimeSpeedModifier("baseline", 0f);
            Hub.Logger?.Notification("[vstestkit] time frozen (VSTK_TIME=1 to let it run)");
        }

        static void StopTheWeather(ICoreServerAPI sapi)
        {
            if (Environment.GetEnvironmentVariable("VSTK_WEATHER") == "1")
            {
                Hub.Logger?.Notification("[vstestkit] leaving weather enabled (VSTK_WEATHER=1)");
                return;
            }

            var weather = sapi.ModLoader.GetModSystem<WeatherSystemServer>();
            if (weather == null)
            {
                Hub.Logger?.Notification("[vstestkit] no weather system loaded; nothing to disable");
                return;
            }

            weather.OverridePrecipitation = 0f;
            Hub.Logger?.Notification("[vstestkit] precipitation forced to 0 (VSTK_WEATHER=1 to allow rain)");
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
