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
    
    [Info("Fatigue", "Default", "0.0.1")]
    [Description("Sets player fatigue based on time in game.")]
    public class Fatigue : RustPlugin
    {
        private const int SECONDS_PER_FATIGUE_POINT = 60 * 60;

        [PluginReference]
        private Plugin CustomBar;
        [PluginReference]
        private Plugin FatigueGather;

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

        void Loaded()
        {  
            LoadState();
            foreach( var player in BasePlayer.activePlayerList )
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
            timer.Every( 5.0f, () => ServicePlayers() );
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
            if ( player.IsNpc ) return null;
            
            PlayerInactive( player );
            return null;
        }

        object OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if( player.IsNpc ) return null;

            RetryAction(
                () => PlayerInactive(player),
                () => player.IsDead(),
                0.2f,
                10 );
            return null;
        }

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            PlayerInactive( player );
        }

        void OnPlayerSleepEnded(BasePlayer player)
        {
            if ( player.IsNpc ) return;

            PlayerActive( player );
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

        private void ServicePlayers()
        {
            List<string> activeListIds = new List<string>(fatigueStore.Keys);
            foreach ( var id in activeListIds ) ServicePlayer(id);
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

            BroadcastValues( 
                player,
                info == null ? 
                    0.0 :
                    info.SecondsActiveAtStateChange +
                            ( DateTimeOffset.UtcNow - info.StateChangeTimestamp ).TotalSeconds );
        }

        private void ServiceInactivePlayer(string id, FatigueInfo info)
        {
            BasePlayer player = BasePlayer.Find(id);
            
            if (player != null && player.IPlayer.IsConnected && !(player.IPlayer.IsSleeping || player.IsDead()))
            {
                Debug.LogWarning($"Inactive player with id: {player.IPlayer.Id} " +
                                    "is actually connected, awake and alive!" );
                return;
            }
            
            var calculatedSecondsActive =
                Math.Max(
                    0.0,
                    info.SecondsActiveAtStateChange -
                            ( DateTimeOffset.UtcNow - info.StateChangeTimestamp ).TotalSeconds);

            if (calculatedSecondsActive == 0.0 )
            {
                fatigueStore.Remove(id);
            }
            
            if( player != null && player.IPlayer.IsConnected ) 
            {
                BroadcastValues(player, calculatedSecondsActive );
            }
        }

        private void BroadcastValues( BasePlayer player, double secondsActive )
        {
            int fatigueLevel = 
                Math.Max( 
                    0,
                    (int)Math.Ceiling( 8.0 - secondsActive / SECONDS_PER_FATIGUE_POINT));
            RetryAction(
                () => CustomBar.Call( "SetValue", player, fatigueLevel ),
                () => CustomBar != null,
                1.0f,
                5 );
            RetryAction(
                () => FatigueGather.Call( "SetValue", player, fatigueLevel ),
                () => FatigueGather != null,
                1.0f,
                5 );
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
            FatigueInfo fatigueInfo = null;
            fatigueStore.TryGetValue(player.IPlayer.Id, out fatigueInfo);

            if ( fatigueInfo?.CurrentState == FatigueInfo.FatigueState.ACTIVE )
            {
                Debug.LogWarning($"{player.IPlayer.Name} [{player.IPlayer.Id}] already active!");
                return;
            }

            var activeSeconds = 
                fatigueInfo == null ? 
                    0.0 : 
                    fatigueInfo.SecondsActiveAtStateChange -
                            ( DateTimeOffset.UtcNow - fatigueInfo.StateChangeTimestamp ).TotalSeconds;
            fatigueStore[player.IPlayer.Id] = 
                new FatigueInfo( 
                    FatigueInfo.FatigueState.ACTIVE, 
                    DateTimeOffset.UtcNow, 
                    activeSeconds );              
        }

        private void PlayerInactive( BasePlayer player )
        {
            FatigueInfo fatigueInfo = null;
            
            if( !fatigueStore.TryGetValue(player.IPlayer.Id, out fatigueInfo) )
            {
                fatigueStore[player.IPlayer.Id] = 
                    new FatigueInfo( 
                        FatigueInfo.FatigueState.INACTIVE, 
                        DateTimeOffset.UtcNow,
                        0.0 );
                return;
            }

            fatigueStore[player.IPlayer.Id] = 
                new FatigueInfo( 
                    FatigueInfo.FatigueState.INACTIVE, 
                    DateTimeOffset.UtcNow,
                    Math.Min(
                        SECONDS_PER_FATIGUE_POINT * 8.0, 
                        fatigueInfo.SecondsActiveAtStateChange +
                            ( DateTimeOffset.UtcNow - fatigueInfo.StateChangeTimestamp ).TotalSeconds ) );
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
                calculatedSecondsActive = Math.Min(calculatedSecondsActive, 8 * SECONDS_PER_FATIGUE_POINT);
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
                    Math.Min(SECONDS_PER_FATIGUE_POINT * 8.0, secondsActive - secondsSinceTimeStamp);
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