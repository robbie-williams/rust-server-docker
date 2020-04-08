using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{

    [Info("FatigueGather", "Default", "0.0.1")]
    class FatigueGather : RustPlugin
    {
        // TODO - add chat command to show current gather rate.
        // TODO - when modifier results in value between (0, 1), we should make the drop probabilistic.
        
        private const float OVERALL_MULTIPLIER = 1.0f;
        private Dictionary<string, int> playerFatigueLevels = new Dictionary<string, int>();

        private Dictionary<int, float> fatigueLevelsToMultipliers = new Dictionary<int, float>
        {
            [8] = 1.0f,
            [7] = 1.0f,
            [6] = 0.99f,
            [5] = 0.97f,
            [4] = 0.95f,
            [3] = 0.85f,
            [2] = 0.6f,
            [1] = 0.25f,
            [0] = 0.1f
        };

        private float getModifierForPlayer(BaseEntity entity)
        {
            var steamId = entity?.ToPlayer()?.IPlayer?.Id;
            if( steamId == null )
            {
                Debug.LogWarning("Entity not a player");
                return OVERALL_MULTIPLIER;
            } 

            var playerFatigueLevel = -1;
            if (!playerFatigueLevels.TryGetValue(steamId, out playerFatigueLevel))
            {
                Debug.LogWarning($"Count not find steam ID {steamId} in fatigueLevel store");
                return OVERALL_MULTIPLIER;
            }
            return OVERALL_MULTIPLIER * fatigueLevelsToMultipliers[playerFatigueLevel];
        }

        private void SetValue( BasePlayer player, int value )
        {
            if( value < 0 || value > 8)
            {
                Debug.LogWarning($"Value provided ({value}) was outside allowed range  [0 - 8]");
                return;
            }
            if(player?.IPlayer?.Id == null)
            {
                Debug.LogWarning($"Not a valid player");
            }
            playerFatigueLevels[player.IPlayer.Id] = value;
        }

        private void OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            if (!entity.ToPlayer()) return;
            
            var modifier = getModifierForPlayer(entity);
            var amount = item.amount;
            item.amount = (int)(item.amount * modifier);

            try
            {
                dispenser.containedItems.Single(x => x.itemid == item.info.itemid).amount += amount - item.amount / modifier;

                if (dispenser.containedItems.Single(x => x.itemid == item.info.itemid).amount < 0)
                {
                    item.amount += (int)dispenser.containedItems.Single(x => x.itemid == item.info.itemid).amount;
                }
            }
            catch ( Exception e )
            { 
                Debug.LogException( e );
            }
        }

        private void OnDispenserBonus(ResourceDispenser dispenser, BaseEntity entity, Item item) => 
            OnDispenserGather(dispenser, entity, item);
 

        private void OnGrowableGather(GrowableEntity growable, Item item, BasePlayer player)
        {
            item.amount = (int)(item.amount * getModifierForPlayer(player));
        }

        private void OnQuarryGather(MiningQuarry quarry, List<ResourceDepositManager.ResourceDeposit.ResourceDepositEntry> items)
        {
            for (var i = 0; i < items.Count; i++)
            {
                items[i].amount = (int)(items[i].amount * OVERALL_MULTIPLIER);
            }
        }

        private void OnExcavatorGather(ExcavatorArm excavator, Item item)
        {
            item.amount = (int)(item.amount * OVERALL_MULTIPLIER);
        }

        private void OnCollectiblePickup(Item item, BasePlayer player)
        {
            item.amount = (int)(item.amount * getModifierForPlayer(player));
        }

        private void OnSurveyGather(SurveyCharge surveyCharge, Item item)
        {
            item.amount = (int)(item.amount * OVERALL_MULTIPLIER);
        }
    }
}
