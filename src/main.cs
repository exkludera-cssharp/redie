using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.UserMessages;
using System.Drawing;

public class Plugin : BasePlugin, IPluginConfig<Config>
{
    public override string ModuleName => "Redie";
    public override string ModuleVersion => "1.0.2";
    public override string ModuleAuthor => "exkludera";

    public HashSet<CCSPlayerController> RediePlayers = new();

    public override void Load(bool hotReload)
    {
        foreach (var cmd in Config.Commands)
            AddCommand(cmd, "Redie Command", (player, command) => Command_Redie(player));

        RegisterEventHandler<EventRoundStart>(EventRoundStart, HookMode.Pre);
        RegisterEventHandler<EventPlayerTeam>(EventPlayerTeam);
        RegisterEventHandler<EventPlayerDeath>(EventPlayerDeath, HookMode.Pre);

        RegisterListener<Listeners.CheckTransmit>(CheckTransmit);

        HookUserMessage(208, CMsgSosStartSoundEvent, HookMode.Pre);

        HookEntityOutput("*", "*", Disrupting, HookMode.Pre);

        VirtualFunctions.CCSPlayer_ItemServices_CanAcquireFunc.Hook(CanAcquireFunc, HookMode.Pre);
        VirtualFunctions.CBaseEntity_TakeDamageOldFunc.Hook(TakeDamageOldFunc, HookMode.Pre);
    }

    public override void Unload(bool hotReload)
    {
        foreach (var cmd in Config.Commands)
            RemoveCommand(cmd, (player, command) => Command_Redie(player));

        DeregisterEventHandler<EventRoundStart>(EventRoundStart, HookMode.Pre);
        DeregisterEventHandler<EventPlayerTeam>(EventPlayerTeam);
        DeregisterEventHandler<EventPlayerDeath>(EventPlayerDeath, HookMode.Pre);

        RemoveListener<Listeners.CheckTransmit>(CheckTransmit);

        UnhookUserMessage(208, CMsgSosStartSoundEvent, HookMode.Pre);

        UnhookEntityOutput("*", "*", Disrupting, HookMode.Pre);

        VirtualFunctions.CCSPlayer_ItemServices_CanAcquireFunc.Unhook(CanAcquireFunc, HookMode.Pre);
        VirtualFunctions.CBaseEntity_TakeDamageOldFunc.Unhook(TakeDamageOldFunc, HookMode.Pre);
    }

    public Config Config { get; set; } = new Config();
    public void OnConfigParsed(Config config)
    {
        Config = config;
        Config.Prefix = StringExtensions.ReplaceColorTags(config.Prefix);
    }

    void Command_Redie(CCSPlayerController? player)
    {
        if (player == null || player.PawnIsAlive || player.Team == CsTeam.Spectator || player.Team == CsTeam.None)
            return;

        if (!RediePlayers.Contains(player))
            Redie(player);

        else UnRedie(player);
    }

    void Redie(CCSPlayerController player)
    {
        var playerPawn = player.PlayerPawn.Value;
        if (playerPawn == null) return;

        player.Respawn();
        player.RemoveWeapons();

        RediePlayers.Add(player);

        Server.NextFrame(() =>
        {
            playerPawn.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DISSOLVING; //noblock
            playerPawn.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DISSOLVING; //noblock
            playerPawn.ShadowStrength = 0f;
            playerPawn.Render = Color.Transparent; //for ragdoll if player unredie

            //timer to avoid blackscreen
            AddTimer(0.25f, () =>
            {
                playerPawn.LifeState = (byte)LifeState_t.LIFE_DYING;
            });
        });

        if (Config.Messages)
            player.PrintToChat($"{Config.Prefix} {Config.Message_Redie}");
    }

    void UnRedie(CCSPlayerController player)
    {
        var playerPawn = player.PlayerPawn.Value;
        if (playerPawn == null) return;

        Server.NextFrame(() =>
        {
            playerPawn.LifeState = (byte)LifeState_t.LIFE_ALIVE;
            player.CommitSuicide(false, true);

            if (Config.Messages)
                player.PrintToChat($"{Config.Prefix} {Config.Message_UnRedie}");
        });
    }

