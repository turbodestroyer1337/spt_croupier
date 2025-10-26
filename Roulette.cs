using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using SPTarkov.Common.Extensions;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Constants;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Dialog;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Services;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Servers.Http;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Xml.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Path = System.IO.Path;

namespace Croupier;

public class Roulette
{
    private static Random _random = new Random();
    public Tier Tier { get; set; }
    public bool NeedsGun { get; set; }
    public List<Item> GeneratedItems { get; set; }
    public List<string> DbMap { get; set; } = new List<string>();

    private List<Item> items;

    public GameDataLoader data = GameDataLoader.Instance;

    public Roulette(Tier tier, bool needsGun)
    {
        Tier = tier;
        NeedsGun = needsGun;
        DbMap = CroupierData.DbItems;
        GeneratedItems = generateItems(tier, needsGun);
    }

    private List<Item> generateItems(Tier tier, bool needsGun)
    {
        items = new List<Item>();
        var color = GetRandomColor(tier);
        var tierString = tier.ToString().ToLower();
        var parentId = new MongoId();
        var variousStuffArray = new List<InventoryItem>();

        // headwear
        string headsetTier = RandomizeTier(tier).ToString().ToLower();
        var allDeadwearItems = data.OutfitsGear.Headwear[headsetTier];
        var headwearArray = AlignChances(RemoveInvalidItems(allDeadwearItems), headsetTier );
        var filteredHeadwearArray = headwearArray.Where(item => item.tags?.Contains(color) == true).ToList();
        if (filteredHeadwearArray.Count == 0) {
            filteredHeadwearArray = headwearArray;
        }
        var headwear = SelectRandomItem(filteredHeadwearArray);
        if (headwear != null) {
            ComposeModular(headwear, parentId);
        }

        // headset
        var headsetIds = data.OutfitsGear.Headset[tierString].Where(id => DbMap.Contains(id)).ToList();
        string headsetId = headsetIds[_random.Next(headsetIds.Count)];
        if (headsetId != null && headwear != null && headwear.blocks_headset != true) { 
            items.Add(new Item
            {
                Id = new MongoId(),
                Template = headsetId,
                ParentId = parentId,
                SlotId = "main",
                Upd = new Upd
                {
                    StackObjectsCount = 1
                }
            });
        }

        // facecover
        var facecoverIds = data.OutfitsGear.Facemasks[tierString].Where(id => DbMap.Contains(id)).ToList(); ;
        string facecoverId = facecoverIds[_random.Next(facecoverIds.Count)];
        if (facecoverId != null && headwear != null && headwear.blocks_facecover != true) {
            items.Add(new Item
            {
                Id = new MongoId(),
                Template = facecoverId,
                ParentId = parentId,
                SlotId = "main",
                Upd = new Upd
                {
                    StackObjectsCount = 1
                }
            });
        }

        // backpack
        var backpackId = new MongoId();
        var backpackTier = RandomizeTier(tier).ToString().ToLower();
        var allBackpackItems = data.OutfitsGear.Backpacks[backpackTier];
        var backpackArray = AlignChances(RemoveInvalidItems(allBackpackItems), backpackTier);
        var filteredBackpackArray = backpackArray.Where(item => item.tags?.Contains(color) == true).ToList();
        if (filteredBackpackArray.Count == 0) {
            filteredBackpackArray = backpackArray;
        }
        var backpack = SelectRandomItem(filteredBackpackArray);
        if (backpack != null) {
            items.Add(new Item
            {
                Id = backpackId,
                Template = backpack.id,
                SlotId = "main",
                ParentId = parentId,
                Upd = new Upd
                {
                    StackObjectsCount = 1,
                    Repairable = new UpdRepairable()
                }
            });
        }

        // chestrig
        var chestrigId = new MongoId();
        var chestrigTier = RandomizeTier(tier).ToString().ToLower();
        var allChestrigItems = data.OutfitsGear.Chestrigs[chestrigTier];
        var chestrigArray = AlignChances(RemoveInvalidItems(allChestrigItems), chestrigTier);
        var filteredChestrigArray = chestrigArray.Where(item => item.tags?.Contains(color) == true).ToList();
        if (filteredChestrigArray.Count == 0) {
            filteredChestrigArray = chestrigArray;
        }
        var chestrig = SelectRandomItem(filteredChestrigArray);
        List<int> chestrigGrid = chestrig.grid.ToList();
        if (chestrig != null) {
            ComposeModular(chestrig, parentId, chestrigId);
        }

        // armor
        if (chestrig != null && chestrig.armored != true) {
               var armorTier = RandomizeTier(tier).ToString().ToLower();
               var allArmorItems = data.OutfitsGear.Armor[armorTier];
               var armorArray = AlignChances(RemoveInvalidItems(allArmorItems), armorTier);
               var filteredArmorArray = armorArray.Where(item => item.tags?.Contains(color) == true).ToList();
               if (filteredArmorArray.Count == 0) {
                    filteredArmorArray = armorArray;
               }
               var armor = SelectRandomItem(filteredArmorArray);
               if (armor != null) {
                    ComposeModular(armor, parentId);
               }
        }

        // guns
        if (needsGun)
        {
            var needsSidegun = false;

            // main
            var mainGunId = new MongoId();
            var gunTier = RandomizeTier(tier).ToString().ToLower();
            var gunArray = AlignChances(RemoveInvalidItems(data.Guns[gunTier]), gunTier);
            List<string> gunClasses = new List<string>();
            foreach (var entry in data.Constants.gunClassMultiplier[gunTier])
            {
                string gunClass = entry.Key;
                int multiplier = entry.Value;
                for (int i = 0; i < multiplier; i++)
                {
                    gunClasses.Add(gunClass);
                }
            }
            string selectedGunClass = gunClasses[_random.Next(gunClasses.Count)];
            var filteredGunArray = gunArray.Where(gun => gun.gunClass == selectedGunClass).ToList();
            if (filteredGunArray.Count == 0) {
                filteredGunArray = gunArray;
            }
            var gun = SelectRandomItem(gunArray);

            if (gun != null && gun.presets != null && gun.presets.Count > 0) {
                var gunPreset = gun.presets[_random.Next(gun.presets.Count)];
                var presetWithGunId = new GearItem {
                    id = gun.id,
                    name = gun.name,
                    mods = gunPreset.mods,
                    tarkov_id = mainGunId
                };
                ComposeModular(presetWithGunId, parentId, mainGunId);
                AddMagazine2Gun(gunPreset.mags, mainGunId, gun.ammo);
                AddMagazinesToInventory(gunPreset.mags, gun.ammo, gun.magsCount, gun.id, chestrigId, chestrigGrid, backpackId, backpack.slot);
                variousStuffArray.Add(new InventoryItem {
                    id = gun.ammo[_random.Next(gun.ammo.Count)],
                    slots = 1,
                    StackObjectsCount = RandomizeCount(gun.ammoCount)
                });
                if (gun.needsSideWeapon != null && gun.needsSideWeapon == true) {
                    var sidegunId = new MongoId();
                    var sidegunArray = AlignChances(RemoveInvalidItems(data.SideGuns[gunTier]), gunTier);
                    var sidegun = SelectRandomItem(sidegunArray);
                    if (sidegun != null && sidegun.presets != null && sidegun.presets.Count > 0) {
                        var sidegunPreset = sidegun.presets[_random.Next(sidegun.presets.Count)];
                        var sidegunPresetWithGunId = new GearItem
                        {
                            id = sidegun.id,
                            name = sidegun.name,
                            mods = sidegunPreset.mods,
                            tarkov_id = sidegunId
                        };
                        ComposeModular(sidegunPresetWithGunId, parentId, sidegunId);
                        AddMagazine2Gun(sidegunPreset.mags, sidegunId, sidegun.ammo);
                        AddMagazinesToInventory(sidegunPreset.mags, sidegun.ammo, sidegun.magsCount, sidegun.id, chestrigId, chestrigGrid, backpackId, backpack.slot);
                        variousStuffArray.Add(new InventoryItem {
                            id = sidegun.ammo[_random.Next(sidegun.ammo.Count)],
                            slots = 1,
                            StackObjectsCount = RandomizeCount(sidegun.ammoCount)
                        });
                    }
                }
            }

        }

        foreach (var itemGroup in data.Various[tierString])
        {
            double randomChance = _random.NextDouble() * 100;
            if (itemGroup.chance >= randomChance)
            {
                var item = itemGroup.items[_random.Next(itemGroup.items.Count)];
                variousStuffArray.Add(new InventoryItem {
                    id = item.id,
                    slots = item.slots,
                    StackObjectsCount = 1
                });
            }
        }

        AddItemsToInventory(variousStuffArray, chestrigId, chestrigGrid, backpackId, backpack.slot);

        return items;
    }

