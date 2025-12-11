using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TeamCherry.Localization;
using UnityEngine;
using UnityEngine.SceneManagement;
using static PlayerDataTest;

namespace MoreConsumablesInShop;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[Harmony]
public class Plugin : BaseUnityPlugin {
	enum ConsumableItemID {
		GeoFrayed = 0,
		GeoSmall = 1,
		GeoMedium = 2,
		GeoLarge = 3,
		GeoHuge = 4,
		ShardSmall = 5,
		ShardBig = 6,
		ShardMech = 7,
		ShardHornet = 8,
		ShardHornetAct3 = 9,
		Count = 10
	}

	enum VendorID {
		ForgeDaughter = 0,
		Fey = 1,
		Grindle = 2,
		Jubilina = 3,
		Mort = 4,
		MottledSkarr = 5,
		Pebb = 6,
		TwelfthArchitect = 7,
		Count = 8
	}

	enum ConsumableGroups {
		Rosaries = 1,
		Shards = 2,
		Both = 3
	}

	internal static new ManualLogSource Logger;

	const string GEO_FRAYED = "INV_NAME_COIN_SET_F";
	const string GEO_SMALL = "INV_NAME_COIN_SET_S";
	const string GEO_MEDIUM = "INV_NAME_COIN_SET_M";
	const string GEO_LARGE = "INV_NAME_COIN_SET_M2";
	const string GEO_HUGE = "INV_NAME_COIN_SET_L";
	const string SHARD_SMALL = "INV_NAME_SHARD_POUCH";
	const string SHARD_BIG = "INV_NAME_GREAT_SHARD";
	const string SHARD_MECH = "INV_NAME_PRISTINE_CORE";
	const string SHARD_HORNET = "INV_NAME_FIXER_IDOL";

	static ShopItem[,] ShopItems;
	static bool ShopItemsAreSetUp = false;

	static ConfigEntry<bool>[] canSellConfig;
	static ConfigEntry<int>[] sellPriceConfig;
	static List<ShopItemList> ModifiedShopList;

	private void Awake() {
		Logger = base.Logger;

		Harmony harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
		harmony.PatchAll();

		SetupConfig(Config);

		ModifiedShopList = new List<ShopItemList>();
		SceneManager.activeSceneChanged += ClearModifiedShopList;

		Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
	}

	private static void SetupConfig(ConfigFile config) {
		canSellConfig = new ConfigEntry<bool>[(int)ConsumableItemID.Count];
		sellPriceConfig = new ConfigEntry<int>[(int)ConsumableItemID.Count];
		foreach(ConsumableItemID item in (ConsumableItemID[])Enum.GetValues(typeof(ConsumableItemID))) {
			if(item == ConsumableItemID.Count)
				continue;

			int idx = (int)item;

			if(item == ConsumableItemID.ShardHornetAct3) {
				canSellConfig[idx] = canSellConfig[(int)ConsumableItemID.ShardHornet];
				sellPriceConfig[idx] = sellPriceConfig[(int)ConsumableItemID.ShardHornet];
			}

			string name = string.Empty;
			int price = 0;
			ConsumableGroups group = ConsumableGroups.Rosaries;
			switch(item) {
				case ConsumableItemID.GeoFrayed:
					name = "Frayed Rosary String";
					price = 50;
					break;
				case ConsumableItemID.GeoSmall:
					name = "Rosary String";
					price = 80;
					break;
				case ConsumableItemID.GeoMedium:
					name = "Rosary Necklace";
					price = 140;
					break;
				case ConsumableItemID.GeoLarge:
					name = "Heavy Rosary Necklace";
					price = 240;
					break;
				case ConsumableItemID.GeoHuge:
					name = "Pale Rosary Necklace";
					price = 360;
					break;
				case ConsumableItemID.ShardSmall:
					name = "Shard Bundle";
					price = 50;
					group = ConsumableGroups.Shards;
					break;
				case ConsumableItemID.ShardBig:
					name = "Beast Shard";
					price = 90;
					group = ConsumableGroups.Shards;
					break;
				case ConsumableItemID.ShardMech:
					name = "Pristine Core";
					price = 140;
					group = ConsumableGroups.Shards;
					break;
				case ConsumableItemID.ShardHornet:
					name = "Hornet Statuette";
					price = 200;
					group = ConsumableGroups.Shards;
					break;
				default:
					Logger.LogWarning($"ConsumableItemID.{item.ToString()} does not have a config value");
					continue;
			};
			string nameNoSpace = string.Concat(name.Where(c => c != ' '));
			string section = $"{group.ToString()}.{nameNoSpace}";

			canSellConfig[idx] = config.Bind<bool>(section, "canSell", true, $"Whether shops can sell {name}s");
			sellPriceConfig[idx] = config.Bind<int>(section, "sellPrice", price, new ConfigDescription($"The sell price of {name}s in Rosaries", new AcceptableValueRange<int>(0, 9999999)));
		}
	}

