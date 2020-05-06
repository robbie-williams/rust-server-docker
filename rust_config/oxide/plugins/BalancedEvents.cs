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
    [Info("BalancedEvents", "Default", "0.0.1")]
    [Description("Generates events based on player count")]
    public class BalancedEvents : RustPlugin
    {  
        private const double OnePlayerOnlineEventRatio = 0.25;
        private const double MaxPlayerRatioForNormalDrops = 0.5;

        private enum Season{ None, Easter, Halloween, Xmas }
        private class EventRange
        {
            public EventRange( double minMinutes, double maxMinutes )
            {
                this.minMinutes = minMinutes;
                this.maxMinutes = maxMinutes;
            }

            public double minMinutes { get; }
            public double maxMinutes { get; }
        }

        private class EventCounter
        {
            private double target;
            private double counter;
            
            public EventCounter(double target)
            {
                this.target = target;
                this.counter = 0.0;
            }

            public void reset(double target)
            {
                this.target = target;
                this.counter = 0.0;
            }

            public bool inc(double amount)
            {
                if (amount<0) throw new Exception("amount must not be < 0");
                counter += amount;
                if (counter >= target) return true;
                else return false;
            }
        }

        private enum EventType
        {
            Bradley,
            Chinook,
            Easter,
            Halloween,
            Heli,
            Plane,
            Santa,
            Ship,
            Xmas
        }

        private const string PrefabCH47 = "assets/prefabs/npc/ch47/ch47scientists.entity.prefab";
        private const string PrefabPlane = "assets/prefabs/npc/cargo plane/cargo_plane.prefab";
        private const string PrefabShip = "assets/content/vehicles/boats/cargoship/cargoshiptest.prefab";
        private const string PrefabPatrol = "assets/prefabs/npc/patrol helicopter/patrolhelicopter.prefab";
        private const string PrefabLaunchSite = "assets/bundled/prefabs/autospawn/monument/large/launch_site_1.prefab";

        private bool configuredServerEvents;
        private bool configuredBradleyEnabled;
        private bool configuredXmasEnabled;

        private Dictionary<EventType, EventRange> eventRanges = new Dictionary<EventType, EventRange>
        {
            [EventType.Bradley] = new EventRange(45, 75),
            [EventType.Chinook] = new EventRange(60, 180),
            [EventType.Easter] = new EventRange(30, 90),
            [EventType.Halloween] = new EventRange(30, 90),
            [EventType.Heli] = new EventRange(120, 240),
            [EventType.Plane] = new EventRange(30, 90),
            [EventType.Santa] = new EventRange(30, 90),
            [EventType.Ship] = new EventRange(120, 240),
            [EventType.Xmas] = new EventRange(30, 90)
        };

        private Dictionary<EventType, Action> eventHooks;

        private Dictionary<EventType, Action> initialiseEventHooksDictionary()
        {
            return new Dictionary<EventType, Action>()
            {
                [EventType.Bradley] = SpawnTank,
                [EventType.Chinook] = SpawnCH47,
                [EventType.Easter] = SpawnEaster,
                [EventType.Halloween] = SpawnHalloween,
                [EventType.Heli] = SpawnPatrol,
                [EventType.Plane] = SpawnPlane,
                [EventType.Santa] = SpawnSanta,
                [EventType.Ship] = SpawnShip,
                [EventType.Xmas] = SpawnXmas
            };
        }

        private Season season = Season.None;

        private HashSet<BradleyAPC> deadBradleys = new HashSet<BradleyAPC>();

        private Dictionary<EventType, bool> autoDestroyState = initialiseAutoDestroyDictionary();
        private Dictionary<EventType, EventCounter> eventCounters = new Dictionary<EventType, EventCounter>();

        private static Dictionary<EventType, bool> initialiseAutoDestroyDictionary()
        {
            var autoDestroyDictionary = new Dictionary<EventType, bool>();
            foreach (EventType eventType in Enum.GetValues(typeof(EventType)))
            {
                autoDestroyDictionary[eventType] = true;
            }
            return autoDestroyDictionary;
        }

        public BalancedEvents()
        {
            eventHooks = initialiseEventHooksDictionary();
        }

        private bool mapHasLaunchSite()
        {
            return TerrainMeta.Path.Monuments.Select(m => m.name == PrefabLaunchSite).Any();
        }

        private bool isEaster()
        {
            return season == Season.Easter;
        }

        private bool isXmas()
        {
            return season == Season.Xmas;
        }

        private bool isHalloween()
        {
            return season == Season.Halloween;
        }

        void Loaded()
        {  
            configuredServerEvents = ConVar.Server.events;
            configuredBradleyEnabled = ConVar.Bradley.enabled;
            configuredXmasEnabled = ConVar.XMas.enabled;
            ConVar.Server.events = false;
            ConVar.Bradley.enabled = false;
            ConVar.XMas.enabled = false;
            timer.Every( 60, checkServerEventsVariable );
            foreach (EventType eventType in Enum.GetValues(typeof(EventType)))
            {
                if (eventType == EventType.Easter && !isEaster() ||
                    eventType == EventType.Halloween && !isHalloween() ||
                    eventType == EventType.Xmas && !isXmas() ||
                    eventType == EventType.Santa && !isXmas() ||
                    eventType == EventType.Bradley && 
                        ( (BradleySpawner.singleton?.spawned?.IsAlive() ?? false) || !mapHasLaunchSite() ) )
                {
                    continue;
                }
                setEventCounter(eventType);
            }
            var nextMinute = this.nextMinute();
            timer.Once( (float)(nextMinute - DateTimeOffset.UtcNow).TotalSeconds, () => serviceEvents(nextMinute) );
        }

        void serviceEvents( DateTimeOffset scheduledTime )
        {
            var now = DateTimeOffset.UtcNow;
            int ticks = (int)(now - scheduledTime).TotalSeconds / 60 + 1;

            double playerCountRatio = 
                calculatePlayerCountRatio( Player.Players.Count(), ConVar.Server.maxplayers);

            if ( ticks >= 1 && playerCountRatio > 0.0 )
            {
                var incrementAmount = ticks * playerCountRatio;
                var eventsToRemove = new HashSet<EventType>();
                foreach (var counterEntry in eventCounters )
                {
                    if ( counterEntry.Value.inc(incrementAmount))
                    {
                        eventHooks[counterEntry.Key]();
                        if( counterEntry.Key == EventType.Bradley )
                        {
                            eventsToRemove.Add(counterEntry.Key);
                        }
                        else
                        {
                            resetEventCounter(counterEntry);
                        }
                    }
                }
                foreach ( var key in eventsToRemove)
                {
                    eventCounters.Remove(key);
                }
            }
            var nextMinute = this.nextMinute();
            timer.Once( (float)(nextMinute - now).TotalSeconds, () => serviceEvents(nextMinute) );
        }

        private double calculatePlayerCountRatio(int currentPlayers, int maxPlayers)
        {
            if ( currentPlayers == 0 ) return 0.0;
            return Math.Min( 
                1.0, 
                // y = (xB - xA) / (yB - yA) * (x - xA) + yA
                ( 1 - OnePlayerOnlineEventRatio) / 
                (MaxPlayerRatioForNormalDrops * currentPlayers - 1) * 
                (currentPlayers - 1) + 
                OnePlayerOnlineEventRatio);
        }

        DateTimeOffset nextMinute()
        {
            var now = DateTimeOffset.UtcNow;
            var minute = TimeSpan.FromMinutes(1);
            return new DateTimeOffset( (now.Ticks + minute.Ticks - 1) / minute.Ticks * minute.Ticks, now.Offset );
        }

        void resetEventCounter(KeyValuePair<EventType,EventCounter> eventCounter)
        {
            var minutes = gaussRandom(eventRanges[eventCounter.Key].minMinutes, 
                                      eventRanges[eventCounter.Key].maxMinutes);
            Puts(
                $"{eventCounter.Key} will be spawned in {minutes:0.##} minutes " + 
                $"(assuming server at least {MaxPlayerRatioForNormalDrops} full).");
            eventCounter.Value.reset(minutes);
        }

        void setEventCounter(EventType eventType)
        {
            var minutes = gaussRandom(eventRanges[eventType].minMinutes, 
                                      eventRanges[eventType].maxMinutes);
            Puts(
                $"{eventType} will be spawned in {minutes:0.##} minutes " + 
                $"(assuming server at least {MaxPlayerRatioForNormalDrops} full).");
            eventCounters[eventType] = new EventCounter(minutes);
        }

        private void checkServerEventsVariable()
        {
            if (ConVar.Server.events)
            {
                PrintWarning($"At {nowString()} it was discovered that server.events has been set back to true. Setting back to false.");
                ConVar.Server.events = false;
            }

            if (ConVar.Bradley.enabled && autoDestroyState[EventType.Bradley])
            {
                PrintWarning($"At {nowString()} it was discovered that bradley.enabled has been set back to true. Setting back to false.");
                ConVar.Bradley.enabled = false;
            }

            if (ConVar.XMas.enabled)
            {
                PrintWarning($"At {nowString()} it was discovered that xmas.enabled has been set back to true. Setting back to false.");
                ConVar.XMas.enabled = false;
            }
        }

        void Unload()
        {
            ConVar.Server.events = configuredServerEvents;
            ConVar.Bradley.enabled = configuredBradleyEnabled;
            ConVar.XMas.enabled = configuredXmasEnabled;
        }

        private void OnEntitySpawned(SupplySignal entity)
        {
            ClearAutoDestroy(10, EventType.Plane);
        }
        
        private void OnEntitySpawned(CargoPlane entity)
        {
            Puts("CargoPlane spawned.");
            if (entity.OwnerID == 0 && autoDestroyState[EventType.Plane])
            {
                PrintWarning($"At {nowString()} CargoPlane spawned, but this shouldn't be possible. Killing entity.");
                entity.Kill();
            }
            autoDestroyState[EventType.Plane] = true;
        }
        
        private void OnEntitySpawned(CargoShip entity)
        {
            Puts("CargoShip spawned.");
            if (entity.OwnerID == 0 && autoDestroyState[EventType.Ship])
            {
                PrintWarning($"At {nowString()} CargoShip spawned, but this shouldn't be possible. Killing entity.");
                entity.Kill();
            }
            autoDestroyState[EventType.Ship] = true;
        }
        
        private void OnEntitySpawned(BradleyAPC entity)
        {
            Puts("BradleyAPC spawned.");
            if (entity.OwnerID == 0 && autoDestroyState[EventType.Bradley])
            {
                PrintWarning($"At {nowString()} BradleyAPC spawned, but this shouldn't be possible. Killing entity.");
                entity.Kill();
            }
            autoDestroyState[EventType.Bradley] = true;
        }

        private void OnEntityKill(BradleyAPC entity)
        {
            Puts("BradleyAPC destroyed.");
            Puts($"Entity name {entity.name}, {entity.PrefabName}, {entity.IsDead()}");
            if ( entity == BradleySpawner.singleton.spawned && 
                 !deadBradleys.Contains(entity) &&
                 mapHasLaunchSite() )
            {
                 setEventCounter( EventType.Bradley );
            }
            // this hook seems to be firing twice, so let's load each call into a set to capture duplicates
            deadBradleys.Add(entity);
            timer.Once( 15, () => deadBradleys.Remove(entity));
        }
        
        private void OnEntitySpawned(BaseHelicopter entity)
        {
            Puts("BaseHelicopter spawned.");
            if (entity.OwnerID == 0 && autoDestroyState[EventType.Heli])
            {
                PrintWarning($"At {nowString()} BaseHelicopter spawned, but this shouldn't be possible. Killing entity.");
                entity.Kill();
            }
            autoDestroyState[EventType.Heli] = true;
        }
        
        private void OnEntitySpawned(CH47Helicopter entity)
        {
            Puts("CH47Helicopter spawned.");
            if (entity.OwnerID == 0 && autoDestroyState[EventType.Chinook])
            {
                PrintWarning($"At {nowString()} CH47Helicopter spawned, but this shouldn't be possible. Killing entity.");
                timer.Once(1f, () => { entity.Kill(); });
            }
            autoDestroyState[EventType.Chinook] = true;
        }

        private void ClearAutoDestroy(int time, EventType eventType )
        {
            autoDestroyState[eventType] = false;
            if (eventType == EventType.Bradley)
            {
                ConVar.Bradley.enabled = true;
            }
            else if (eventType == EventType.Xmas)
            {
                ConVar.XMas.enabled = true;
            }
            
            timer.Once(time, () =>
            {
                autoDestroyState[eventType] = true;
                if (eventType == EventType.Bradley)
                {
                    ConVar.Bradley.enabled = false;
                }
                else if (eventType == EventType.Xmas)
                {
                    ConVar.XMas.enabled = false;
                }
            });
        }

        private void SpawnTank()
        {
            if (!mapHasLaunchSite())
            {
                PrintWarning("Map has no launch site - cannot spawn Bradley.");
                return;
            }
            ClearAutoDestroy(1, EventType.Bradley);
            BradleySpawner.singleton?.SpawnBradley();
        }

        private void SpawnShip()
        {
            ClearAutoDestroy(1, EventType.Ship);
            var x = TerrainMeta.Size.x;
            var vector3 = Vector3Ex.Range(-1f, 1f);
            vector3.y = 0.0f;
            vector3.Normalize();
            var worldPos = vector3 * (x * 1f);
            worldPos.y = TerrainMeta.WaterMap.GetHeight(worldPos);
            var entity = GameManager.server.CreateEntity(PrefabShip, worldPos);
            entity?.Spawn();
        }

        private void SpawnPatrol()
        {
            ClearAutoDestroy(1, EventType.Heli);
            var position = new Vector3(ConVar.Server.worldsize, 100, ConVar.Server.worldsize) - new Vector3(50f, 0f, 50f);
            var entity = GameManager.server.CreateEntity(PrefabPatrol, position);
            entity?.Spawn();
        }

        private void SpawnPlane()
        {
            ClearAutoDestroy(1, EventType.Plane);
            var position = new Vector3(ConVar.Server.worldsize, 100, ConVar.Server.worldsize) - new Vector3(50f, 0f, 50f);
            var entity = GameManager.server.CreateEntity(PrefabPlane, position);
            entity?.Spawn();
        }
        
        private void SpawnCH47()
        {
            ClearAutoDestroy(1, EventType.Chinook);
            var position = new Vector3(ConVar.Server.worldsize, 100, ConVar.Server.worldsize) - new Vector3(50f, 0f, 50f);
            var entity = GameManager.server.CreateEntity(PrefabCH47, position) as CH47HelicopterAIController;
            entity?.TriggeredEventSpawn();
            entity?.Spawn();
        }

        private void SpawnEaster()
        {
            rust.RunServerCommand("spawn egghunt");
        }

        private void SpawnHalloween()
        {
            rust.RunServerCommand("spawn halloweenhunt");
        }

        private void SpawnSanta()
        {
            rust.RunServerCommand("spawn santasleigh");
        }

        private void SpawnXmas()
        {
            ClearAutoDestroy(1, EventType.Xmas);
            rust.RunServerCommand("xmas.refill");
        }

        private string nowString()
        {
            return DateTime.UtcNow.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", CultureInfo.InvariantCulture);
        }

        private double gaussRandom( double lower, double upper )
        {
            double result;
            do
            {
                double r;
                double u;
                do
                {
                    u = 2 * UnityEngine.Random.value - 1;
                    double v = 2 * UnityEngine.Random.value - 1;
                    r = u * u + v * v;
                }
                while ( r == 0 || r >= 1 );

                double c = Math.Sqrt( -2 * Math.Log( r ) / r );
                result = u * c;
            }
            while ( result < -4.0 || result > 4.0 ); // > 99.9% of values [-4, 4], 
                                                     // so regen values that fall outside 
                                                     // this range to guarantee bounds.

            result = ( result + 4.0 ) / 8.0;

            return lower + ( upper - lower ) * result;

        }

        [ConsoleCommand("balancedevents.spawn")]
        private void CraftCommandConsole(ConsoleSystem.Arg arg)
        {
            Dictionary<string, Action> commands = new Dictionary<string, Action>
            {
                ["bradley"] = SpawnTank,
                ["chinook"] = SpawnCH47,
                ["easter"] = SpawnEaster,
                ["halloween"] = SpawnHalloween,
                ["heli"] = SpawnPatrol,
                ["plane"] = SpawnPlane,
                ["santa"] = SpawnSanta,
                ["ship"] = SpawnShip,
                ["xmas"] = SpawnXmas
            };
            
            if (!arg.HasArgs() || arg.Args.Length != 1)
            {
                PrintError("must provide exactly one argument - either " + String.Join( ",", commands.Keys));
                return;
            }

            string spawnType = arg.Args[0];
            if (!commands.ContainsKey( spawnType ) )
            {
                PrintError($"provided {spawnType} but must provide  either " + String.Join( ",", commands.Keys));
                return;
            }
            commands[spawnType].Invoke();
        }
    }
}