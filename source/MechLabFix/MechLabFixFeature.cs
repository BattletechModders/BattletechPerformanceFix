using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BattleTech;
using BattleTech.UI;
using UnityEngine;

namespace BattletechPerformanceFix.MechLabFix;

internal class MechLabFixFeature : Feature {
    public void Activate() {
        Main.harmony.PatchAll(typeof(MechLabFixFeature));
    }

#region fix for unused shop clones
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(MechLabPanel), nameof(MechLabPanel.InitWidgets))]
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

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MechLabPanel), nameof(MechLabPanel.InitWidgets))]
    public static void InitWidgets_Pre(ref bool __runOriginal, MechLabPanel __instance)
    {
        if (!__runOriginal)
        {
            return;
        }

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

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MechLabPanel), nameof(MechLabPanel.PopulateInventory))]
    public static void PopulateInventory_Pre(ref bool __runOriginal, MechLabPanel __instance)
    {
        if (!__runOriginal)
        {
            return;
        }

        Log.Main.Debug?.Log("[LimitItems] PopulateInventory_Pre");
        // return;
        try
        {
            MechLabFixState.GameObjects.Setup(__instance.inventoryWidget);
            state = new(__instance);
        } catch(Exception e) {
            Log.Main.Error?.Log("Encountered exception", e);
        }
        __runOriginal = false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MechLabInventoryWidget), nameof(MechLabInventoryWidget.ClearInventory))]
    public static void ClearInventory_Pre(ref bool __runOriginal, MechLabInventoryWidget __instance)
    {
        if (!__runOriginal)
        {
            return;
        }

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

    [HarmonyPrefix]
    [HarmonyPatch(typeof(UnityEngine.UI.ScrollRect), "LateUpdate")]
    public static void LateUpdate_Pre(ref bool __runOriginal, UnityEngine.UI.ScrollRect __instance)
    {
        if (!__runOriginal)
        {
            return;
        }

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

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MechLabInventoryWidget), nameof(MechLabInventoryWidget.OnAddItem))]
    public static void OnAddItem_Pre(ref bool __runOriginal, MechLabInventoryWidget __instance, IMechLabDraggableItem item)
    {
        if (!__runOriginal)
        {
            return;
        }

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
            __runOriginal = false;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MechLabInventoryWidget), nameof(MechLabInventoryWidget.OnRemoveItem))]
    public static void OnRemoveItem_Pre(ref bool __runOriginal, MechLabInventoryWidget __instance, IMechLabDraggableItem item)
    {
        if (!__runOriginal)
        {
            return;
        }

        Log.Main.Debug?.Log("[LimitItems] OnRemoveItem_Pre");
        if (state != null && state.inventoryWidget == __instance) {
            try {
                var nlv = item as InventoryItemElement_NotListView;
                RemoveItemQuantity(state.FetchItem(item.ComponentRef), nlv.controller.quantity);
            } catch(Exception e) {
                Log.Main.Error?.Log("Encountered exception", e);
            }
            __runOriginal = false;
        }
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

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(MechLabInventoryWidget), nameof(MechLabInventoryWidget.ApplyFiltering))]
    public static void ApplyFiltering_Pre(ref bool __runOriginal, MechLabInventoryWidget __instance, bool refreshPositioning)
    {
        if (!__runOriginal)
        {
            return;
        }

        Log.Main.Debug?.Log("[LimitItems] ApplyFiltering_Pre");
        try
        {
            if (state != null && state.inventoryWidget == __instance && !MechLabFixState.filterGuard) {
                Log.Main.Debug?.Log($"OnApplyFiltering (refresh-pos? {refreshPositioning})");
                state.FilterChanged(refreshPositioning);
                __runOriginal = false;
            }
        }
        catch (Exception e)
        {
            Log.Main.Error?.Log("Encountered exception", e);
            throw;
        }
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(MechLabInventoryWidget), nameof(MechLabInventoryWidget.ApplySorting))]
    public static void ApplySorting_Pre(ref bool __runOriginal, MechLabInventoryWidget __instance)
    {
        if (!__runOriginal)
        {
            return;
        }

        Log.Main.Debug?.Log("[LimitItems] ApplySorting_Pre");
        try
        {
            if (state != null && state.inventoryWidget == __instance) {
                // it's a mechlab screen, we do our own sort.
                var _cs = __instance.currentSort;
                var cst = _cs.Method;
                Log.Main.Debug?.Log($"OnApplySorting using {cst.DeclaringType.FullName}::{cst}");
                state.FilterChanged(false);
                __runOriginal = false;
            }
        }
        catch (Exception e)
        {
            Log.Main.Error?.Log("Encountered exception", e);
            throw;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MechLabPanel), nameof(MechLabPanel.MechCanEquipItem))]
    public static void MechCanEquipItem_Pre(ref bool __runOriginal, InventoryItemElement_NotListView item)
    {
        if (!__runOriginal)
        {
            return;
        }

        Log.Main.Trace?.Log("[LimitItems] MechCanEquipItem_Pre");

        // undo "fix NRE within Pool()" from earlier
        if (item.controller != null && item.controller.componentDef == null)
        {
            item.controller = null;
        }

        // no idea why this is here, just a NRE fix for vanilla?
        __runOriginal = item.ComponentRef != null;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MechLabInventoryWidget), nameof(MechLabInventoryWidget.OnItemGrab))]
    public static void OnItemGrab_Pre(ref bool __runOriginal, MechLabInventoryWidget __instance, ref IMechLabDraggableItem item)
    {
        if (!__runOriginal)
        {
            return;
        }

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

    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryItemElement_NotListView), nameof(InventoryItemElement_NotListView.OnDestroy))]
    public static void OnDestroy_Pre(ref bool __runOriginal, InventoryItemElement_NotListView __instance)
    {
        if (!__runOriginal)
        {
            return;
        }

        if (__instance.iconMech != null)
        {
            __instance.iconMech.sprite = null;
        }
        __runOriginal = false;
    }
}