	private static void ClearModifiedShopList(Scene from, Scene to) {
		if(to.name == "Menu_Title") {
			ModifiedShopList.Clear();
		}
	}

	[HarmonyPatch(typeof(ShopOwnerBase), nameof(ShopOwnerBase.SpawnUpdateShop))]
	[HarmonyPrefix]
	private static void InitializeSkarrMerchantShopItemList(ShopOwnerBase __instance) {
		if(__instance.TryGetComponent(out ShopOwner shopOwner)) {
			if(__instance.name == "Ant Merchant" && shopOwner.stockList == null) {
				Logger.LogInfo($"Creating ShopItemList for \"{shopOwner.name}\"");
				shopOwner.stockList = ScriptableObject.CreateInstance<ShopItemList>();
				shopOwner.stockList.name = "Ant Merchant Stock";
				shopOwner.stockList.shopItems = shopOwner.stock;
			}
		}
	}



	[HarmonyPatch(typeof(ShopItemList), nameof(ShopItemList.ShopItems), MethodType.Getter)]
	[HarmonyPrefix]
	private static void AddItemsToShop(ShopItemList __instance) {
		if(__instance.shopItems == null)
			return;

		Logger.LogInfo("Trying to set up the shop");

		if(!ShopItemsAreSetUp) {
			SetupShopItems();
		}

		int vendor;
		ConsumableGroups group;
		int popCount = 1;
		bool rosariesFirst = false;
		switch(__instance.name) {
			case "Forgedaughter Stock":
				vendor = (int)VendorID.ForgeDaughter;
				group = ConsumableGroups.Shards;
				break;
			case "Bellhart Stock":
				vendor = (int)VendorID.Fey;
				group = ConsumableGroups.Both;
				popCount++;
				break;
			case "Grindle Stock":
				vendor = (int)VendorID.Grindle;
				group = ConsumableGroups.Rosaries;
				break;
			case "City Merchant Stock":
				vendor = (int)VendorID.Jubilina;
				group = ConsumableGroups.Rosaries;
				break;
			case "Pilgrims Rest Stock":
				vendor = (int)VendorID.Mort;
				group = ConsumableGroups.Both;
				popCount++;
				rosariesFirst = true;
				break;
			case "Ant Merchant Stock":
				vendor = (int)VendorID.MottledSkarr;
				group = ConsumableGroups.Shards;
				break;
			case "Bonebottom Peddler Stock":
				vendor = (int)VendorID.Pebb;
				group = ConsumableGroups.Rosaries;
				break;
			case "Architect Stock":
				vendor = (int)VendorID.TwelfthArchitect;
				group = ConsumableGroups.Shards;
				break;
			default:
				Logger.LogInfo($"\"{__instance.name}\" case not handled. Shop will not be set up");
				return;
		}

		if(ModifiedShopList.Contains(__instance)) {
			Logger.LogInfo($"Shop \"{__instance.name}\" is already set up");
			return;
		}

		ModifiedShopList.Add(__instance);

		Logger.LogInfo($"Trying to set up shop: \"{__instance.name}\", " +
			$"vendor: \"{(VendorID)vendor}\" ({vendor}), group: \"{group}\" ({(int)group}), popCount: {popCount}");

		List<ShopItem> stock = __instance.shopItems.ToList();

		Logger.LogInfo($"Popping {popCount} items from stock");
		for(int i = 0; i < popCount; i++) {
			stock.RemoveAt(stock.Count - 1);
		}

		if(rosariesFirst && (group == ConsumableGroups.Rosaries || group == ConsumableGroups.Both)) {
			Logger.LogInfo("Adding rosaries");
			stock.AddIf(ShopItems[(int)ConsumableItemID.GeoFrayed, vendor], canSellConfig[(int)ConsumableItemID.GeoFrayed].Value);
			stock.AddIf(ShopItems[(int)ConsumableItemID.GeoSmall, vendor], canSellConfig[(int)ConsumableItemID.GeoSmall].Value);
			stock.AddIf(ShopItems[(int)ConsumableItemID.GeoMedium, vendor], canSellConfig[(int)ConsumableItemID.GeoMedium].Value);
			stock.AddIf(ShopItems[(int)ConsumableItemID.GeoLarge, vendor], canSellConfig[(int)ConsumableItemID.GeoLarge].Value);
			stock.AddIf(ShopItems[(int)ConsumableItemID.GeoHuge, vendor], canSellConfig[(int)ConsumableItemID.GeoHuge].Value);
		}

		if(group == ConsumableGroups.Shards || group == ConsumableGroups.Both) {
			Logger.LogInfo("Adding shards");
			stock.AddIf(ShopItems[(int)ConsumableItemID.ShardSmall, vendor], canSellConfig[(int)ConsumableItemID.ShardSmall].Value);
			stock.AddIf(ShopItems[(int)ConsumableItemID.ShardBig, vendor], canSellConfig[(int)ConsumableItemID.ShardBig].Value);
			stock.AddIf(ShopItems[(int)ConsumableItemID.ShardMech, vendor], canSellConfig[(int)ConsumableItemID.ShardMech].Value);
			stock.AddIf((PlayerData.instance.act3_wokeUp && !QuestManager.GetAllFullQuests().First(q => q.name == "Building Materials (Statue)").IsCompleted) ? ShopItems[(int)ConsumableItemID.ShardHornetAct3, vendor] : ShopItems[(int)ConsumableItemID.ShardHornet, vendor], canSellConfig[(int)ConsumableItemID.ShardHornet].Value);
		}

		if(!rosariesFirst && (group == ConsumableGroups.Rosaries || group == ConsumableGroups.Both)) {
			Logger.LogInfo("Adding rosaries");
			stock.AddIf(ShopItems[(int)ConsumableItemID.GeoFrayed, vendor], canSellConfig[(int)ConsumableItemID.GeoFrayed].Value);
			stock.AddIf(ShopItems[(int)ConsumableItemID.GeoSmall, vendor], canSellConfig[(int)ConsumableItemID.GeoSmall].Value);
			stock.AddIf(ShopItems[(int)ConsumableItemID.GeoMedium, vendor], canSellConfig[(int)ConsumableItemID.GeoMedium].Value);
			stock.AddIf(ShopItems[(int)ConsumableItemID.GeoLarge, vendor], canSellConfig[(int)ConsumableItemID.GeoLarge].Value);
			stock.AddIf(ShopItems[(int)ConsumableItemID.GeoHuge, vendor], canSellConfig[(int)ConsumableItemID.GeoHuge].Value);
		}

		__instance.shopItems = stock.ToArray();

		Logger.LogInfo("Done");
	}