    private string GetRandomColor(Tier tier)
    {
        var tierKey = tier.ToString().ToLower();
        var data = GameDataLoader.Instance;
        var colors = data.Constants.colors[tierKey];
        List<string> weightedColors = new List<string>();
        foreach (var colorEntry in colors) {
            string colorName = colorEntry.Key;
            int weight = colorEntry.Value;
            for (int i = 0; i < weight; i++) { weightedColors.Add(colorName); }
        }
        Random random = _random;
        int randomIndex = random.Next(weightedColors.Count);
        return weightedColors[randomIndex];
    }

    private Tier RandomizeTier(Tier currentTier)
    {
        var probabilityMap = new Dictionary<Tier, List<TierProbability>> {
            {
                Tier.Low, new List<TierProbability> {
                    new TierProbability { Value = Tier.Low, Probability = 93 },
                    new TierProbability { Value = Tier.Mid, Probability = 6 },
                    new TierProbability { Value = Tier.Top, Probability = 1 }
                }
            },
            {
                Tier.Mid, new List<TierProbability> {
                    new TierProbability { Value = Tier.Mid, Probability = 92 },
                    new TierProbability { Value = Tier.Low, Probability = 6 },
                    new TierProbability { Value = Tier.Top, Probability = 2 }
                }
            },
            {
                Tier.Top, new List<TierProbability> {
                    new TierProbability { Value = Tier.Top, Probability = 92 },
                    new TierProbability { Value = Tier.Mid, Probability = 6 },
                    new TierProbability { Value = Tier.Low, Probability = 2 }
                }
            }
        };

        if (!probabilityMap.ContainsKey(currentTier))
        {
            throw new ArgumentException("Invalid tier. Expected Low, Mid, or Top.");
        }

        var probabilities = probabilityMap[currentTier];
        Random random = _random;
        double rand = random.NextDouble() * 100;
        double cumulative = 0;
        foreach (var prob in probabilities)
        {
            cumulative += prob.Probability;
            if (rand < cumulative)
            {
                return prob.Value;
            }
        }
        return probabilities[probabilities.Count - 1].Value;
    }

