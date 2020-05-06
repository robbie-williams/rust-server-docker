using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("FatigueText", "Default", "0.0.1")]
    [Description("Displays fatigue text.")]
    public class FatigueText : RustPlugin
    {
        private static readonly string CustomBarName = CuiHelper.GetGuid();
        private static readonly string CustomBarTextName = CuiHelper.GetGuid();
        private static readonly string CustomBarImageName = CuiHelper.GetGuid();

        private Dictionary<string, int> fatigueLevelStore = new Dictionary<string, int>();
        private Dictionary<string, DateTimeOffset> fatigueNextLevelTimeStore = new Dictionary<string, DateTimeOffset>();
        private Dictionary<string, Dictionary<string, object>> panels = new Dictionary<string, Dictionary<string, object>>();
        private Dictionary<string, Timer> playerRedrawTimers = new Dictionary<string, Timer>();
        private Dictionary<int, float> fatigueGatherTable;

        void Loaded()
        {
            RetryAction(
                () => fatigueGatherTable = (Dictionary<int, float>)Manager.GetPlugin("FatigueGather")?.Call("GetFatigueGatherTable"),
                () => Manager.GetPlugin("FatigueGather") != null,
                0.5f,
                20 );
        }

        void OnPluginLoaded(Plugin plugin)
        {
            if (plugin.Name.Equals("FatigueGather"))
            {
                fatigueGatherTable = (Dictionary<int, float>)plugin.Call("GetFatigueGatherTable");
            }
        }

        void OnPluginUnloaded(String name)
        {
            if (name.Equals("FatigueGather"))
            {
                fatigueGatherTable = null;
            }
        }

        void Unload()
        {
            foreach ( var player in BasePlayer.activePlayerList )
            {
                DestroyPanel( player );
                fatigueLevelStore.Remove(player.IPlayer.Id);
                fatigueNextLevelTimeStore.Remove(player.IPlayer.Id);
                Timer existingTimer;
                if (playerRedrawTimers.TryGetValue(player.IPlayer.Id, out existingTimer))
                {
                    existingTimer.Destroy();
                    playerRedrawTimers.Remove(player.IPlayer.Id);
                }
            }
        }
    
        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            DestroyPanel( player );
            fatigueLevelStore.Remove( player.IPlayer.Id);
            fatigueNextLevelTimeStore.Remove( player.IPlayer.Id);
            Timer existingTimer;
            if (playerRedrawTimers.TryGetValue(player.IPlayer.Id, out existingTimer))
            {
                existingTimer.Destroy();
                playerRedrawTimers.Remove(player.IPlayer.Id);
            }
        }
        
        private void RetryAction(Action action, Func<bool> condition, float delay, int attempts)
        {
            if (condition.Invoke())
            {
                action.Invoke();
            }
            else
            {
                if ( attempts > 1 )
                {
                    timer.Once( delay, () => RetryAction( action, condition, delay, attempts - 1) );
                }
            }
        }

        private void OnFatigueLevel(BasePlayer player, int value)
        {
            if ( value < 0 || value > 8)
            {
                Debug.LogWarning($"Value provided ({value}) was outside allowed range  [0 - 8]");
                return;
            }

            if (player?.IPlayer?.Id == null)
            {
                Debug.LogWarning($"Not a valid player: {player}");
                return;
            }

            fatigueLevelStore[player.IPlayer.Id] = value;
            DrawOrRedrawPanel(player);
        }

        private void OnNextFatigueLevelTime(BasePlayer player, DateTimeOffset dateTimeOffset)
        {
            if (player?.IPlayer?.Id == null)
            {
                Debug.LogWarning($"Not a valid player: {player}");
                return;
            }

            fatigueNextLevelTimeStore[player.IPlayer.Id] = dateTimeOffset;
            DrawOrRedrawPanel(player);
        }

        private void OnClearFatigueLevelTime(BasePlayer player)
        {
            if (player?.IPlayer?.Id == null)
            {
                Debug.LogWarning($"Not a valid player: {player}");
                return;
            }

            fatigueNextLevelTimeStore.Remove(player.IPlayer.Id);
            DrawOrRedrawPanel(player);
        }


        private string BuildPanelText(BasePlayer player)
        {
            int fatigueLevel;
            if (!fatigueLevelStore.TryGetValue(player.IPlayer.Id, out fatigueLevel))
            {
                return "";
            }

            var panelText = $"Fatigue Level: {fatigueLevel}.";
            
            float gatherRate = 0.0f;
            if (fatigueGatherTable?.TryGetValue(fatigueLevel, out gatherRate) ?? false)
            {
                panelText += $" Gather: {gatherRate:0%}.";
            }
            
            DateTimeOffset nextLevelTime;
            if (fatigueNextLevelTimeStore.TryGetValue(player.IPlayer.Id, out nextLevelTime))
            {
                var duration = nextLevelTime - DateTimeOffset.UtcNow;
                int hours = duration.Hours;
                int minutes = duration.Minutes;
                int seconds = duration.Seconds;
                panelText +=" Next Level: ";
                if( hours > 0 ) panelText += $"{hours:D2}";
                panelText += $"{minutes:D2}:{seconds:D2}";
            }
            return panelText;
        }

        private void DrawOrRedrawPanel(BasePlayer player)
        {
            if (panels.ContainsKey(player.IPlayer.Id))
            {
                RedrawPanel(player);
            }
            else
            {
                DrawPanel(player);
            }
        }

        private void RedrawPanel(BasePlayer player)
        {          
            Timer existingTimer;
            if (playerRedrawTimers.TryGetValue(player.IPlayer.Id, out existingTimer))
            {
                existingTimer.Destroy();
                playerRedrawTimers.Remove(player.IPlayer.Id);
            }
            
            var panelText = BuildPanelText(player);
            (panels[player.IPlayer.Id]["barText"] as CuiElement)
                .Components
                .OfType<CuiTextComponent>()
                .Single()
                .Text = BuildPanelText(player);
                
            CuiHelper.DestroyUi(player, CustomBarTextName);
            if (!String.IsNullOrEmpty(panelText) && !player.IsSleeping() && player.IsConnected)
            {
                CuiHelper.AddUi(player, new List<CuiElement>{ panels[player.IPlayer.Id]["barText"] as CuiElement } );
                DateTimeOffset nextLevelTime;
                if (fatigueNextLevelTimeStore.TryGetValue(player.IPlayer.Id, out nextLevelTime))
                {
                    var duration = (nextLevelTime - DateTimeOffset.UtcNow).TotalSeconds;
                    playerRedrawTimers[player.IPlayer.Id] = timer.Once( (float)(duration % 1.0), () => DrawOrRedrawPanel(player) );
                }
            }
        }

        private void DrawPanel(BasePlayer player)
        {
            Timer existingTimer;
            if (playerRedrawTimers.TryGetValue(player.IPlayer.Id, out existingTimer))
            {
                existingTimer.Destroy();
                playerRedrawTimers.Remove(player.IPlayer.Id);
            }
            
            var elements = new CuiElementContainer();
            
            var customBar =  new CuiElement
            {
                Name = CustomBarName,
                Parent = "Hud",
                Components =
                {
                    new CuiImageComponent{ Color = "0.7 0.7 0.7 0.3" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "0 0",
                        OffsetMin = "0 0",
                        OffsetMax = "220 12"
                    }
                }
            };
            elements.Add(customBar);
            
            var barText = new CuiElement
            {
                Name = CustomBarTextName,
                Parent = CustomBarName,
                Components = 
                {
                    new CuiTextComponent{ Text = BuildPanelText(player), Color = "0.8 0.8 0.8 0.7", Align = TextAnchor.MiddleLeft, FontSize = 10 },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "18 0",
                        OffsetMax = "0 0"
                    }
                }
            };
            elements.Add(barText);

            var barImage = new CuiElement
            {
                Name = CustomBarImageName,
                Parent = CustomBarName,
                Components = 
                {
                    new CuiRawImageComponent{ Url = "http://i.imgur.com/XIIZkqD.png", Color = "0.7 0.7 0.7 0.8" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "0 0",
                        OffsetMin = "0 0",
                        OffsetMax = "12 12"
                    }
                }
            };
            elements.Add(barImage);

            CuiHelper.AddUi(player, elements);
            
            panels[player.IPlayer.Id] = new Dictionary<string, object>
            {
                ["elements"] = elements,
                ["barText"] = barText
            };
            
            DateTimeOffset nextLevelTime;
            if (fatigueNextLevelTimeStore.TryGetValue(player.IPlayer.Id, out nextLevelTime))
            {
                var duration = (nextLevelTime - DateTimeOffset.UtcNow).TotalSeconds;
                playerRedrawTimers[player.IPlayer.Id] = timer.Once( (float)(duration % 1.0), () => DrawOrRedrawPanel(player) );
            }
        }

        private void DestroyPanel(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, CustomBarName);
            if(!panels.Remove(player.IPlayer.Id))
            {
                Debug.LogWarning($"Unable to remove fatigue panel for player {player.IPlayer.Name} [{player.IPlayer.Id}]!");
            }
        }
    }
}