	// Set up all items to be added to shops 
	private static void SetupShopItems() {
		ShopItemsAreSetUp = true;
		Logger.LogInfo("Creating all shop items");
		ShopItems = new ShopItem[(int)ConsumableItemID.Count, (int)VendorID.Count];
		for(int vendor = 0; vendor < (int)VendorID.Count; vendor++) {
			ConsumableGroups group;
			string uiSheet;
			string descSheet;
			string geoDesc = "";
			string shardDesc = "";
			QuestTest rosaryLargeQuest = new QuestTest() {
				Quest = QuestManager.GetAllFullQuests().First(q => q.name == "Belltown House Start"),
				CheckCompleted = true,
				IsCompleted = true
			};
			QuestTest rosaryHugeQuest = new QuestTest() {
				Quest = QuestManager.GetAllFullQuests().First(q => q.name == "Belltown House Mid"),
				CheckCompleted = true,
				IsCompleted = true
			};
			QuestTest? extraQuestTestR = null;
			QuestTest? extraQuestTestS = null;
			switch((VendorID)vendor) {
				case VendorID.ForgeDaughter:
					group = ConsumableGroups.Shards;
					uiSheet = "UI";
					descSheet = "Forge";
					shardDesc = "FORGE_SHARD_POUCH_DESC";
					break;
				case VendorID.Fey:
					group = ConsumableGroups.Both;
					uiSheet = "UI";
					descSheet = "Belltown";
					geoDesc = "SHOP_ITEM_ROSARYSTRING_M_DESC";
					shardDesc = "SHOP_ITEM_SHARD_POUCH";
					extraQuestTestR = new QuestTest() {
						Quest = QuestManager.GetAllFullQuests().First(q => q.name == "Save Courier Short"),
						CheckCompleted = true,
						IsCompleted = true
					};
					extraQuestTestS = extraQuestTestR;
					break;
				case VendorID.Grindle:
					group = ConsumableGroups.Rosaries;
					uiSheet = "UI";
					descSheet = "Wanderers";
					geoDesc = "GRINDLE_ITEM_ROSARY_STRING";
					rosaryLargeQuest = new QuestTest() {
						Quest = QuestManager.GetAllFullQuests().First(q => q.name == "Citadel Ascent Lift"),
						CheckCompleted = true,
						IsCompleted = true
					};
					rosaryHugeQuest = new QuestTest() {
						Quest = QuestManager.GetAllFullQuests().First(q => q.name == "Black Thread Pt0"),
						CheckAccepted = true,
						IsAccepted = true
					};
					break;
				case VendorID.Jubilina:
					group = ConsumableGroups.Rosaries;
					uiSheet = "UI";
					descSheet = "Enclave";
					geoDesc = "CITY_MERCHANT_ITEM_ROSARYSTRING";
					rosaryLargeQuest = new QuestTest() {
						Quest = QuestManager.GetAllFullQuests().First(q => q.name == "Songclave Donation 1"),
						CheckCompleted = true,
						IsCompleted = true
					};
					rosaryHugeQuest = new QuestTest() {
						Quest = QuestManager.GetAllFullQuests().First(q => q.name == "Songclave Donation 2"),
						CheckCompleted = true,
						IsCompleted = true
					};
					break;
				case VendorID.Mort:
					group = ConsumableGroups.Both;
					uiSheet = "UI";
					descSheet = "Wilds";
					geoDesc = "PILGRIM_REST_SHOP_ITEM_DESC_COINSET_S";
					shardDesc = "PILGRIM_REST_SHOP_ITEM_DESC_SHARD_POUCH";
					rosaryLargeQuest = new QuestTest() {
						Quest = QuestManager.GetAllFullQuests().First(q => q.name == "Courier Delivery Pilgrims Rest"),
						CheckWasEverCompleted = true,
						WasEverCompleted = true
					};
					rosaryHugeQuest = new QuestTest() {
						Quest = QuestManager.GetAllFullQuests().First(q => q.name == "Courier Delivery Pilgrims Rest"),
						CheckWasEverCompleted = true,
						WasEverCompleted = true
					};
					extraQuestTestS = new QuestTest() {
						Quest = QuestManager.GetAllFullQuests().First(q => q.name == "Courier Delivery Pilgrims Rest"),
						CheckWasEverCompleted = true,
						WasEverCompleted = true
					};
					break;
				case VendorID.MottledSkarr:
					group = ConsumableGroups.Shards;
					uiSheet = "UI";
					descSheet = "Wilds";
					shardDesc = "ANT_MERCHANT_SHARD_POUCH_DESC";
					break;
				case VendorID.Pebb:
					group = ConsumableGroups.Rosaries;
					uiSheet = "UI";
					descSheet = "Bonebottom";
					geoDesc = "BB_SHOPKEEP_ITEM_ROSARY_SET_DESC";
					rosaryLargeQuest = new QuestTest() {
						Quest = QuestManager.GetAllFullQuests().First(q => q.name == "Courier Delivery Bonebottom"),
						CheckWasEverCompleted = true,
						WasEverCompleted = true
					};
					rosaryHugeQuest = new QuestTest() {
						Quest = QuestManager.GetAllFullQuests().First(q => q.name == "Skull King"),
						CheckCompleted = true,
						IsCompleted = true
					};
					break;
				case VendorID.TwelfthArchitect:
					group = ConsumableGroups.Shards;
					uiSheet = "UI";
					descSheet = "Under";
					shardDesc = "ARCHITECT_SHOP_DESC_SHARDPOUCH";
					break;
				default:
					Logger.LogWarning($"No case set up for VendorID \"{(VendorID)vendor}\" ({vendor}), skipping...");
					continue;
			}

			if(group == ConsumableGroups.Rosaries || group == ConsumableGroups.Both) {
				// Create rosary shopItems 
				ShopItems[(int)ConsumableItemID.GeoFrayed, vendor] = CreateShopItem(
					GEO_FRAYED, uiSheet, geoDesc, descSheet,
					sellPriceConfig[(int)ConsumableItemID.GeoFrayed].Value,
					CollectableItemManager.GetItemByName("Rosary_Set_Frayed"),
					extraQuestTest: extraQuestTestR
				);

				ShopItems[(int)ConsumableItemID.GeoSmall, vendor] = CreateShopItem(
					GEO_SMALL, uiSheet, geoDesc, descSheet,
					sellPriceConfig[(int)ConsumableItemID.GeoSmall].Value,
					CollectableItemManager.GetItemByName("Rosary_Set_Small"),
					extraQuestTest: extraQuestTestR
				);

				ShopItems[(int)ConsumableItemID.GeoMedium, vendor] = CreateShopItem(
					GEO_MEDIUM, uiSheet, geoDesc, descSheet,
					sellPriceConfig[(int)ConsumableItemID.GeoMedium].Value,
					CollectableItemManager.GetItemByName("Rosary_Set_Medium"),
					questsAppearConditions: [
						new QuestTest() {
							Quest = QuestManager.GetAllFullQuests().First(q => q.name == "The Threadspun Town"),
							CheckCompleted = true,
							IsCompleted = true
						}
					],
					extraQuestTest: extraQuestTestR
				);

				ShopItems[(int)ConsumableItemID.GeoLarge, vendor] = CreateShopItem(
					GEO_LARGE, uiSheet, geoDesc, descSheet,
					sellPriceConfig[(int)ConsumableItemID.GeoLarge].Value,
					CollectableItemManager.GetItemByName("Rosary_Set_Large"),
					questsAppearConditions: [
						rosaryLargeQuest
					],
					extraQuestTest: extraQuestTestR
				);

				ShopItems[(int)ConsumableItemID.GeoHuge, vendor] = CreateShopItem(
					GEO_HUGE, uiSheet, geoDesc, descSheet,
					sellPriceConfig[(int)ConsumableItemID.GeoHuge].Value,
					CollectableItemManager.GetItemByName("Rosary_Set_Huge_White"),
					questsAppearConditions: [
						rosaryHugeQuest,
						rosaryLargeQuest
					],
					extraQuestTest: extraQuestTestR
				);
			}

			if(group == ConsumableGroups.Shards || group == ConsumableGroups.Both) {
				// Create shard shopItems 
				ShopItems[(int)ConsumableItemID.ShardSmall, vendor] = CreateShopItem(
					SHARD_SMALL, uiSheet, shardDesc, descSheet,
					sellPriceConfig[(int)ConsumableItemID.ShardSmall].Value,
					CollectableItemManager.GetItemByName("Shard Pouch"),
					extraQuestTest: extraQuestTestS
				);

				ShopItems[(int)ConsumableItemID.ShardBig, vendor] = CreateShopItem(
					SHARD_BIG, uiSheet, shardDesc, descSheet,
					sellPriceConfig[(int)ConsumableItemID.ShardBig].Value,
					CollectableItemManager.GetItemByName("Great Shard"),
					extraAppearConditions: new PlayerDataTest() {
						TestGroups = [
							new TestGroup() {
								Tests = [
									new Test() {
										Type = TestType.Bool,
										FieldName = "act2Started",
										NumType = NumTestType.Equal,
										BoolValue = true
									}
								]
							}
						]
					},
					extraQuestTest: extraQuestTestS
				);
				ShopItems[(int)ConsumableItemID.ShardMech, vendor] = CreateShopItem(
					SHARD_MECH, uiSheet, shardDesc, descSheet,
					sellPriceConfig[(int)ConsumableItemID.ShardMech].Value,
					CollectableItemManager.GetItemByName("Pristine Core"),
					extraAppearConditions: new PlayerDataTest() {
						TestGroups = [
							new TestGroup() {
								Tests = [
									new Test() {
										Type = TestType.Bool,
										FieldName = "HasMelodyArchitect",
										NumType = NumTestType.Equal,
										BoolValue = true
									},
									new Test() {
										Type = TestType.Bool,
										FieldName = "MetArchitect",
										NumType = NumTestType.Equal,
										BoolValue = true
									}
								]
							}
						]
					},
					extraQuestTest: extraQuestTestS
				);
				ShopItems[(int)ConsumableItemID.ShardHornet, vendor] = CreateShopItem(
					SHARD_HORNET, uiSheet, shardDesc, descSheet,
					sellPriceConfig[(int)ConsumableItemID.ShardHornet].Value,
					CollectableItemManager.GetItemByName("Fixer Idol"),
					questsAppearConditions: [
						new QuestTest() {
							Quest = QuestManager.GetAllFullQuests().First(q => q.name == "Building Materials (Statue)"),
							CheckCompleted = true,
							IsCompleted = true
						}
					],
					extraQuestTest: extraQuestTestS
				);
				ShopItems[(int)ConsumableItemID.ShardHornetAct3, vendor] = CreateShopItem(
					SHARD_HORNET, uiSheet, shardDesc, descSheet,
					sellPriceConfig[(int)ConsumableItemID.ShardHornetAct3].Value,
					CollectableItemManager.GetItemByName("Fixer Idol"),
					extraAppearConditions: new PlayerDataTest() {
						TestGroups = [
							new TestGroup() {
								Tests = [
									new Test() {
										Type = TestType.Bool,
										FieldName = "seenBonetownDestroyed",
										NumType = NumTestType.Equal,
										BoolValue = true
									}
								]
							}
						]
					},
					extraQuestTest: extraQuestTestS
				);
			}
		}
		Logger.LogInfo("Shop items created");
	}