    private List<T> RemoveInvalidItems<T>(List<T> items) where T : class, ICroupierItem
    {
        List<T> validItems = new List<T>();
        foreach (var item in items) {
            string id = null;
            if (item != null) {
                id = item.id;
            }

            if (id == null || DbMap.Contains(id)) {
                validItems.Add(item);
            }
        }

        return validItems;
    }

    private bool IsValidItem(string itemId)
    {
        if (string.IsNullOrEmpty(itemId))
            return false;

        return DbMap.Contains(itemId);
    }

    private List<T> AlignChances<T>(List<T> items, string tier) where T : ICroupierItem
    {
        var data = GameDataLoader.Instance;
        List<T> resultArr = new List<T>();
        Random random = new Random();

        foreach (var item in items)
        {
            double chance = 1.0;
            if (data.Constants.chanceMultiplier.ContainsKey(tier))
            {
                var tierMultipliers = data.Constants.chanceMultiplier[tier];

                if (tierMultipliers.ContainsKey(item.name))
                {
                    chance = tierMultipliers[item.name];
                }
            }
            else if (item.weight != null && item.weight > 0) {
                chance = item.weight.Value;
            }

            double randomNumber = random.NextDouble();
            double fractionalPart = chance % 1;
            if (fractionalPart > 0) {
                if (randomNumber < fractionalPart) {
                    resultArr.Add(item);
                }
                chance = Math.Floor(chance);
            }
            for (int j = 0; j < (int)chance; j++) {
                resultArr.Add(item);
            }
        }

        return resultArr;
    }

    private T SelectRandomItem<T>(List<T> items) where T : class, ICroupierItem
    {
        var validItems = RemoveInvalidItems(items);
        if (validItems.Count == 0)
        {
            return null;
        }
        return validItems[_random.Next(validItems.Count)];
    }


    private void ComposeModular(GearItem item, MongoId parentId, string tarkovId = null)
    {
        var mods = item.mods;
        if (tarkovId != null) item.tarkov_id = tarkovId;
        else {
            item.tarkov_id = new MongoId();
        }

        items.Add(new Item {
            Id = item.tarkov_id,
            Template = item.id,
            SlotId = "main",
            ParentId = parentId,
            Upd = new Upd
            {
                StackObjectsCount = 1
            }
        });

        if (mods != null && mods.Count > 0) {
            foreach (var modSlot in mods) {
                var modItem = SelectRandomModItem(modSlot.items);
                if (modItem != null) {
                    AttachItemMod(item.tarkov_id, modSlot.type, modItem);
                }
            }
        }
    }

