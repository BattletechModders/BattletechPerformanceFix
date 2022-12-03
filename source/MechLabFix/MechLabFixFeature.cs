using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BattleTech;
using BattleTech.UI;
using Harmony;
using UnityEngine;

namespace BattletechPerformanceFix.MechLabFix;

internal class MechLabFixFeature : Feature {
    public void Activate() {
        "InitWidgets".Transpile<MechLabPanel>();
        "InitWidgets".Pre<MechLabPanel>();
        "PopulateInventory".Pre<MechLabPanel>();
        "MechCanEquipItem".Pre<MechLabPanel>();

        "LateUpdate".Pre<UnityEngine.UI.ScrollRect>();

        "ClearInventory".Pre<MechLabInventoryWidget>();
        "OnAddItem".Pre<MechLabInventoryWidget>();
        "OnRemoveItem".Pre<MechLabInventoryWidget>();
        "OnItemGrab".Pre<MechLabInventoryWidget>();
        "ApplyFiltering".Pre<MechLabInventoryWidget>("ApplyFiltering_Pre", Priority.First);
        "ApplySorting".Pre<MechLabInventoryWidget>("ApplySorting_Pre", Priority.First);
        // "RemoveDataItem".Pre<MechLabInventoryWidget>();

        // Fix some annoying seemingly vanilla log spam
        "OnDestroy".Pre<InventoryItemElement_NotListView>(iel => { if(iel.iconMech != null) iel.iconMech.sprite = null;
            return false; });
    }

#region fix for unused shop clones
    public static IEnumerable<CodeInstruction> InitWidgets_Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var to = AccessTools.Method(typeof(MechLabFixFeature), nameof(CreateUIModule));

        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodBase method && method.Name == nameof(CreateUIModule))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = to;
            }
            yield return instruction;
        }
    }

    public static SG_Shop_Screen CreateUIModule(UIManager uiManager, string prefabOverride = "", bool resort = true)
    {
        Log.Main.Debug?.Log("[LimitItems] CreateUIModule");
        try {
            return uiManager.GetOrCreateUIModule<SG_Shop_Screen>(prefabOverride, resort);
        } catch(Exception e) {
            Log.Main.Error?.Log("Encountered exception", e);
            throw;
        }
    }

    public static void InitWidgets_Pre(MechLabPanel __instance)
    {
        Log.Main.Debug?.Log("[LimitItems] InitWidgets_Pre");
        try {
            if (__instance.Shop != null)
            {
                __instance.Shop.Pool();
            }
        } catch(Exception e) {
            Log.Main.Error?.Log("Encountered exception", e);
        }
    }

