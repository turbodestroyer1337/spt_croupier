using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Croupier;


public enum Tier { Low, Mid, Top }

public class GearItemMod
{
    public string Type { get; set; }
    public List<string> Items { get; set; }
}

public class InventoryItem
{
    public string id { get; set; }
    public int slots { get; set; }
    public int StackObjectsCount { get; set; }
}


public interface ICroupierItem
{
    string id { get; }
    string name { get; set; }
    List<string>? tags { get; set; }
    double? weight { get; set; }
}

public class GearItem : ICroupierItem
{
    public string full_name { get; set; }
    public string id { get; set; }
    public string name { get; set; }
    public List<ModSlot>? mods { get; set; }
    public List<string>? tags { get; set; }
    public double? weight { get; set; }
    public bool? blocks_headset { get; set; } // headwear
    public bool? blocks_facecover { get; set; } // headwear
    public bool? blocks_eyewear { get; set; } // headwear
    public bool? armored { get; set; } // headwear, chestrigs
    public string tarkov_id { get; set; }

    public string? slot { get; set; } // backpacks
    public List<int>? grid { get; set; } // chestrigs
}

public class Constants
{
    public Dictionary<string, Dictionary<string, int>> colors { get; set; }
    public Dictionary<string, Dictionary<string, double>> chanceMultiplier { get; set; }
    public Dictionary<string, Dictionary<string, int>> gunClassMultiplier { get; set; }
}

public class GameDataLoader
{
    private static GameDataLoader _instance;
    private static readonly object _lock = new object();
    public Constants Constants { get; private set; }
    public Dictionary<string, List<WeaponItem>> Guns { get; private set; }
    public Dictionary<string, List<WeaponItem>> SideGuns { get; private set; }
    public Dictionary<string, List<VariousItemGroup>> Various { get; private set; }
    public OutfitGearData OutfitsGear { get; private set; }

    private GameDataLoader()
    {
        LoadAllData();
    }

