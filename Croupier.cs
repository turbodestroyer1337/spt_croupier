using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Path = System.IO.Path;

namespace Croupier;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.turbodestroyer.croupier";
    public override string Name { get; init; } = "Croupier";
    public override string Author { get; init; } = "turbodestroyer";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("2.0.4");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.2");

    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; } = true;
    public override string? License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class Croupier(ModHelper modHelper, ISptLogger<Croupier> logger, AddCustomTraderHelper addCustomTraderHelper, ImageRouter imageRouter, ConfigServer configServer, TimeUtil timeUtil, ICloner cloner, DatabaseService databaseService, LocaleService localeService) : IOnLoad
{
    private readonly TraderConfig _traderConfig = configServer.GetConfig<TraderConfig>();
    private readonly RagfairConfig _ragfairConfig = configServer.GetConfig<RagfairConfig>();
    private TraderBase traderBase;
    private SPTarkov.Server.Core.Models.Spt.Server.DatabaseTables db;
    private MongoId original_item_id = (MongoId)"5b7c710788a4506dec015957";

    public Task OnLoad()
    {
        // add trader
        CroupierData.pathToMod = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var traderImagePath = Path.Combine(CroupierData.pathToMod, "res/croupier.jpg");
        var traderBase = modHelper.GetJsonDataFromFile<TraderBase>(CroupierData.pathToMod, "db/base.json");
        CroupierData.Config = modHelper.GetJsonDataFromFile<ModConfig>(CroupierData.pathToMod, "config.json");
        CroupierData.BlackmarketFailMessages = modHelper.GetJsonDataFromFile<List<string>>(CroupierData.pathToMod, "data/blackmarket_fail_messages.json");
        CroupierData.BlackmarketSuccessMessages = modHelper.GetJsonDataFromFile<List<string>>(CroupierData.pathToMod, "data/blackmarket_messages.json");
        CroupierData.CroupierSuccessMessages = modHelper.GetJsonDataFromFile<List<string>>(CroupierData.pathToMod, "data/messages.json");
        imageRouter.AddRoute(traderBase.Avatar.Replace(".jpg", ""), traderImagePath);
        addCustomTraderHelper.SetTraderUpdateTime(_traderConfig, traderBase, timeUtil.GetHoursAsSeconds(1), timeUtil.GetHoursAsSeconds(2));
        _ragfairConfig.Traders.TryAdd(traderBase.Id, true);

        // add items (boxes)
        db = databaseService.GetTables();
        var dbLocales = databaseService.GetTables().Locales.Global;
        var items = modHelper.GetJsonDataFromFile<List<DbItem>>(CroupierData.pathToMod, "db/items.json");
        foreach (var item in items)
        {
            AddNewDbItem(item);
            foreach (var (localeKey, localeKvP) in dbLocales)
            {
                localeKvP.AddTransformer(lazyloadedLocaleData =>
                {
                    lazyloadedLocaleData.Add($"{item.id} Name", item.name);
                    lazyloadedLocaleData.Add($"{item.id} ShortName", item.shortName);
                    lazyloadedLocaleData.Add($"{item.id} Description", item.description);
                    return lazyloadedLocaleData;
                });
            }
        }

        // add trader assort
        addCustomTraderHelper.AddTraderWithEmptyAssortToDb(traderBase);
        addCustomTraderHelper.AddTraderToLocales(traderBase, "Croupier", "The best loadouts in Tarkov.");
        var assort = modHelper.GetJsonDataFromFile<TraderAssort>(CroupierData.pathToMod, "db/assort.json");
        addCustomTraderHelper.OverwriteTraderAssort(traderBase.Id, assort);

        CroupierData.weatherConfig = configServer.GetConfig<WeatherConfig>();
        return Task.CompletedTask;
    }
    
    private void AddNewDbItem(DbItem item)
    {
        var newItem = cloner.Clone(db.Templates.Items[original_item_id]);
        newItem.Id = item.id;
        newItem.Name = item.objName;
        newItem.Properties.BackgroundColor = item.backgroundColor;
        newItem.Properties.ShortName = item.shortName;
        newItem.Properties.Description = item.description;
        newItem.Properties.Name = item.name;
        newItem.Properties.Width = 5;
        newItem.Properties.Height = 3;
        newItem.Properties.CanSellOnRagfair = false;
        newItem.Properties.NotShownInSlot = true;
        newItem.Properties.ExaminedByDefault = false;
        newItem.Properties.IsUngivable = true;
        //newItem.Properties.IsUnsaleable = true;
        newItem.Properties.Unlootable = true;
        newItem.Properties.Prefab.Path = item.prefab;
        db.Templates.Items.Add(newItem.Id, newItem);
    }
}

