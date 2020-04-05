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
    [Info("CustomBar", "Default", "0.0.1")]
    [Description("Displays a custom bar.")]
    public class CustomBar : RustPlugin
    {
        private static readonly string CUSTOM_BAR_NAME = CuiHelper.GetGuid();
        private static readonly string CUSTOM_BAR_FILL_NAME = CuiHelper.GetGuid();
        private static readonly string CUSTOM_BAR_NUMBER_NAME = CuiHelper.GetGuid();
        private static readonly string CUSTOM_BAR_IMAGE_NAME = CuiHelper.GetGuid();
     
        private const double BAR_FILL_MIN = 0.1300;
        private const double BAR_FILL_MAX = 0.9900;
        private const double BAR_FILL_BOTTOM = 0.1250;
        private const double BAR_FILL_TOP = 0.8500;

        void OnPlayerConnected(BasePlayer player)
        {
            RunWhenPlayerLoaded( player, () => CreateBar( player ) );
        }

        void Loaded()
        {
            foreach( var player in BasePlayer.activePlayerList )
            {
                RunWhenPlayerLoaded( player, () => CreateBar( player ) );
            }
        }

        void Unload()
        {
            foreach( var player in BasePlayer.activePlayerList )
            {
                DestroyBar( player );
            }
        }
    
        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            DestroyBar( player );
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

        private void SetValue( BasePlayer player, int value )
        {
            if( value < 0 || value > 8)
            {
                Debug.LogWarning($"Value provided ({value}) was outside allowed range  [0 - 8]");
                return;
            }
            RetryAction( 
                () => Redraw(player, value),
                () => Bars.ContainsKey(player.IPlayer.Id),
                0.5f,
                10 );
        }

        private void Redraw( BasePlayer player, int value )
        {
            var barFillRecTrans = 
                (Bars[player.IPlayer.Id]["barFill"] as CuiElement)
                    .Components
                    .OfType<CuiRectTransformComponent>()
                    .Single();
            
            var barFill = 
                BAR_FILL_MIN + ( value / 8.0 ) * (BAR_FILL_MAX - BAR_FILL_MIN);
            barFillRecTrans.AnchorMin = $"{BAR_FILL_MIN} {BAR_FILL_BOTTOM}";
            barFillRecTrans.AnchorMax = $"{barFill} {BAR_FILL_TOP}";
            (Bars[player.IPlayer.Id]["barNumber"] as CuiElement)
                .Components
                .OfType<CuiTextComponent>()
                .Single()
                .Text = value.ToString();
                
            CuiHelper.DestroyUi(player, CUSTOM_BAR_FILL_NAME);
            if( value != 0)
            {
                CuiHelper.AddUi(player, new List<CuiElement>{ Bars[player.IPlayer.Id]["barFill"] as CuiElement } );
            }
            
            CuiHelper.DestroyUi(player, CUSTOM_BAR_NUMBER_NAME);
            CuiHelper.AddUi(player, new List<CuiElement>{ Bars[player.IPlayer.Id]["barNumber"] as CuiElement } );
        }

        private Dictionary<string, Dictionary<string, object>> Bars = new Dictionary<string, Dictionary<string, object>>();

        private void CreateBar( BasePlayer player )
        {
            if(Bars.ContainsKey(player.IPlayer.Id))
            {
                Debug.LogWarning($"Custom bar for player {player.IPlayer.Name} [{player.IPlayer.Id}] already exists!");
            }
            
            var elements = new CuiElementContainer();
            
            var customBar =  new CuiElement
            {
                Name = CUSTOM_BAR_NAME,
                Parent = "Hud",
                Components =
                {
                    new CuiImageComponent{ Color = "0.7 0.7 0.7 0.3" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.0130 0.0231",
                        AnchorMax = "0.1625 0.0583"
                    }
                }
            };
            elements.Add(customBar);
            
            var barFill = new CuiElement
            {
                Name = CUSTOM_BAR_FILL_NAME,
                Parent = CUSTOM_BAR_NAME,
                Components = 
                {
                    new CuiImageComponent{ Color = "0.631 0.365 0.918 0.85" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = $"{BAR_FILL_MIN} {BAR_FILL_BOTTOM}",
                        AnchorMax = $"{BAR_FILL_MAX} {BAR_FILL_TOP}"
                    }
                }
            };
            elements.Add(barFill);
            
            var barNumber = new CuiElement
            {
                Name = CUSTOM_BAR_NUMBER_NAME,
                Parent = CUSTOM_BAR_NAME,
                Components = 
                {
                    new CuiTextComponent{ Text = "8", Color = "0.8 0.8 0.8 0.7", Align = TextAnchor.MiddleLeft },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.1672 0.000",
                        AnchorMax = "1.0000 1.000"
                    }
                }
            };
            elements.Add(barNumber);

            var barImage = new CuiElement
            {
                Name = CUSTOM_BAR_IMAGE_NAME,
                Parent = CUSTOM_BAR_NAME,
                Components = 
                {
                    new CuiRawImageComponent{ Url = "http://i.imgur.com/XIIZkqD.png", Color = "0.7 0.7 0.7 0.8" },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.0209 0.2105",
                        AnchorMax = "0.1045 0.8158"
                    }
                }
            };
            elements.Add(barImage);

            CuiHelper.AddUi(player, elements);
            Bars[player.IPlayer.Id] = new Dictionary<string, object>
            {
                ["elements"] = elements,
                ["barFill"] = barFill,
                ["barNumber"] = barNumber
            };
        }

        private void DestroyBar( BasePlayer player )
        {
            CuiHelper.DestroyUi(player, CUSTOM_BAR_NAME);
            if(!Bars.Remove(player.IPlayer.Id))
            {
                Debug.LogWarning($"Unable to remove custom bar for player {player.IPlayer.Name} [{player.IPlayer.Id}]!");
            }
        }
    }
}