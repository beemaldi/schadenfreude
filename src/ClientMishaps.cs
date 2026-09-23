using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace BadLuck;

/// <summary>
/// Client side of stumbling: reads the counter the server set on your own player and puts the
/// character on the ground. The cliff fall needs nothing here any more - the server handles that one
/// with knockback and a short teleport.
/// </summary>
public class ClientMishaps
{
    /// <summary>
    /// Code from player.json ("sleep" points at the shape animation "lie"). A hand-built
    /// AnimationMetaData with Animation = "sleep" finds nothing - the shape only knows "lie".
    /// </summary>
    const string LieAnimation = "sleep";

    readonly ICoreClientAPI capi;
    int lastTrip = int.MinValue;
    long tripUntilMs = -1;

    public ClientMishaps(ICoreClientAPI capi)
    {
        this.capi = capi;
        capi.Event.RegisterGameTickListener(OnTick, 20);
    }

    void OnTick(float dt)
    {
        EntityPlayer eplr = capi.World.Player?.Entity;
        if (eplr == null) return;
        var attrs = eplr.WatchedAttributes;
        long now = capi.ElapsedMilliseconds;

        // On the first tick only remember the value, so nothing old fires right after logging in
        int trip = attrs.GetInt(Mishaps.TripCounterKey);
        if (lastTrip == int.MinValue) lastTrip = trip;

        if (trip != lastTrip)
        {
            lastTrip = trip;
            tripUntilMs = now + (long)(attrs.GetFloat(Mishaps.TripSecondsKey, 3) * 1000);
            eplr.AnimManager.StartAnimation(LieAnimation);
        }
        if (tripUntilMs < 0) return;

        if (now < tripUntilMs)
        {
            // On the ground: camera low, character lying down
            eplr.Controls.FloorSitting = true;
        }
        else
        {
            tripUntilMs = -1;
            eplr.Controls.FloorSitting = false;
            eplr.AnimManager.StopAnimation(LieAnimation);
        }
    }
}
