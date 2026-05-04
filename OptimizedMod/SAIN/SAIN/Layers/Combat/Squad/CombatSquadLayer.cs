using EFT;
using SAIN.Components;
using SAIN.Extensions;
using SAIN.Layers.Combat.Solo;
using SAIN.Models.Enums;
using SAIN.SAINComponent.Classes.Decision;
using UnityEngine;

namespace SAIN.Layers.Combat.Squad;

internal class CombatSquadLayer(BotOwner botOwner, int priority) : SAINLayer(botOwner, priority, Name, ESAINLayer.Squad)
{
    public static readonly string Name = BuildLayerName("Squad Layer");

    public override Action GetNextAction()
    {
        LastActionDecision = Bot.Decision.CurrentSquadDecision;
        switch (LastActionDecision)
        {
            case ESquadDecision.Regroup:
                return new Action(typeof(RegroupAction), $"{LastActionDecision}");

            case ESquadDecision.Suppress:
                return new Action(typeof(SuppressAction), $"{LastActionDecision}");

            case ESquadDecision.Search:
                return new Action(typeof(SearchAction), $"{LastActionDecision}");

            case ESquadDecision.SpreadOut:
                return new Action(typeof(SearchAction), $"{LastActionDecision}");

            case ESquadDecision.HoldPositions:
                return new Action(typeof(RegroupAction), $"{LastActionDecision}");

            case ESquadDecision.GroupSearch:
                if (Bot.Squad.IAmLeader)
                {
                    return new Action(typeof(SearchAction), $"{LastActionDecision} : Lead Search Party");
                }
                return new Action(typeof(FollowSearchParty), $"{LastActionDecision} : Follow Squad Leader");

            case ESquadDecision.Help:
                return new Action(typeof(SearchAction), $"{LastActionDecision}");

            case ESquadDecision.PushSuppressedEnemy:
                return new Action(typeof(RushEnemyAction), $"{LastActionDecision}");

            case ESquadDecision.BoundingRetreat:
            case ESquadDecision.Retreat:
                return new Action(typeof(RegroupAction), $"{LastActionDecision}");

            default:
                return new Action(typeof(RegroupAction), $"DEFAULT!");
        }
    }

    public override bool IsActive()
    {
        if (!BotOwner.IsBotActive())
        {
            CheckActiveChanged(false);
            return false;
        }

        if (GetBotComponent())
        {
            BotComponent bot = Bot;
            if (bot != null && bot.BotActive)
            {
                SAINDecisionClass decisions = bot.Decision;

                // Self actions (surgery, healing) take absolute priority — even over squad coordination.
                if (decisions.CurrentSelfDecision != ESelfActionType.None)
                {
                    CheckActiveChanged(false);
                    return false;
                }

                // If squad coordinator has issued an active (non-expired) order, stay active.
                // Unlike the previous guard, we do NOT bail when CurrentCombatDecision != None.
                // During combat the coordinator must keep distributing targets and flank assignments.
                if (decisions.CurrentSquadDecision != ESquadDecision.None)
                {
                    if (bot.Squad.IAmLeader)
                    {
                        SquadCombatCoordinator.CoordinateSquad(bot, decisions);
                    }
                    CheckActiveChanged(true);
                    return true;
                }

                // If this bot belongs to a squad and the coordinator has an unexpired order,
                // keep the squad layer active even though the 10 Hz pipeline hasn't set a squad decision yet.
                var squadState = SquadCombatCoordinator.GetSquadState(bot);
                if (squadState != null && Time.time < squadState.OrderExpireTime && squadState.LastOrder != ESquadDecision.None)
                {
                    if (bot.Squad.IAmLeader)
                    {
                        SquadCombatCoordinator.CoordinateSquad(bot, decisions);
                    }
                    CheckActiveChanged(true);
                    return true;
                }

                // Rogue base-defense: run coordination once squad decisions are still None so initial
                // orders and loot suppression can start (any member triggers via squad leader — throttled).
                if (SquadCombatCoordinator.ShouldBootstrapRogueDefenseCombatLayer(bot))
                {
                    BotComponent squadLeader = bot.Squad?.LeaderComponent;
                    if (squadLeader != null && squadLeader.BotActive)
                    {
                        SquadCombatCoordinator.CoordinateSquad(squadLeader, squadLeader.Decision);
                        if (bot.Decision.CurrentSquadDecision != ESquadDecision.None)
                        {
                            CheckActiveChanged(true);
                            return true;
                        }
                    }
                }
            }
        }
        CheckActiveChanged(false);
        return false;
    }

    public override bool IsCurrentActionEnding()
    {
        if (base.IsCurrentActionEnding())
        {
            return true;
        }
        BotComponent bot = Bot;
        if (bot != null && bot.BotActive && bot.Decision.CurrentSquadDecision != LastActionDecision)
        {
            return true;
        }
        return false;
    }

    private ESquadDecision LastActionDecision = ESquadDecision.None;
}