	private static ShopItem CreateShopItem(string name, string nameSheet, string desc, string descSheet, int cost, SavedItem item, QuestTest[] questsAppearConditions = null, PlayerDataTest extraAppearConditions = null, QuestTest? extraQuestTest = null) {
		if(item == null) {
			Logger.LogError($"CreateShopItem \"{name}\" - SavedItem parameter is null");
			return null;
		}

		Logger.LogInfo($"Creating ${cost} ShopItem \"{name}\" (\"{nameSheet}\"), \"{desc}\" (\"{descSheet}\")");

		ShopItem shopItem = ShopItem.CreateTemp(name);
		shopItem.displayName = new LocalisedString(nameSheet, name);
		shopItem.description = new LocalisedString(descSheet, desc);
		shopItem.cost = cost;
		shopItem.currencyType = CurrencyType.Money;
		shopItem.savedItem = item;

		if(extraQuestTest != null) {
			if(questsAppearConditions == null) {
				questsAppearConditions = [(QuestTest)extraQuestTest];
			} else {
				List<QuestTest> temp = questsAppearConditions.ToList();
				temp.Add((QuestTest)extraQuestTest);
				questsAppearConditions = temp.ToArray();
			}
		}

		if(questsAppearConditions != null) {
			shopItem.questsAppearConditions = questsAppearConditions;
		}

		if(extraAppearConditions != null) {
			shopItem.extraAppearConditions = extraAppearConditions;
		}

		return shopItem;
	}
}
