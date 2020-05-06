using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries;
using Oxide.Game.Rust;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    // TODO add health max plugin
    // TODO add crafting rate based on fatigue plugin
    // TODO add smelt rate based on fatigue plugin (might be tricky)
    // TODO split BroadcastValues(...) into two methods
    
    [Info("Leaderboard", "Default", "0.0.1")]
    [Description("Tracks who is currently winning on the scrap leaderboard.")]
    public class Leaderboard : RustPlugin
    {
        private const double PlayerSynergyDivisorPower = 1.5;
        private Dictionary<string, double> playerScrapCounts;
        
        void Loaded()
        {
            calculatePositions();
            timer.Every( 60.0f, calculatePositions );
        }

        void calculatePositions()
        {
            playerScrapCounts = new Dictionary<string, double>();
            var visitedTcs = new HashSet<BuildingPrivlidge>();
            foreach (var tc in UnityEngine.Object.FindObjectsOfType<BuildingPrivlidge>())
            {
                var buildingOwningTc = tc.GetBuilding().GetDominatingBuildingPrivilege();
                if (visitedTcs.Contains(buildingOwningTc)) continue;
                visitedTcs.Add(buildingOwningTc); 
                foreach (var entity in buildingOwningTc.GetBuilding().decayEntities )
                {
                    if (!(entity is StorageContainer)) continue;
                    if ( buildingOwningTc.authorizedPlayers.Count() < 1 ) continue;
                    var storageContainer = entity as StorageContainer;
                    var playerSynergyDivisor = 
                        Math.Pow( buildingOwningTc.authorizedPlayers.Count(), PlayerSynergyDivisorPower);
                    foreach ( Item item in storageContainer.inventory.itemList )
                    {
                        if (isScrap(item)) 
                            foreach ( var player in buildingOwningTc.authorizedPlayers ) 
                                incrementPlayerScrapCount(player.userid.ToString(), 
                                                          item.amount / playerSynergyDivisor );
                    }
                }
            }
            foreach ( var player in Player.Players.Concat(Player.Sleepers) )
            {
                foreach( Item item in player.inventory.AllItems() )
                {
                    if (isScrap(item)) incrementPlayerScrapCount(player.IPlayer.Id, item.amount);
                }
            }
        }

        bool isScrap(Item item)
        {
            return item?.info?.name == "scrap.item";
        }

        void incrementPlayerScrapCount(String playerId, double amount)
        {
            if (playerScrapCounts.ContainsKey(playerId))
            {
                playerScrapCounts[playerId] += amount;
            }
            else
            {
                playerScrapCounts[playerId] = amount;
            }
        }

        [ChatCommand("leaderboard")]
        private void LeaderboardChat(BasePlayer player, string command, string[] args)
        {
            var leaderboard = new List<KeyValuePair<string, double>>();
            foreach ( var entry in playerScrapCounts )
            {
                leaderboard.Add(
                    new KeyValuePair<string, double>( 
                        covalence.Players.FindPlayerById( entry.Key ).Name, 
                        entry.Value ));
            } 
            leaderboard.Sort( ( x, y ) =>
            {
                // Reverse order (e.g. highest first)
                if ( y.Value < x.Value ) return -1;
                if ( y.Value > x.Value ) return 1;
                return String.Compare( x.Key, y.Key );
            } );


            String leaderboardText = "";
            
            int position = 0;
            double previousValue = Double.MaxValue;
            int playersInCurrentPosition = 1;
            
            foreach (var entry in leaderboard)
            {
                if ( entry.Value != previousValue)
                {
                    previousValue = entry.Value;
                    position += playersInCurrentPosition;
                    playersInCurrentPosition = 1;
                }
                else
                {
                    playersInCurrentPosition++;
                }
                
                var valueText = entry.Value % 1 == 0 ? $"{(int)entry.Value}" : $"{entry.Value:0.##}";
                leaderboardText += $"\n{position}. {entry.Key} {valueText}";
            }
            player.IPlayer.Message( leaderboardText.Substring(1));
        }
    }
}