[Injectable(TypePriority = -69)]
public class CroupierBeforeRouter : StaticRouter {
    private static readonly Dictionary<string, List<Item>> _itemCache = new();

    public CroupierBeforeRouter(JsonUtil jsonUtil, ISptLogger<CroupierBeforeRouter> logger, ProfileHelper profileHelper) : base(jsonUtil, [
            new RouteAction<ItemEventRouterRequest>(
                "/client/game/profile/items/moving",
                async (url, info, sessionID, output) => {
                    if ((info.Data != null) && (CroupierData.Config.FleaSellEnabled)) {
                        var needsCache = false;
                        for (int i = 0; i < info.Data.Count; i++) {
                            var eventData = info.Data[i];
                            //logger.Info($"CroupierBeforeRouter: Processing event {i} with action {eventData.Action} for session {sessionID}");
                            if (eventData.Action == "TradingConfirm") {
                                var tradingData = eventData as ProcessBaseTradeRequestData;
                                if ((tradingData != null) && (tradingData.Type == "sell_to_trader")) {
                                    var transactionId = tradingData.GetType().GetProperty("TransactionId")?.GetValue(tradingData);
                                    if (transactionId?.ToString() == "1337bb0dd843363fcd1be869") {
                                        needsCache = true;
                                        break;
                                    }
                                }
                            }
                        }
                        if (needsCache) {
                            var pmcProfile = profileHelper.GetPmcProfile((MongoId)sessionID);
                            var pmcItems = pmcProfile.Inventory.Items;
                            _itemCache[sessionID.ToString()] = new List<Item>(pmcItems);
                        }
                    }
                    return output ?? "";
                }
            )
        ])
    {
        logger.Success("CroupierBeforeRouter loaded!");
    }

    public static List<Item>? GetCachedInventory(string sessionId) {
        //Console.WriteLine($"CroupierBeforeRouter: Retrieving cached inventory for session {sessionId}");
        return _itemCache.TryGetValue(sessionId, out var itemList) ? itemList : null;
    }

    public static void ClearCache(string sessionId) {
        //Console.WriteLine($"CroupierBeforeRouter: Clearing cached inventory for session {sessionId}");
        _itemCache.Remove(sessionId);
    }

    public static void ClearAllCache() {
        //Console.WriteLine($"CroupierBeforeRouter: Clearing all cached inventories");
        _itemCache.Clear();
    }
}