    HookResult EventRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        RediePlayers.Clear();

        return HookResult.Continue;
    }

    HookResult EventPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        var player = @event.Userid;

        if (player == null || !player.IsValid)
            return HookResult.Continue;

        if (RediePlayers.Contains(player))
            RediePlayers.Remove(player);

        return HookResult.Continue;
    }

    HookResult EventPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var player = @event.Userid;

        if (player == null || !player.IsValid)
            return HookResult.Continue;

        if (RediePlayers.Contains(player))
        {
            info.DontBroadcast = true;
            RediePlayers.Remove(player);
        }

        return HookResult.Continue;
    }

    void CheckTransmit(CCheckTransmitInfoList infoList)
    {
        foreach ((CCheckTransmitInfo info, CCSPlayerController? player) in infoList)
        {
            if (player == null) continue;

            foreach (var hidden in RediePlayers)
            {
                if (player == hidden || player.Pawn.Value?.As<CCSPlayerPawnBase>().PlayerState == CSPlayerState.STATE_OBSERVER_MODE)
                    continue;

                var remove = hidden.Pawn.Value;
                if (remove == null) continue;

                info.TransmitEntities.Remove(remove);
            }
        }
    }

    HookResult CMsgSosStartSoundEvent(UserMessage um)
    {
        int entIndex = um.ReadInt("source_entity_index");
        var entHandle = NativeAPI.GetEntityFromIndex(entIndex);

        var pawn = new CBasePlayerPawn(entHandle);
        if (pawn == null || !pawn.IsValid || pawn.DesignerName != "player")
            return HookResult.Continue;

        var player = pawn.Controller?.Value?.As<CCSPlayerController>();
        if (player == null || !player.IsValid)
            return HookResult.Continue;

        if (RediePlayers.Contains(player))
        {
            foreach (var target in Utilities.GetPlayers())
            {
                if (target.IsBot) continue;
                if (target == player) continue;

                um.Recipients.Remove(target);
            }
        }

        return HookResult.Continue;
    }

    HookResult Disrupting(CEntityIOOutput output, string name, CEntityInstance activator, CEntityInstance caller, CVariant value, float delay)
    {
        if (activator.DesignerName != "player")
            return HookResult.Continue;

        var pawn = activator.As<CCSPlayerPawn>();
        if (pawn == null || !pawn.IsValid)
            return HookResult.Continue;

        var player = pawn.OriginalController?.Value?.As<CCSPlayerController>();
        if (player == null || player.IsBot)
            return HookResult.Continue;

        if (RediePlayers.Contains(player))
            return HookResult.Handled;

        return HookResult.Continue;
    }

    HookResult CanAcquireFunc(DynamicHook hook)
    {
        var player = hook.GetParam<CCSPlayer_ItemServices>(0).Pawn.Value.Controller.Value?.As<CCSPlayerController>();
        if (player == null) return HookResult.Continue;

        if (RediePlayers.Contains(player))
        {
            hook.SetReturn(false);
            return HookResult.Handled;
        }

        return HookResult.Continue;
    }

    HookResult TakeDamageOldFunc(DynamicHook hook)
    {
        var pawn = hook.GetParam<CCSPlayerPawn>(0);
        var info = hook.GetParam<CTakeDamageInfo>(1);

        if (pawn == null || info == null)
            return HookResult.Continue;

        var player = pawn.OriginalController?.Value?.As<CCSPlayerController>();
        if (player == null || player.IsBot) return HookResult.Continue;

        if (RediePlayers.Contains(player))
        {
            if (info.DamageFlags.HasFlag(TakeDamageFlags_t.DFLAG_FORCE_DEATH))
                return HookResult.Continue;

            if ((pawn.DesignerName == "player" && info.Attacker.Value?.DesignerName == "player") || info.BitsDamageType == DamageTypes_t.DMG_FALL)
            {
                hook.SetReturn(false);
                return HookResult.Handled;
            }
 
            UnRedie(player);
        }

        return HookResult.Continue;
    }
}