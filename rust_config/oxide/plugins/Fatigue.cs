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
    
    [Info("Fatigue", "Default", "0.0.1")]
    [Description("Sets player fatigue based on time in game.")]
    public class Fatigue : RustPlugin
    {
        private const int SecondsPerFatiguePoint = 60 * 60;

        private class FatigueInfo
        {
            public enum FatigueState{ ACTIVE, INACTIVE };

            public FatigueInfo( FatigueState CurrentState, DateTimeOffset StateChangeTimestamp, double SecondsActiveAtStateChange )
            {
                this.CurrentState = CurrentState;
                this.StateChangeTimestamp = StateChangeTimestamp;
                this.SecondsActiveAtStateChange = SecondsActiveAtStateChange;
            }
            public FatigueState CurrentState { get; }
            public DateTimeOffset StateChangeTimestamp { get; }
            public double SecondsActiveAtStateChange { get; }
        }

        private Dictionary<string, FatigueInfo> fatigueStore = new Dictionary<string, FatigueInfo>();
        private Dictionary<string, Timer> playerServiceTimers = new Dictionary<string, Timer>();
        private Dictionary<string, int> playerLastLevelBroadcast = new Dictionary<string, int>();

        void Loaded()
        {  
            LoadState();
            foreach ( var player in BasePlayer.activePlayerList )
            {
                if ( player.IPlayer.IsSleeping || player.IsDead()  )
                {
                    PlayerInactive( player );
                }
                else
                {
                    PlayerActive( player );
                }
            }
            serviceDisconnectedInactivePlayers();
            timer.Every( 300, serviceDisconnectedInactivePlayers );
            
        }

        private void serviceDisconnectedInactivePlayers()
        {
            foreach ( var id in new List<String>(fatigueStore.Keys) )
            {
                BasePlayer fatigueStorePlayer = BasePlayer.Find(id);
                if (fatigueStorePlayer == null ||
                    (!fatigueStorePlayer?.IPlayer.IsConnected ?? false))
                {
                    ServicePlayer( id );
                }
            }
        }

        void Unload()
        {
            SaveState();
        } 

        void OnServerSave()
        {
            SaveState();
        }

        object OnPlayerSleep(BasePlayer player)
        {
            if ( player.IsNpc || !player.IsConnected ) return null;
            PlayerInactive( player );
            return null;
        }

        object OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if ( player.IsNpc || !player.IsConnected ) return null;
            RetryAction(
                () => PlayerInactive(player),
                () => player.IsDead(),
                0.2f,
                10 );
            return null;
        }

        void OnPluginLoaded(Plugin plugin)
        {
            if (plugin.Name.Equals("FatigueText") || plugin.Name.Equals("FatigueGather"))
            {
                playerLastLevelBroadcast.Clear();
                foreach (var id in new List<String>(fatigueStore.Keys))
                {
                    ServicePlayer(id);
                }
            }
        }

        void OnPlayerConnected(BasePlayer player)
        {
            RunWhenPlayerLoaded( player, () => ServicePlayer(player.IPlayer.Id) );
        }

        private void RunWhenPlayerLoaded(BasePlayer player, Action action)
        {
            if (player.HasPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot))
            {
                timer.Once(0.5f, () => RunWhenPlayerLoaded(player, action));
            }
            else
            {
                action.Invoke();
            }
        }

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            PlayerInactive( player );
        }

        void OnPlayerSleepEnded(BasePlayer player)
        {
            if ( player.IsNpc || !player.IsConnected ) return;
            PlayerActive( player );
        }

        private void ServicePlayer(string id)
        {  
            FatigueInfo info = null;
            if(!fatigueStore.TryGetValue(id, out info)) return;

            if ( info?.CurrentState == FatigueInfo.FatigueState.ACTIVE )
            {
                ServiceActivePlayer(id, info);
            }
            else
            {
                ServiceInactivePlayer(id, info);
            }
        }

        private void ServiceActivePlayer(string id, FatigueInfo info)
        {
            BasePlayer player = BasePlayer.Find(id);
            if ( player == null || !player.IPlayer.IsConnected )
            {
                Debug.LogWarning($"Could not find active player with id: {player.IPlayer.Id}!");
                return;
            }

            Timer existingTimer;
            if (playerServiceTimers.TryGetValue(player.IPlayer.Id, out existingTimer))
            {
                existingTimer.Destroy();
                playerServiceTimers.Remove(player.IPlayer.Id);
            }

            var secondsActive = 
                info.SecondsActiveAtStateChange + ( DateTimeOffset.UtcNow - info.StateChangeTimestamp ).TotalSeconds;
            var level = Math.Min(8, (int)Math.Floor(secondsActive / SecondsPerFatiguePoint));
            var nextLevelSecondsActive = ( level + 1) * SecondsPerFatiguePoint;
            var nextLevelSecondsFromNow = nextLevelSecondsActive - secondsActive;
            DateTimeOffset? nextLevelTime = DateTimeOffset.UtcNow.AddSeconds( nextLevelSecondsFromNow );

            int lastLevelBroadcast;
            if (!playerLastLevelBroadcast.TryGetValue(player.IPlayer.Id, out lastLevelBroadcast) ||
                level != lastLevelBroadcast)
            {
                BroadcastValues( player, level, level < 8 ? nextLevelTime : null );
                playerLastLevelBroadcast[player.IPlayer.Id] = level;
            }
            if ( level < 8 )
            {
                playerServiceTimers[player.IPlayer.Id] = 
                        timer.Once( (float)nextLevelSecondsFromNow, () => ServicePlayer(id) );
            }
        }

        private void ServiceInactivePlayer(string id, FatigueInfo info)
        {
            BasePlayer player = BasePlayer.Find(id);
            Boolean playerConnected = player != null && (player?.IPlayer?.IsConnected ?? false);
            
            Timer existingTimer;
            if (playerServiceTimers.TryGetValue(id, out existingTimer))
            {
                existingTimer.Destroy();
                playerServiceTimers.Remove(id);
            }
            
            var secondsActive = 
                info.SecondsActiveAtStateChange - ( DateTimeOffset.UtcNow - info.StateChangeTimestamp ).TotalSeconds;
            var level = Math.Max(0, (int)Math.Floor(secondsActive / SecondsPerFatiguePoint));
            var nextLevelSecondsActive = level * SecondsPerFatiguePoint;
            var nextLevelSecondsFromNow = secondsActive - nextLevelSecondsActive;

            DateTimeOffset? nextLevelTime = DateTimeOffset.UtcNow.AddSeconds( nextLevelSecondsFromNow );

            int lastLevelBroadcast;
            if ( secondsActive <= 0 )
            {
                playerLastLevelBroadcast.Remove(id);
                if (playerConnected) BroadcastValues( player, 0, null );
                fatigueStore.Remove(id);
                return;
            }

            if (playerConnected)
            { 
                if (!playerLastLevelBroadcast.TryGetValue(id, out lastLevelBroadcast) ||
                    level != lastLevelBroadcast )
                {
                    BroadcastValues( player, level, nextLevelTime );
                    playerLastLevelBroadcast[id] = level;
                }
                playerServiceTimers[id] = 
                    timer.Once( (float)nextLevelSecondsFromNow, () => ServicePlayer(id) );
            }  
        }

        private void BroadcastValues( BasePlayer player, int fatigueLevel, DateTimeOffset? nextLevelTime )
        {     
            Interface.CallHook("OnFatigueLevel", player, fatigueLevel );   
            if (fatigueLevel == 8)
            {
                Interface.CallHook("OnClearFatigueLevelTime", player);
                return;
            }

            Interface.CallHook("OnNextFatigueLevelTime", player, nextLevelTime);
        }

        private void RetryAction(Action action, Func<bool> condition, float delay, int attempts)
        {
            if (condition.Invoke())
            {
                action.Invoke();
            }
            else
            {
                if( attempts > 1 )
                {
                    timer.Once( delay, () => RetryAction( action, condition, delay, attempts - 1) );
                }
            }
        }

        private void PlayerActive(BasePlayer player)
        {
            FatigueInfo fatigueInfo;
            fatigueStore.TryGetValue(player.IPlayer.Id, out fatigueInfo);

            if ( fatigueInfo?.CurrentState == FatigueInfo.FatigueState.ACTIVE )
            {
                Debug.LogWarning($"{player.IPlayer.Name} [{player.IPlayer.Id}] already active!");
                return;
            }

            Timer existingTimer;
            if (playerServiceTimers.TryGetValue(player.IPlayer.Id, out existingTimer))
            {
                existingTimer.Destroy();
                playerServiceTimers.Remove(player.IPlayer.Id);
            }

            var secondsActive = 
                fatigueInfo == null ? 
                    0 : 
                    Math.Max(
                        0,
                        fatigueInfo.SecondsActiveAtStateChange -
                            ( DateTimeOffset.UtcNow - fatigueInfo.StateChangeTimestamp ).TotalSeconds);
            
            var info = 
                new FatigueInfo( 
                    FatigueInfo.FatigueState.ACTIVE, 
                    DateTimeOffset.UtcNow, 
                    secondsActive );
            fatigueStore[player.IPlayer.Id] = info;
            playerLastLevelBroadcast.Remove(player.IPlayer.Id);
            ServicePlayer(player.IPlayer.Id);
        }

        private void PlayerInactive( BasePlayer player )
        {
            FatigueInfo fatigueInfo;
            if ( !fatigueStore.TryGetValue(player.IPlayer.Id, out fatigueInfo) ||
                 fatigueInfo.CurrentState == FatigueInfo.FatigueState.INACTIVE )
            {
                return;
            }

            Timer existingTimer;
            if (playerServiceTimers.TryGetValue(player.IPlayer.Id, out existingTimer))
            {
                existingTimer.Destroy();
                playerServiceTimers.Remove(player.IPlayer.Id);
            }
        
            var secondsActive =
                Math.Min(
                    SecondsPerFatiguePoint * 8.0, 
                    fatigueInfo.SecondsActiveAtStateChange +
                        ( DateTimeOffset.UtcNow - fatigueInfo.StateChangeTimestamp ).TotalSeconds );

            playerLastLevelBroadcast.Remove(player.IPlayer.Id);
            if ( secondsActive > 0 )
            {
                var info = 
                    new FatigueInfo( 
                        FatigueInfo.FatigueState.INACTIVE, 
                        DateTimeOffset.UtcNow,
                        secondsActive );
                fatigueStore[player.IPlayer.Id] = info;
                ServicePlayer(player.IPlayer.Id);
            }
            else
            {
                fatigueStore.Remove(player.IPlayer.Id);
            }
        }

        void SaveState()
        {
            DynamicConfigFile dataFile = Interface.Oxide.DataFileSystem.GetDatafile("Fatigue");
            dataFile.Clear();
            foreach ( var entry in fatigueStore )
            {
                double calculatedSecondsActive;
                if (entry.Value.CurrentState == FatigueInfo.FatigueState.ACTIVE)
                {
                    calculatedSecondsActive =
                        entry.Value.SecondsActiveAtStateChange +
                            ( DateTimeOffset.UtcNow - entry.Value.StateChangeTimestamp ).TotalSeconds;
                }
                else
                {
                    calculatedSecondsActive =
                        entry.Value.SecondsActiveAtStateChange -
                            ( DateTimeOffset.UtcNow - entry.Value.StateChangeTimestamp ).TotalSeconds;
                }
                calculatedSecondsActive = Math.Min(calculatedSecondsActive, 8 * SecondsPerFatiguePoint);
                dataFile[entry.Key, "SecondsActive"] = calculatedSecondsActive;
                dataFile[entry.Key, "Timestamp"] = 
                    DateTime.UtcNow.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", CultureInfo.InvariantCulture);
            }
            dataFile.Save();
        }

        void LoadState()
        {
            DynamicConfigFile dataFile = Interface.Oxide.DataFileSystem.GetDatafile("Fatigue");
            foreach ( var entry in dataFile )
            {
                var secondsActive = (double)dataFile[entry.Key, "SecondsActive"];
                var timestampRaw = dataFile[entry.Key, "Timestamp"];
                DateTimeOffset timestamp;
                if (timestampRaw is DateTime)
                {
                    timestamp = ((DateTime)timestampRaw).ToUniversalTime();
                }
                else if (timestampRaw is string)
                {
                    timestamp = 
                        DateTimeOffset.Parse((string)timestampRaw, CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
                }
                else
                {
                    throw new Exception($"timestamp is unsupported type: {timestampRaw.GetType().FullName}");
                }
                var secondsSinceTimeStamp = (DateTimeOffset.UtcNow - timestamp).TotalSeconds;
                var calculatedSecondsActive = 
                    Math.Min(SecondsPerFatiguePoint * 8.0, secondsActive - secondsSinceTimeStamp);
                if (calculatedSecondsActive > 0.0 )
                {
                    fatigueStore[entry.Key] = 
                        new FatigueInfo( 
                            FatigueInfo.FatigueState.INACTIVE, 
                            DateTimeOffset.UtcNow,
                            calculatedSecondsActive);
                }
            }
        }
    }
}