[Injectable]
public class CroupierStaticRouter : StaticRouter
{
    private static ItemHelper _itemHelper;
    public CroupierStaticRouter(JsonUtil jsonUtil, ISptLogger<CroupierStaticRouter> logger, InventoryHelper inventoryHelper, PaymentService paymentService, MailSendService mailSend, ProfileHelper profileHelper, ItemHelper itemHelper, DatabaseService databaseService) : base(jsonUtil, [
            new RouteAction<ItemEventRouterRequest>(
                "/client/game/profile/items/moving",
                async (url, info, sessionID, output) => {
                    if (info.Data != null) {
                        var outputJSON = JsonDocument.Parse(output ?? "{}");
                        for (int i = 0; i < info.Data.Count; i++) {
                            var eventData = info.Data[i];
                            if (eventData.Action == "TradingConfirm") {
                                var tradingData = eventData as ProcessBaseTradeRequestData;
                                var transactionId = tradingData.GetType().GetProperty("TransactionId")?.GetValue(tradingData);
                                if ((tradingData != null) && (transactionId?.ToString() == "1337bb0dd843363fcd1be869")) {
                                    var pmcData = profileHelper.GetPmcProfile((MongoId)sessionID);
                                    if (tradingData.Type == "sell_to_trader" && CroupierData.Config.FleaSellEnabled) {
                                        if (_itemHelper == null) _itemHelper = itemHelper;
                                        var pmcLevel = pmcData.Info.Level;
                                        var eventItems = tradingData.GetType().GetProperty("Items")?.GetValue(eventData);
                                        var sellPrice = tradingData.GetType().GetProperty("Price")?.GetValue(tradingData);
                                        var cachedInventory = CroupierBeforeRouter.GetCachedInventory(sessionID.ToString());
                                        var price_diff = compareBlackmarketPrices(eventItems, cachedInventory);
                                        if (price_diff > 0) {
                                            if (!CroupierData.Config.FleaSellEnabledOnlyAfter15Lvl || pmcLevel >= 15) {
                                                var money_to_add = price_diff;
                                                var messages = CroupierData.BlackmarketSuccessMessages;
                                                var random_message = messages != null && messages.Count > 0 ? messages[new Random().Next(messages.Count)] : "Nice doing business with you! Here's your cut after tax: ";
                                                var newMoney = new List<Item>();
                                                var parentMesId = new MongoId();
                                                while (money_to_add > 0) {
                                                    var tmp_price = money_to_add - 1000000;
                                                    var moneyToAdd = 0;
                                                    if (tmp_price >= 0) {
                                                        money_to_add = tmp_price;
                                                        moneyToAdd = 1000000;
                                                    } else {
                                                        moneyToAdd = money_to_add;
                                                        money_to_add = 0;
                                                    }
                                                    var newMoneyStack = new Item {
                                                        Id = new MongoId(),
                                                        Template = (MongoId)"5449016a4bdc2d6f028b456f",
                                                        ParentId = parentMesId,
                                                        SlotId = "main",
                                                        Upd = new Upd {
                                                            StackObjectsCount = (double)moneyToAdd
                                                        }
                                                    };
                                                    newMoney.Add(newMoneyStack);
                                                }
                                            mailSend.SendDirectNpcMessageToPlayer(sessionID, "1337bb0dd843363fcd1be869", SPTarkov.Server.Core.Models.Enums.MessageType.FleamarketMessage, random_message, newMoney);
                                            } else {
                                                 var messages = CroupierData.BlackmarketFailMessages;
                                                 var random_message = messages != null && messages.Count > 0 ? messages[new Random().Next(messages.Count)] : "Croupier: You need to be at least level 15 to use the blackmarket!";
                                                 mailSend.SendDirectNpcMessageToPlayer(sessionID, "1337bb0dd843363fcd1be869", SPTarkov.Server.Core.Models.Enums.MessageType.NpcTraderMessage, random_message, null);
                                            }
                                        }
                                    } 
                                    
                                    if (tradingData.Type == "buy_from_trader") {
                                        var newItemsCount = 0;
                                        if (CroupierData.DbItems == null) {
                                            var dbItems = databaseService.GetTables().Templates.Items;
                                            List<string> ids = dbItems.Keys.Select(key => key.ToString()).ToList();
                                            CroupierData.DbItems = ids;
                                        }
                                        var itemId = Convert.ToString(tradingData.GetType().GetProperty("ItemId")?.GetValue(tradingData));
                                        var count = Convert.ToInt32(tradingData.GetType().GetProperty("Count")?.GetValue(tradingData));
                                        (Tier tier, bool needsGun) = itemId switch
                                        {
                                            "676e7094c10d4e01865d8112" => (Tier.Low, false),
                                            "676e6e7c2e39b0c7ab1e7109" => (Tier.Low, true),
                                            "676e6ebec1ad34f0d56e6fce" => (Tier.Mid, false),
                                            "676e6e8180f50661c07bb6ec" => (Tier.Mid, true),
                                            "676e6eaac5568a5d867b4b5d" => (Tier.Top, false),
                                            "676e70994154237666a572d7" => (Tier.Top, true),
                                            _ => throw new ArgumentException($"Unknown itemId: {itemId}")
                                        };

                                        var targetTpls = new HashSet<string> {
                                            "676e674807fe27c9fbcf410d", "676e66ad873d974331326aed",
                                            "676e673ff463b63eb4ad353b", "676e66c9a0660ca45b61e4a3",
                                            "676e6735ea62d4e5b820aad1", "676e66e7e36748f0c288dafe" };
                                        var matchedItemIds = new List<string>();

                                        try {
                                            var outputDoc = JsonDocument.Parse(output ?? "{}");
                                            var root = outputDoc.RootElement;

                                            if (root.TryGetProperty("data", out var data)) {
                                                if (data.TryGetProperty("profileChanges", out var profileChanges)) {
                                                    if (profileChanges.TryGetProperty(sessionID.ToString(), out var changes)) {
                                                        if (changes.TryGetProperty("items", out var items)) {
                                                            if (items.TryGetProperty("new", out var newItems)) {
                                                                var filteredNewItems = new List<object>();
                                                                foreach (var item in newItems.EnumerateArray()) {
                                                                    newItemsCount++;
                                                                    if (item.TryGetProperty("_tpl", out var tpl) && item.TryGetProperty("_id", out var id)) {
                                                                        var tplValue = tpl.GetString();
                                                                        var idValue = id.GetString();
                                                                        if (tplValue != null && targetTpls.Contains(tplValue)) {
                                                                            matchedItemIds.Add(idValue ?? "");
                                                                        } else {
                                                                            filteredNewItems.Add(JsonSerializer.Deserialize<object>(item.GetRawText()));
                                                                        }
                                                                    } else {
                                                                        filteredNewItems.Add(JsonSerializer.Deserialize<object>(item.GetRawText()));
                                                                    }
                                                                }
                                                                var modifiedOutput = new Dictionary<string, object>();
                                                                if (root.TryGetProperty("err", out var err)) {
                                                                    modifiedOutput["err"] = JsonSerializer.Deserialize<object>(err.GetRawText());
                                                                }
                                                                if (root.TryGetProperty("errmsg", out var errmsg)) {
                                                                    modifiedOutput["errmsg"] = errmsg.ValueKind == JsonValueKind.Null ? null : JsonSerializer.Deserialize<object>(errmsg.GetRawText());
                                                                }
                                                                var modifiedData = new Dictionary<string, object>();
                                                                if (data.TryGetProperty("warnings", out var warnings)) {
                                                                    modifiedData["warnings"] = JsonSerializer.Deserialize<object>(warnings.GetRawText());
                                                                }
                                                                var modifiedProfileChanges = new Dictionary<string, object>();
                                                                foreach (var profile in profileChanges.EnumerateObject()) {
                                                                    var profileId = profile.Name;
                                                                    var profileChangesData = profile.Value;
                                                                    if (profileId == sessionID.ToString()) {
                                                                        var modifiedProfile = new Dictionary<string, object>();
                                                                        foreach (var prop in profileChangesData.EnumerateObject()) {
                                                                            if (prop.Name == "items") {
                                                                                var modifiedItems = new Dictionary<string, object>();
                                                                                foreach (var itemProp in prop.Value.EnumerateObject()) {
                                                                                    if (itemProp.Name == "new") {
                                                                                        modifiedItems["new"] = filteredNewItems;
                                                                                    } else {
                                                                                        modifiedItems[itemProp.Name] = JsonSerializer.Deserialize<object>(itemProp.Value.GetRawText());
                                                                                    }
                                                                                }
                                                                                modifiedProfile["items"] = modifiedItems;
                                                                            } else {
                                                                                modifiedProfile[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                                                                            }
                                                                        }
                                                                        modifiedProfileChanges[profileId] = modifiedProfile;
                                                                    } else {
                                                                        modifiedProfileChanges[profileId] = JsonSerializer.Deserialize<object>(profileChangesData.GetRawText());
                                                                    }
                                                                }
                                                                modifiedData["profileChanges"] = modifiedProfileChanges;
                                                                modifiedOutput["data"] = modifiedData;
                                                                output = JsonSerializer.Serialize(modifiedOutput);
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        catch (Exception ex) {
                                            logger.Error($"Failed to parse/modify output: {ex.Message}");
                                        }

                                        foreach(var itemIdFound in matchedItemIds)
                                        {
                                            inventoryHelper.RemoveItem(pmcData, (MongoId)itemIdFound, sessionID);
                                        }

                                        var currentSeason = GetCurrentSeason();
                                        bool isWinter;
                                        if (currentSeason == 2 || currentSeason == 3) {
                                            isWinter = true;
                                        } else {
                                            isWinter = false;
                                        }

                                        if (newItemsCount > 0) {
                                            for (var j = 0; j < count; j++) {
                                                var messages = CroupierData.CroupierSuccessMessages;
                                                var random_message = messages != null && messages.Count > 0 ? messages[new Random().Next(messages.Count)] : "Here's your stuff!";
                                                var roulette = new Roulette(tier, needsGun, isWinter);
                                                mailSend.SendDirectNpcMessageToPlayer(sessionID, "1337bb0dd843363fcd1be869", SPTarkov.Server.Core.Models.Enums.MessageType.FleamarketMessage, random_message, roulette.GeneratedItems);
                                            }
                                        }
                                        
                                    }
                                }
                            }
                        }
                    }
                    CroupierBeforeRouter.ClearAllCache();
                    return output ?? "";
                }
            )
        ])
    { }

    public static int compareBlackmarketPrices(object eventItems, List<Item>? cachedInventory)
    {
        var itemIds = new List<string>();
        var itemCounts = new Dictionary<string, int>();
        int additionalMoney = 0;
        if (eventItems is IEnumerable<object> items) {
            foreach (var item in items) {
                var inventoryItemId = item.GetType().GetProperty("Id")?.GetValue(item);
                var itemCount = item.GetType().GetProperty("Count")?.GetValue(item);
                if (inventoryItemId != null && itemCount != null) {
                    itemIds.Add(inventoryItemId.ToString());
                    itemCounts[inventoryItemId.ToString()] = Convert.ToInt32(itemCount);
                }
            }
        }
        var itemsToCount = new List<Item>();
        if (cachedInventory != null) {
            foreach (var cachedItem in cachedInventory) {
                if ((itemIds.Contains(cachedItem.Id.ToString())) && (!itemIds.Contains(cachedItem.ParentId.ToString()))) {
                    if (!CroupierData.Config.FleaSellOnlyFoundInRaid || (cachedItem.Upd != null && cachedItem.Upd.SpawnedInSession == true)) {
                        var count = itemCounts[cachedItem.Id.ToString()];
                        var flea_price = Convert.ToInt32(_itemHelper.GetDynamicItemPrice((MongoId)cachedItem.Template));
                        var handbook_price = _itemHelper.GetStaticItemPrice((MongoId)cachedItem.Template);
                        var trader_price = Convert.ToInt32(handbook_price * 0.55);
                        var tax = CroupierData.Config.FleaSellTaxPercent;
                        if (tax > 100) tax = 100;
                        if (tax < 0) tax = 0;
                        additionalMoney += ((int)(flea_price * ((100 - tax) / 100.0)) - trader_price) * count;
                    }
                }
            }
        }

        return additionalMoney;
    }

    private static int GetCurrentSeason()
    {
        if (CroupierData.weatherConfig.OverrideSeason.HasValue)
        {
            return (int)CroupierData.weatherConfig.OverrideSeason.Value;
        }

        var seasonDates = CroupierData.weatherConfig.SeasonDates;
        var md = DateTime.Now.Month * 100 + DateTime.Now.Day;
        foreach (var s in seasonDates)
        {
            var start = s.StartMonth.GetValueOrDefault() * 100 + s.StartDay.GetValueOrDefault();
            var end = s.EndMonth.GetValueOrDefault() * 100 + s.EndDay.GetValueOrDefault();
            var wraps = end < start;
            var inRange = wraps ? md >= start || md <= end : md >= start && md <= end;
            if (inRange)
            {
                return (int)s.SeasonType.GetValueOrDefault();
            }
        }

        return 0;
    }

}

public static class CroupierData
{
    public static ModConfig? Config { get; set; }
    public static List<string>? BlackmarketFailMessages { get; set; }
    public static List<string>? BlackmarketSuccessMessages { get; set; }
    public static List<string>? CroupierSuccessMessages { get; set; }
    public static List<string>? DbItems { get; set; }
    public static string pathToMod = "";
    public static WeatherConfig? weatherConfig;
}

public class ModConfig
{
    public bool FleaSellEnabled { get; set; }
    public bool FleaSellEnabledOnlyAfter15Lvl { get; set; }
    public bool FleaSellOnlyFoundInRaid { get; set; }
    public uint FleaSellTaxPercent { get; set; }
}

public class DbItem
{
    public MongoId id { get; set; }
    public string objName { get; set; }
    public string name { get; set; }
    public string shortName { get; set; }
    public string description { get; set; }
    public string backgroundColor { get; set; }
    public string prefab { get; set; }
}