using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Schadenfreude;

/// <summary>
/// Mechanic 16: you choke on your food and cough. Coughing is loud - for a while afterwards creatures
/// notice the player from further away (the animalSeekingRange game stat).
/// </summary>
public static class Choking
{
    static readonly SeekRangeBoost notice = new("schadenfreude-cough");
    static readonly AssetLocation CoughSound = new(SchadenfreudeModSystem.ModId, "sounds/player/cough");

    /// <summary>Until when a player is still coughing - no new fit before that, so coughs never overlap</summary>
    static readonly Dictionary<string, long> busyUntilMs = new();

    static ICoreServerAPI sapi;

    public static void Register(ICoreServerAPI api)
    {
        sapi = api;
        notice.Clear();
        busyUntilMs.Clear();
        api.Event.RegisterGameTickListener(_ => notice.RemoveExpired(sapi), 1000);
        api.Event.PlayerDisconnect += player =>
        {
            notice.Remove(player);
            busyUntilMs.Remove(player.PlayerUID);
        };
    }

    /// <summary>A piece of food, or one serving of a meal, was eaten</summary>
    public static void OnAte(IServerPlayer player)
    {
        ChokingConfig cfg = SchadenfreudeModSystem.Config.Choking;
        if (!cfg.Enabled || player?.Entity == null || !player.Entity.Alive || IsCoughing(player)) return;
        if (!SchadenfreudeModSystem.Affects(player) || !SchadenfreudeModSystem.Roll(sapi.World, cfg.ChancePercent)) return;

        StartFit(player);
        notice.Apply(player.Entity, cfg.NoticeBonus, cfg.NoticeSeconds);
    }

    /// <summary>Choke right now (test command); false = still coughing from the last fit</summary>
    public static bool ForceChoke(IServerPlayer player)
    {
        if (player?.Entity == null || IsCoughing(player)) return false;

        StartFit(player);
        return true;
    }

    static bool IsCoughing(IServerPlayer player)
    {
        return busyUntilMs.TryGetValue(player.PlayerUID, out long until) && sapi.World.ElapsedMilliseconds < until;
    }

    static void StartFit(IServerPlayer player)
    {
        ChokingConfig cfg = SchadenfreudeModSystem.Config.Choking;
        int coughs = Math.Max(1, cfg.Coughs);

        // Spread over the fit, but never closer together than the gap - the sound is a long one
        int intervalMs = (int)(Math.Max(cfg.CoughGapSeconds, cfg.FitSeconds / coughs) * 1000);
        busyUntilMs[player.PlayerUID] = sapi.World.ElapsedMilliseconds + (long)coughs * intervalMs;

        SchadenfreudeModSystem.Chat(player, "schadenfreude:choking-message");
        Cough(player, coughs, intervalMs);
    }

    static void Cough(IServerPlayer player, int left, int intervalMs)
    {
        if (left <= 0 || player.Entity == null || !player.Entity.Alive) return;

        sapi.World.PlaySoundAt(CoughSound, player.Entity, null, true, 24);

        if (left > 1) sapi.Event.RegisterCallback(_ => Cough(player, left - 1, intervalMs), intervalMs);
    }
}