    public static GameDataLoader Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new GameDataLoader();
                    }
                }
            }
            return _instance;
        }
    }

    private void LoadAllData()
    {
        string basePath = CroupierData.pathToMod;
        string constantsPath = Path.Combine(basePath, "data", "constants.json");
        Constants = LoadJson<Constants>(constantsPath);

        Guns = new Dictionary<string, List<WeaponItem>>
        {
            { "low", LoadJson<List<WeaponItem>>(Path.Combine(basePath, "data", "low", "guns.json")) },
            { "mid", LoadJson<List<WeaponItem>>(Path.Combine(basePath, "data", "mid", "guns.json")) },
            { "top", LoadJson<List<WeaponItem>>(Path.Combine(basePath, "data", "top", "guns.json")) }
        };

        SideGuns = new Dictionary<string, List<WeaponItem>>
        {
            { "low", LoadJson<List<WeaponItem>>(Path.Combine(basePath, "data", "low", "side.json")) },
            { "mid", LoadJson<List<WeaponItem>>(Path.Combine(basePath, "data", "mid", "side.json")) },
            { "top", LoadJson<List<WeaponItem>>(Path.Combine(basePath, "data", "top", "side.json")) }
        };

        Various = new Dictionary<string, List<VariousItemGroup>>
        {
            { "low", LoadJson<List<VariousItemGroup>>(Path.Combine(basePath, "data", "low", "various.json")) },
            { "mid", LoadJson<List<VariousItemGroup>>(Path.Combine(basePath, "data", "mid", "various.json")) },
            { "top", LoadJson<List<VariousItemGroup>>(Path.Combine(basePath, "data", "top", "various.json")) }
        };

        OutfitsGear = new OutfitGearData
        {
            Armor = new Dictionary<string, List<GearItem>>
        {
            { "low", LoadJson<List<GearItem>>(Path.Combine(basePath, "data", "low", "armors.json")) },
            { "mid", LoadJson<List<GearItem>>(Path.Combine(basePath, "data", "mid", "armors.json")) },
            { "top", LoadJson<List<GearItem>>(Path.Combine(basePath, "data", "top", "armors.json")) }
        },
            Backpacks = new Dictionary<string, List<GearItem>>
        {
            { "low", LoadJson<List<GearItem>>(Path.Combine(basePath, "data", "low", "backpacks.json")) },
            { "mid", LoadJson<List<GearItem>>(Path.Combine(basePath, "data", "mid", "backpacks.json")) },
            { "top", LoadJson<List<GearItem>>(Path.Combine(basePath, "data", "top", "backpacks.json")) }
        },
            Chestrigs = new Dictionary<string, List<GearItem>>
        {
            { "low", LoadJson<List<GearItem>>(Path.Combine(basePath, "data", "low", "chestrigs.json")) },
            { "mid", LoadJson<List<GearItem>>(Path.Combine(basePath, "data", "mid", "chestrigs.json")) },
            { "top", LoadJson<List<GearItem>>(Path.Combine(basePath, "data", "top", "chestrigs.json")) }
        },
            Headwear = new Dictionary<string, List<GearItem>>
        {
            { "low", LoadJson<List<GearItem>>(Path.Combine(basePath, "data", "low", "headwear.json")) },
            { "mid", LoadJson<List<GearItem>>(Path.Combine(basePath, "data", "mid", "headwear.json")) },
            { "top", LoadJson<List<GearItem>>(Path.Combine(basePath, "data", "top", "headwear.json")) }
        },
            Eyewear = LoadJson<Dictionary<string, List<string>>>(Path.Combine(basePath, "data", "eyewear.json")),
            Headset = LoadJson<Dictionary<string, List<string>>>(Path.Combine(basePath, "data", "headsets.json")),
            Facemasks = LoadJson<Dictionary<string, List<string>>>(Path.Combine(basePath, "data", "facemasks.json"))
        };
    }

    private T LoadJson<T>(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) {
                return default(T);
            }
            string jsonString = File.ReadAllText(filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };
            var result = JsonSerializer.Deserialize<T>(jsonString, options);

            return result;
        }
        catch (Exception ex) {
            return default(T);
        }
    }
}

public class OutfitGearData
{
    public Dictionary<string, List<GearItem>> Armor { get; set; }
    public Dictionary<string, List<GearItem>> Backpacks { get; set; }
    public Dictionary<string, List<GearItem>> Chestrigs { get; set; }
    public Dictionary<string, List<GearItem>> Headwear { get; set; }
    public Dictionary<string, List<string>> Eyewear { get; set; }
    public Dictionary<string, List<string>> Headset { get; set; }
    public Dictionary<string, List<string>> Facemasks { get; set; }
}

public class ModItem
{
    public string Id { get; set; }
    public List<ModSlot>? Mods { get; set; }
}


public class WeaponItem : GearItem
{
    [JsonPropertyName("class")]
    public string gunClass { get; set; }

    public List<string> ammo { get; set; }
    public string reloadMode { get; set; }
    public string firemode { get; set; }
    public string caliber { get; set; }
    public int ammoCount { get; set; }
    public int magsCount { get; set; }
    public bool? needsSideWeapon { get; set; }

    public List<WeaponPreset> presets { get; set; }
}

public class VariousItemGroup
{
    public List<VariousItemData> items { get; set; }
    public double chance { get; set; }
    public string comment { get; set; }
}

public class VariousItemData
{
    public string id { get; set; }
    public int slots { get; set; }
}

public class WeaponPreset
{
    public string comment { get; set; }
    public List<Magazine> mags { get; set; }
    public List<ModSlot> mods { get; set; }
}

public class Magazine
{
    public string id { get; set; }
    public int slots { get; set; }
    public int cartridges { get; set; }
    public bool preffered { get; set; }
}
public class ModSlot
{
    public string type { get; set; }
    public List<object> items { get; set; }
}
