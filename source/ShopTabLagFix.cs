using BattleTech.UI;

namespace BattletechPerformanceFix;

public class ShopTabLagFix : Feature
{
    public void Activate()
    {
        Main.harmony.PatchAll(typeof(ShopTabLagFix));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SG_Shop_Screen), nameof(SG_Shop_Screen.AddShopInventory))]
    public static void OnlySortAtEnd(SG_Shop_Screen __instance)
    {
        Log.Main.Debug?.Log("ShopTabLagFix: OnlySortAtEnd");
        var lv = __instance.inventoryWidget.ListView;

        //These don't actually seem to be needed, but keeping them just in case.
        lv.Sort();
        lv.Refresh();
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MechLabInventoryWidget_ListView), nameof(MechLabInventoryWidget_ListView.AddItemToInventory))]
    public static void AddItemToInventory(ref bool __runOriginal, MechLabInventoryWidget_ListView __instance, InventoryDataObject_BASE ItemData)
    {
        if (!__runOriginal)
        {
            return;
        }

        Log.Main.Debug?.Log("ShopTabLagFix: AddItemToInventory");
        var _this = __instance;
        var _items = _this.ListView.Items;
        InventoryDataObject_BASE listElementController_BASE = null;
        foreach (var listElementController_BASE2 in _this.inventoryData)
        {
            if (listElementController_BASE2.GetItemType() == ItemData.GetItemType() && listElementController_BASE2.IsDuplicateContent(ItemData) && _this.ParentDropTarget != null && _this.StackQuantities)
            {
                listElementController_BASE = listElementController_BASE2;
                break;
            }
        }

        if (listElementController_BASE != null)
        {
            listElementController_BASE.ModifyQuantity(1);
        }
        else
        {
            // OnItemAdded logic does not seem to be needed?
            // HBSLoopScroll 219-228
            _items.Add(ItemData);
        }
        __runOriginal = false;
    }
}