    private void AttachItemMod(string parentId, string slotId, object mod)
    {
        if (mod == null)
            return;

        string thisId = new MongoId();
        string modTpl;
        List<ModSlot> childMods = null;

        if (mod is string stringMod)
        {
            modTpl = stringMod;
        }
        else if (mod is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Null) return;
            if (jsonElement.ValueKind == JsonValueKind.String) {
                modTpl = jsonElement.GetString();
            }
            else {
                modTpl = jsonElement.GetProperty("id").GetString();
                if (jsonElement.TryGetProperty("mods", out var modsProperty)) {
                    childMods = JsonSerializer.Deserialize<List<ModSlot>>(modsProperty.GetRawText());
                }
            }
        }
        else if (mod is ModItem modItemObj) {
            modTpl = modItemObj.Id;
            childMods = modItemObj.Mods;
        }
        else return;

        if (string.IsNullOrEmpty(modTpl)) return;
        if (IsValidItem(modTpl)) {
            items.Add(new Item {
                Id = thisId,
                Template = modTpl,
                ParentId = parentId,
                SlotId = slotId,
                Upd = new Upd
                {
                    StackObjectsCount = 1
                }
            });
            if (childMods != null && childMods.Count > 0) {
                foreach (var childModSlot in childMods) {
                    if (childModSlot.items != null && childModSlot.items.Count > 0) {
                        var childModItem = SelectRandomModItem(childModSlot.items);
                        if (childModItem != null) {
                            AttachItemMod(thisId, childModSlot.type, childModItem);
                        }
                    }
                }
            }
        }
    }

    private object SelectRandomModItem(List<object> items)
    {
        if (items == null || items.Count == 0) {
            return null;
        }
        var validItems = items.Where(item => {
            if (item == null) return true;
            if (item is string stringId) {
                return DbMap.Contains(stringId);
            }
            if (item is JsonElement jsonElement) {
                if (jsonElement.ValueKind == JsonValueKind.Null) return true;
                if (jsonElement.ValueKind == JsonValueKind.String) {
                    return DbMap.Contains(jsonElement.GetString());
                }
                if (jsonElement.TryGetProperty("id", out var idProp)) {
                    return DbMap.Contains(idProp.GetString());
                }
            }
            if (item is ModItem modItem) {
                return DbMap.Contains(modItem.Id);
            }
            return false;
        }).ToList();

        if (validItems.Count == 0) return null;
        return validItems[_random.Next(validItems.Count)];
    }

    private void AddMagazine2Gun(List<Magazine> arrU, string parentId, List<string> ammo)
    {
        var arr = arrU.Where(mag => DbMap.Contains(mag.id)).ToList();
        var mags = arr.Where(mag => mag.preffered == true).ToList();
        if (mags.Count == 0) {
            mags = arr;
        }
        if (mags.Count > 0) {
            var mag = mags[_random.Next(mags.Count)];
            string patronId = ammo[_random.Next(ammo.Count)];
            string magId = new MongoId();
            items.Add(new Item {
                Id = magId,
                Template = mag.id,
                ParentId = parentId,
                SlotId = "mod_magazine",
                Upd = new Upd {
                    StackObjectsCount = 1
                }
            });

            items.Add(new Item
            {
                Id = new MongoId(),
                Template = patronId,
                ParentId = magId,
                SlotId = "cartridges",
                Upd = new Upd {
                    StackObjectsCount = mag.cartridges
                }
            });
        }
    }

    private int GetFreeChestrigSlot(List<int> chestrigGrid, int slots)
    {
        int slot = chestrigGrid.IndexOf(slots);

        if (slot == -1) {
            for (int j = 0; j < chestrigGrid.Count; j++) {
                if (chestrigGrid[j] >= slots) {
                    slot = j;
                    break;
                }
            }
        }

        return slot;
    }

    private void AddMagazinesToInventory( List<Magazine> magazinesArrUnf, List<string> ammoArr, int count, string gunId, string chestrigId, List<int> chestrigGrid, string backpackId, string backpackSlot) {
        List<(Magazine mag, int slot, bool isBackpack)> finalMagsList = new List<(Magazine, int, bool)>();
        int magazinesLeft = count;

        var magazinesArr = magazinesArrUnf.Where(mag => DbMap.Contains(mag.id)).ToList();

        int mags4sCount = chestrigGrid.Count(val => val == 4);
        int mags3sCount = chestrigGrid.Count(val => val == 3);

        bool hasSmallMags = magazinesArr.Any(mag => mag.slots == 2) || magazinesArr.Any(mag => mag.slots == 1);

        if (hasSmallMags || mags4sCount > 0)
        {
            // 4x mags
            if (magazinesLeft > 0 && mags4sCount > 0 && magazinesArr.Any(mag => mag.slots == 4))
            {
                for (int i = 0; i < mags4sCount && magazinesLeft > 0; i++)
                {
                    var mags4x = magazinesArr.Where(mag => mag.slots == 4).ToList();
                    var mag = mags4x[_random.Next(mags4x.Count)];
                    int chestrigSlot = chestrigGrid.IndexOf(4) + 1;
                    finalMagsList.Add((mag, chestrigSlot, false)); // false = chestrig
                    chestrigGrid[chestrigGrid.IndexOf(4)] = 0;
                    magazinesLeft--;
                }
            }

            // 3x mags
            if (magazinesLeft > 0 && mags3sCount > 0 && magazinesArr.Any(mag => mag.slots == 3))
            {
                for (int i = 0; i < mags3sCount && magazinesLeft > 0; i++)
                {
                    var mags3x = magazinesArr.Where(mag => mag.slots == 3).ToList();
                    var mag = mags3x[_random.Next(mags3x.Count)];
                    int chestrigSlot = chestrigGrid.IndexOf(3) + 1;
                    finalMagsList.Add((mag, chestrigSlot, false));
                    chestrigGrid[chestrigGrid.IndexOf(3)] = 0;
                    magazinesLeft--;
                }
            }

            // other mags (1x and 2x)
            if (magazinesArr.Any(mag => mag.slots == 2) || magazinesArr.Any(mag => mag.slots == 1))
            {
                for (int i = 0; i < magazinesLeft; i++)
                {
                    var availableMags = magazinesArr.Where(mag => mag.slots <= 2).ToList();
                    if (availableMags.Count == 0) break;

                    var mag = availableMags[_random.Next(availableMags.Count)];

                    int slot = GetFreeChestrigSlot(chestrigGrid, mag.slots);
                    if (slot != -1)
                    {
                        finalMagsList.Add((mag, slot + 1, false));
                        chestrigGrid[slot] = chestrigGrid[slot] - mag.slots;
                    }
                }
            }
        }
        else
        {
            // if only 4/3x mags are available, and there is no space in chestrig - add them to backpack
            for (int i = 0; i < magazinesLeft; i++)
            {
                var mag = magazinesArr[_random.Next(magazinesArr.Count)];
                finalMagsList.Add((mag, 0, true)); // true = backpack, slot не важен
            }
        }

        foreach (var (mag, slot, isBackpack) in finalMagsList)
        {
            string magId = new MongoId();
            string parentId = isBackpack ? backpackId : chestrigId;
            string slotId = isBackpack ? backpackSlot : slot.ToString();

            items.Add(new Item
            {
                Id = magId,
                Template = mag.id,
                ParentId = parentId,
                SlotId = slotId,
                Upd = new Upd
                {
                    StackObjectsCount = 1
                }
            });

            string patronId = ammoArr[_random.Next(ammoArr.Count)];

            items.Add(new Item
            {
                Id = new MongoId(),
                Template = patronId,
                ParentId = magId,
                SlotId = "cartridges",
                Upd = new Upd
                {
                    StackObjectsCount = mag.cartridges
                }
            });
        }
    }

    private void AddItemsToInventory( List<InventoryItem> itemsArr, string chestrigId, List<int> chestrigGrid, string backpackId, string backpackSlot)
    {
        var validItems = itemsArr.Where(item => DbMap.Contains(item.id)).ToList();
        foreach (var item in validItems) {
            int slot = GetFreeChestrigSlot(chestrigGrid, item.slots);
            string parentId;
            string slotId;

            if (slot != -1) {
                parentId = chestrigId;
                slotId = (slot + 1).ToString();
                chestrigGrid[slot] -= item.slots;
            } else {
                parentId = backpackId;
                slotId = backpackSlot;
            }

            items.Add(new Item {
                Id = new MongoId(),
                Template = item.id,
                ParentId = parentId,
                SlotId = slotId,
                Upd = new Upd
                {
                    StackObjectsCount = item.StackObjectsCount
                }
            });
        }
    }


    private int RandomizeCount(int max)
    {
        if (max <= 0) return 0;

        double rand = _random.NextDouble();

        if (rand < 0.4)
            return max;
        else if (rand < 0.7)
            return Math.Max(1, max - 1);
        else if (rand < 0.9)
            return Math.Max(0, max - 2);
        else
            return 0;
    }

    private class TierProbability
    {
        public Tier Value { get; set; }
        public int Probability { get; set; }
    }
}