#endregion

    internal static MechLabFixState state;

    public static bool PopulateInventory_Pre(MechLabPanel __instance)
    {
        Log.Main.Debug?.Log("[LimitItems] PopulateInventory_Pre");
        // return;
        try
        {
            MechLabFixState.GameObjects.Setup(__instance.inventoryWidget);
            state = new(__instance);
        } catch(Exception e) {
            Log.Main.Error?.Log("Encountered exception", e);
        }
        return false;
    }

    public static void ClearInventory_Pre(MechLabInventoryWidget __instance)
    {
        Log.Main.Debug?.Log("[LimitItems] ClearInventory_Pre");
        try
        {
            Log.Main.Debug?.Log($"inventoryCount={__instance.localInventory?.Count}");
            foreach (var iie in __instance.localInventory)
            {
                // fix NRE within Pool()
                iie.controller = new ListElementController_InventoryGear_NotListView();
                iie.controller.dataManager = iie.dataManager = UnityGameInstance.BattleTechGame.DataManager;
                iie.controller.ItemWidget = iie;
            }
        } catch(Exception e) {
            Log.Main.Error?.Log("Encountered exception", e);
        }
    }

    public static void LateUpdate_Pre(UnityEngine.UI.ScrollRect __instance)
    {
        try
        {
            if (state != null && state.inventoryWidget.scrollbarArea == __instance) {
                var newIndexCandidate = (int)((state.rowCountBelowScreen) * (1.0f - __instance.verticalNormalizedPosition));
                newIndexCandidate = Mathf.Clamp(newIndexCandidate, 0, state.rowMaxToStartLoading);
                if (state.rowToStartLoading != newIndexCandidate) {
                    state.rowToStartLoading = newIndexCandidate;
                    Log.Main.Debug?.Log($"[LimitItems] Refresh with: {newIndexCandidate} {__instance.verticalNormalizedPosition}");
                    state.Refresh();
                }
            }
        }
        catch (Exception e)
        {
            Log.Main.Error?.Log("Encountered exception", e);
        }
    }

    public static bool OnAddItem_Pre(MechLabInventoryWidget __instance, IMechLabDraggableItem item)
    {
        Log.Main.Debug?.Log("[LimitItems] OnAddItem_Pre");
        if (state != null && state.inventoryWidget == __instance) {
            try {
                var nlv = item as InventoryItemElement_NotListView;
                var quantity = nlv == null ? 1 : nlv.controller.quantity;
                var existing = state.FetchItem(item.ComponentRef);
                if (existing == null) {
                    Log.Main.Debug?.Log($"OnAddItem new {quantity}");
                    var controller = nlv == null ? null : nlv.controller;
                    if (controller == null) {
                        if (item.ComponentRef.ComponentDefType == ComponentType.Weapon) {
                            var ncontroller = new ListElementController_InventoryWeapon_NotListView();
                            ncontroller.InitAndCreate(item.ComponentRef, state.instance.dataManager, state.inventoryWidget, quantity, false);
                            controller = ncontroller;
                        } else {
                            var ncontroller = new ListElementController_InventoryGear_NotListView();
                            ncontroller.InitAndCreate(item.ComponentRef, state.instance.dataManager, state.inventoryWidget, quantity, false);
                            controller = ncontroller;
                        }
                    }
                    state.rawInventory.Add(controller);
                    state.rawInventory = state.Sort(state.rawInventory);
                    state.FilterChanged(false);
                } else {
                    Log.Main.Debug?.Log($"OnAddItem existing {quantity}");
                    if (existing.quantity != Int32.MinValue) {
                        existing.ModifyQuantity(quantity);
                    }
                    state.Refresh();
                }
            } catch(Exception e) {
                Log.Main.Error?.Log("Encountered exception", e);
            }
            return false;
        }
        return true;
    }

    public static bool RemoveDataItem_Prefix(MechLabInventoryWidget __instance, InventoryDataObject_BASE ItemData, ref bool __result)
    {
        Log.Main.Debug?.Log("[LimitItems] RemoveDataItem_Prefix");
        try
        {
            if (state != null && state.inventoryWidget == __instance && __instance.IsSimGame)
            {
                __result = RemoveItemQuantity( state.FetchItem(ItemData.componentDef), ItemData.quantity);
            }
            return false;
        }
        catch (Exception e)
        {
            Log.Main.Error?.Log("Encountered exception", e);
        }
        return true;
    }

    public static bool OnRemoveItem_Pre(MechLabInventoryWidget __instance, IMechLabDraggableItem item)
    {
        Log.Main.Debug?.Log("[LimitItems] OnRemoveItem_Pre");
        if (state != null && state.inventoryWidget == __instance) {
            try {
                var nlv = item as InventoryItemElement_NotListView;
                RemoveItemQuantity(state.FetchItem(item.ComponentRef), nlv.controller.quantity);
            } catch(Exception e) {
                Log.Main.Error?.Log("Encountered exception", e);
            }
            return false;
        }
        return true;
    }

    private static bool RemoveItemQuantity(ListElementController_BASE_NotListView lec, int quantity)
    {
        if (lec == null) {
            Log.Main.Error?.Log("Existing not found");
            return false;
        }

        if (quantity == 0 || lec.quantity == int.MinValue) {
            Log.Main.Error?.Log("Existing has invalid quantity");
            return false;
        }

        const int change = -1;
        Log.Main.Debug?.Log($"OnRemoveItem quantity={lec.quantity} change={change}");
        lec.ModifyQuantity(change);
        if (lec.quantity < 1)
        {
            state.rawInventory.Remove(lec);
        }
        state.FilterChanged(false);
        state.Refresh();
        return true;
    }

    public static bool ApplyFiltering_Pre(MechLabInventoryWidget __instance, bool refreshPositioning)
    {
        Log.Main.Debug?.Log("[LimitItems] ApplyFiltering_Pre");
        try
        {
            if (state != null && state.inventoryWidget == __instance && !MechLabFixState.filterGuard) {
                Log.Main.Debug?.Log($"OnApplyFiltering (refresh-pos? {refreshPositioning})");
                state.FilterChanged(refreshPositioning);
                return false;
            } else {
                return true;
            }
        }
        catch (Exception e)
        {
            Log.Main.Error?.Log("Encountered exception", e);
            throw;
        }
    }

    public static bool ApplySorting_Pre(MechLabInventoryWidget __instance)
    {
        Log.Main.Debug?.Log("[LimitItems] ApplySorting_Pre");
        try
        {
            if (state != null && state.inventoryWidget == __instance) {
                // it's a mechlab screen, we do our own sort.
                var _cs = __instance.currentSort;
                var cst = _cs.Method;
                Log.Main.Debug?.Log($"OnApplySorting using {cst.DeclaringType.FullName}::{cst}");
                state.FilterChanged(false);
                return false;
            } else {
                return true;
            }
        }
        catch (Exception e)
        {
            Log.Main.Error?.Log("Encountered exception", e);
            throw;
        }
    }

    public static bool MechCanEquipItem_Pre(InventoryItemElement_NotListView item)
    {
        Log.Main.Trace?.Log("[LimitItems] MechCanEquipItem_Pre");

        // undo "fix NRE within Pool()" from earlier
        if (item.controller != null && item.controller.componentDef == null)
        {
            item.controller = null;
        }

        // no idea why this is here, just a NRE fix for vanilla?
        return item.ComponentRef != null;
    }

    public static void OnItemGrab_Pre(MechLabInventoryWidget __instance, ref IMechLabDraggableItem item)
    {
        Log.Main.Debug?.Log("[LimitItems] OnItemGrab_Pre");
        try
        {
            if (state != null && state.inventoryWidget == __instance)
            {
                var nlv = item as InventoryItemElement_NotListView;
                var iw = MechLabFixState.GameObjects.iieTmpG;
                var lec = nlv.controller;
                var cref = state.GetRef(lec);
                iw.ClearEverything();
                iw.ComponentRef = cref;
                lec.ItemWidget = iw;
                iw.SetData(lec, state.inventoryWidget, lec.quantity);
                lec.SetupLook(iw);
                iw.gameObject.SetActive(true);
                item = iw;
            }
        }
        catch (Exception e)
        {
            Log.Main.Error?.Log("Encountered exception", e);
            throw;
        }
    }
}