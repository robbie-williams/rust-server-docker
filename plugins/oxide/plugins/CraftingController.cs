using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Crafting Controller", "Mughisi/nivex/Whispers88", "2.5.6")]
    [Description("Allows modification of crafting times and which items can be crafted")]
    class CraftingController : RustPlugin
    {
        #region Configuration Data

        private bool configChanged;

        // Plugin settings
        private const string DefaultChatPrefix = "Crafting Controller";
        private const string DefaultChatPrefixColor = "#008000ff";

        public string ChatPrefix { get; private set; }
        public string ChatPrefixColor { get; private set; }

        // Plugin options
        private const float DefaultCraftingRate = 100;
        private const float DefaultCraftingExperience = 100;
        private const bool DefaultAdminInstantBulkCraft = false;
        private const bool DefaultModeratorInstantBulkCraft = false;
        private const bool DefaultPlayerInstantBulkCraft = false;
        private const bool DefaultAdminInstantCraft = true;
        private const bool DefaultModeratorInstantCraft = false;
        private const bool DefaultCompleteCurrentCraftingOnShutdown = false;
        private const bool DefaultAllowCraftingWhenInventoryIsFull = false;

        public float CraftingRate { get; private set; }
        public float CraftingExperience { get; private set; }
        public bool AdminInstantBulkCraft { get; private set; }
        public bool ModeratorInstantBulkCraft { get; private set; }
        public bool PlayerInstantBulkCraft { get; private set; }
        public bool AdminInstantCraft { get; private set; }
        public bool ModeratorInstantCraft { get; private set; }
        public bool CompleteCurrentCrafting { get; private set; }
        public bool ShowCraftNotes { get; private set; }
        public bool AllowCraftingWhenInventoryIsFull { get; private set; }

        // Plugin options - blocked items
        private static readonly List<object> DefaultBlockedItems = new List<object>();
        private static readonly Dictionary<string, object> DefaultIndividualRates = new Dictionary<string, object>();

        public List<string> BlockedItems { get; private set; }
        public Dictionary<string, float> IndividualRates { get; private set; }

        // Plugin messages
        private const string DefaultCurrentCraftingRate = "The crafting rate is set to {0}%.";
        private const string DefaultModifyCraftingRate = "The crafting rate is now set to {0}%.";
        private const string DefaultModifyCraftingRateItem = "The crafting rate for {0} is now set to {1}%.";
        private const string DefaultModifyError = "The new crafting rate must be a number. 0 is instant craft, 100 is normal and 200 is double!";
        private const string DefaultCraftBlockedItem = "{0} is blocked and can not be crafted!";
        private const string DefaultNoItemSpecified = "You need to specify an item for this command.";
        private const string DefaultNoItemRate = "You need to specify an item and a new crafting rate for this command.";
        private const string DefaultInvalidItem = "{0} is not a valid item. Please use the name of the item as it appears in the item list. Ex: Camp Fire";
        private const string DefaultBlockedItem = "{0} has already been blocked!";
        private const string DefaultBlockSucces = "{0} has been blocked from crafting.";
        private const string DefaultUnblockItem = "{0} is not blocked!";
        private const string DefaultUnblockSucces = "{0} is no longer blocked from crafting.";
        private const string DefaultNoPermission = "You don't have permission to use this command.";
        private const string DefaultShowBlockedItems = "The following items are blocked: ";
        private const string DefaultNoBlockedItems = "No items have been blocked.";
        private const string DefaultRemovedItem = "Removed individual crafting rate for {0}";
        private const string DefaultNoSlots = "You don't have enough slots to craft! Need {0}, have {1}!";

        public string CurrentCraftingRate { get; private set; }
        public string ModifyCraftingRate { get; private set; }
        public string ModifyCraftingRateItem { get; private set; }
        public string ModifyError { get; private set; }
        public string CraftBlockedItem { get; private set; }
        public string NoItemSpecified { get; private set; }
        public string NoItemRate { get; private set; }
        public string InvalidItem { get; private set; }
        public string BlockedItem { get; private set; }
        public string BlockSucces { get; private set; }
        public string UnblockItem { get; private set; }
        public string UnblockSucces { get; private set; }
        public string NoPermission { get; private set; }
        public string ShowBlockedItems { get; private set; }
        public string NoBlockedItems { get; private set; }
        public string RemovedItem { get; private set; }
        public string NoSlots { get; private set; }

        #endregion

        List<ItemBlueprint> blueprintDefinitions = new List<ItemBlueprint>();

        public Dictionary<string, float> Blueprints { get; } = new Dictionary<string, float>();

        List<ItemDefinition> itemDefinitions = new List<ItemDefinition>();

        public List<string> Items { get; } = new List<string>();

        private void Loaded() => LoadConfigValues();

        private void OnServerInitialized()
        {
            blueprintDefinitions = ItemManager.bpList;
            foreach (var bp in blueprintDefinitions)
                Blueprints.Add(bp.targetItem.shortname, bp.time);

            itemDefinitions = ItemManager.itemList;
            foreach (var itemdef in itemDefinitions)
                Items.Add(itemdef.displayName.english);

            UpdateCraftingRate();
        }

        private void Unload()
        {
            foreach (var bp in blueprintDefinitions)
                bp.time = Blueprints[bp.targetItem.shortname];
        }

        protected override void LoadDefaultConfig() => PrintWarning("New configuration file created.");

        private void LoadConfigValues()
        {
            // Plugin settings
            ChatPrefix = GetConfigValue("Settings", "ChatPrefix", DefaultChatPrefix);
            ChatPrefixColor = GetConfigValue("Settings", "ChatPrefixColor", DefaultChatPrefixColor);

            // Plugin options
            ShowCraftNotes = GetConfigValue("Options", "ShowCraftingNotes", true);
            AdminInstantBulkCraft = GetConfigValue("Options", "InstantBulkCraftForAdmins", DefaultAdminInstantBulkCraft);
            ModeratorInstantBulkCraft = GetConfigValue("Options", "InstantBulkCraftForModerators", DefaultModeratorInstantCraft);
            PlayerInstantBulkCraft = GetConfigValue("Options", "InstantBulkCraftIfRateIsZeroForPlayers", DefaultPlayerInstantBulkCraft);
            AdminInstantCraft = GetConfigValue("Options", "InstantCraftForAdmins", DefaultAdminInstantCraft);
            ModeratorInstantCraft = GetConfigValue("Options", "InstantCraftForModerators", DefaultModeratorInstantCraft);
            CraftingRate = GetConfigValue("Options", "CraftingRate", DefaultCraftingRate);
            CraftingExperience = GetConfigValue("Options", "CraftingExperienceRate", DefaultCraftingExperience);
            CompleteCurrentCrafting = GetConfigValue("Options", "CompleteCurrentCraftingOnShutdown", DefaultCompleteCurrentCraftingOnShutdown);
            AllowCraftingWhenInventoryIsFull = GetConfigValue("Options", "AllowCraftingWhenInventoryIsFull", DefaultAllowCraftingWhenInventoryIsFull);

            // Plugin options - blocked items
            var list = GetConfigValue("Options", "BlockedItems", DefaultBlockedItems);
            var dict = GetConfigValue("Options", "IndividualCraftingRates", DefaultIndividualRates);

            BlockedItems = new List<string>();
            foreach (var item in list)
                BlockedItems.Add(item.ToString());

            IndividualRates = new Dictionary<string, float>();
            foreach (var entry in dict)
            {
                float rate;
                if (!float.TryParse(entry.Value.ToString(), out rate)) continue;
                IndividualRates.Add(entry.Key, rate);
            }

            // Plugin messages
            CurrentCraftingRate = GetConfigValue("Messages", "CurrentCraftingRate", DefaultCurrentCraftingRate);
            ModifyCraftingRate = GetConfigValue("Messages", "ModifyCraftingRate", DefaultModifyCraftingRate);
            ModifyCraftingRateItem = GetConfigValue("Messages", "ModifyCraftingRateItem", DefaultModifyCraftingRateItem);
            ModifyError = GetConfigValue("Messages", "ModifyCraftingRateError", DefaultModifyError);
            CraftBlockedItem = GetConfigValue("Messages", "CraftBlockedItem", DefaultCraftBlockedItem);
            NoItemSpecified = GetConfigValue("Messages", "NoItemSpecified", DefaultNoItemSpecified);
            NoItemRate = GetConfigValue("Messages", "NoItemRate", DefaultNoItemRate);
            InvalidItem = GetConfigValue("Messages", "InvalidItem", DefaultInvalidItem);
            BlockedItem = GetConfigValue("Messages", "BlockedItem", DefaultBlockedItem);
            BlockSucces = GetConfigValue("Messages", "BlockSucces", DefaultBlockSucces);
            UnblockItem = GetConfigValue("Messages", "UnblockItem", DefaultUnblockItem);
            UnblockSucces = GetConfigValue("Messages", "UnblockSucces", DefaultUnblockSucces);
            NoPermission = GetConfigValue("Messages", "NoPermission", DefaultNoPermission);
            ShowBlockedItems = GetConfigValue("Messages", "ShowBlockedItems", DefaultShowBlockedItems);
            NoBlockedItems = GetConfigValue("Messages", "NoBlockedItems", DefaultNoBlockedItems);
            RemovedItem = GetConfigValue("Messages", "RemovedItem", DefaultRemovedItem);
            NoSlots = GetConfigValue("Messages", "NoSlotsLeft", DefaultNoSlots);

            if (!configChanged) return;
            Puts("Configuration file updated.");
            SaveConfig();
        }

        private void SendHelpText(BasePlayer player) => SendChatMessage(player, CurrentCraftingRate, CraftingRate);

        private void OnServerQuit()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (CompleteCurrentCrafting)
                    CompleteCrafting(player);

                CancelAllCrafting(player);
            }
        }

        private static void CompleteCrafting(BasePlayer player)
        {
            if (player.inventory.crafting.queue.Count == 0) return;
            player.inventory.crafting.FinishCrafting(player.inventory.crafting.queue.First.Value);
            player.inventory.crafting.queue.RemoveFirst();
        }

        private static void CancelAllCrafting(BasePlayer player)
        {
            var crafter = player.inventory.crafting;
            foreach (var task in crafter.queue)
                crafter.CancelTask(task.taskUID, true);
        }

        private void UpdateCraftingRate()
        {
            foreach (var bp in blueprintDefinitions)
            {
                if (IndividualRates.ContainsKey(bp.targetItem.displayName.english))
                {
                    //Puts("{0}: {1} -> {2}", bp.targetItem.shortname, bp.time, IndividualRates[bp.targetItem.displayName.english]);
                    if (IndividualRates[bp.targetItem.displayName.english] != 0f)
                        bp.time = Blueprints[bp.targetItem.shortname] * (IndividualRates[bp.targetItem.displayName.english] / 100);
                    else bp.time = 0f;
                }
                else
                {
                    //Puts("{0}: {1} -> {2}", bp.targetItem.shortname, bp.time, Blueprints[bp.targetItem.shortname] * CraftingRate / 100);
                    if (CraftingRate != 0f)
                        bp.time = Blueprints[bp.targetItem.shortname] * (CraftingRate / 100);
                    else bp.time = 0f;
                }
            }
        }

        private object OnItemCraft(ItemCraftTask task, BasePlayer crafter)
        {
            var itemname = task.blueprint.targetItem.displayName.english;
            ///
            var player = task.owner;
            var target = task.blueprint.targetItem;
            var name = target.shortname;
            var stacks = GetStacks(target, task.amount * task.blueprint.amountToCreate);
            var slots = FreeSlots(player);
            ///
            if (BlockedItems.Contains(itemname))
            {
                task.cancelled = true;
                SendChatMessage(crafter, CraftBlockedItem, itemname);
                foreach (var item in task.takenItems) player.GiveItem(item);
                //foreach (var amount in task.blueprint.ingredients) crafter.inventory.GiveItem(ItemManager.CreateByItemID(amount.itemid, (int)amount.amount * task.amount));
                return false;
            }

            if (!AllowCraftingWhenInventoryIsFull && !HasPlace(slots, stacks))
            {
                task.cancelled = true;
                SendChatMessage(crafter, NoSlots, stacks.Count, slots);
                foreach (var item in task.takenItems) player.GiveItem(item);
                return false;
            }

            if (AdminInstantBulkCraft && task.owner.net.connection.authLevel == 2)
            {
                InstantBulkCraft(player, target, stacks, task.skinID);
                task.cancelled = true;
                return false;
            }

            if (ModeratorInstantBulkCraft && task.owner.net.connection.authLevel == 1)
            {
                InstantBulkCraft(player, target, stacks, task.skinID);
                task.cancelled = true;
                return false;
            }

            if (AdminInstantCraft && task.owner.net.connection.authLevel == 2) task.endTime = 1f;
            if (ModeratorInstantCraft && task.owner.net.connection.authLevel == 1) task.endTime = 1f;
            if (PlayerInstantBulkCraft && task.blueprint.time <= 0f)
            {
                InstantBulkCraft(player, target, stacks, task.skinID);
                task.cancelled = true;
                return false;
            }

            return null;
        }

        //

        private int FreeSlots(BasePlayer player)
        {
            var slots = player.inventory.containerMain.capacity + player.inventory.containerBelt.capacity;
            var taken = player.inventory.containerMain.itemList.Count + player.inventory.containerBelt.itemList.Count;
            return slots - taken;
        }

        private List<int> GetStacks(ItemDefinition item, int amount)
        {
            var list = new List<int>();
            var maxStack = item.stackable;

            while (amount > maxStack)
            {
                amount -= maxStack;
                list.Add(maxStack);
            }

            list.Add(amount);

            return list;
        }

        private bool HasPlace(int slots, List<int> stacks)
        {
            if (slots - stacks.Count < 0)
            {
                return false;
            }

            return slots > 0;
        }

        //

        private void InstantBulkCraft(BasePlayer player, ItemDefinition item, List<int> stacks, int craftSkin)
        {
            var skin = ItemDefinition.FindSkin(item.itemid, craftSkin);
            foreach (var stack in stacks)
            {
                var x = ItemManager.Create(item, stack, craftSkin != 0 && skin == 0uL ? (ulong)craftSkin : skin);
                player.GiveItem(x);
                if (ShowCraftNotes) player.Command(string.Concat(new object[] { "note.inv ", item.itemid, " ", stack }), new object[0]);
            }
        }

        private void OnItemCraftFinished(ItemCraftTask task, Item item)
        {
            if (task.owner.net.connection.authLevel == 0) return;
            var crafter = task.owner.inventory.crafting;
            if (crafter.queue.Count == 0) return;
            crafter.queue.First().endTime = 1f;
        }

        #region Helper methods

        private void SendChatMessage(BasePlayer player, string message, params object[] args) => player?.SendConsoleCommand("chat.add", -1, string.Format($"<color={ChatPrefixColor}>{ChatPrefix}</color>: {message}", args), 1.0);

        T GetConfigValue<T>(string category, string setting, T defaultValue)
        {
            var data = Config[category] as Dictionary<string, object>;
            if (data == null)
            {
                data = new Dictionary<string, object>();
                Config[category] = data;
                configChanged = true;
            }
            object value;
            if (!data.TryGetValue(setting, out value))
            {
                value = defaultValue;
                data[setting] = value;
                configChanged = true;
            }
            return (T)Convert.ChangeType(value, typeof(T));
        }

        void SetConfigValue<T>(string category, string setting, T newValue)
        {
            var data = Config[category] as Dictionary<string, object>;
            object value;
            if (data != null && data.TryGetValue(setting, out value))
            {
                value = newValue;
                data[setting] = value;
                configChanged = true;
            }
            SaveConfig();
        }

        #endregion
    }
}