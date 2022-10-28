using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BattleTech;
using BattleTech.UI;
using Harmony;
using UnityEngine;
using static BattletechPerformanceFix.Extensions;
using Object = UnityEngine.Object;

namespace BattletechPerformanceFix.MechLabFix;

internal class MechLabFixFeature : Feature {
    public void Activate() {
        "InitWidgets".Transpile<MechLabPanel>();
        "InitWidgets".Pre<MechLabPanel>();
        "OnPooled".Pre<MechLabPanel>();
        "OnPooled".Post<MechLabPanel>();
        "PopulateInventory".Pre<MechLabPanel>();
        "LateUpdate".Pre<UnityEngine.UI.ScrollRect>();
        "OnAddItem".Pre<MechLabInventoryWidget>();
        "OnRemoveItem".Pre<MechLabInventoryWidget>();
        "OnItemGrab".Pre<MechLabInventoryWidget>();
        "ApplyFiltering".Pre<MechLabInventoryWidget>("ApplyFiltering_Pre", Priority.First);
        "MechCanEquipItem".Pre<MechLabPanel>();
        "ApplySorting".Pre<MechLabInventoryWidget>("ApplySorting_Pre", Priority.First);

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
        LogDebug("[LimitItems] CreateUIModule");
        try {
            return uiManager.GetOrCreateUIModule<SG_Shop_Screen>(prefabOverride, resort);
        } catch(Exception e) {
            LogException(e);
            throw;
        }
    }

    public static void InitWidgets_Pre(MechLabPanel __instance)
    {
        LogDebug("[LimitItems] InitWidgets_Pre");
        try {
            if (__instance.Shop != null)
            {
                __instance.Shop.Pool();
            }
        } catch(Exception e) {
            LogException(e);
        }
    }

#endregion

internal static MechLabFixState state;

    public static bool PopulateInventory_Pre(MechLabPanel __instance)
    {
        LogDebug("[LimitItems] PopulateInventory_Pre");
        try {
            if (state != null)
            {
                LogInfo("[LimitItems] state.Dispose");
                state.Dispose();
                state = null;
            }
            state = new MechLabFixState(__instance);
        } catch(Exception e) {
            LogException(e);
        }
        return false;
    }

    // TODO make the patch a component so we can avoid pooling here
    public static void OnPooled_Pre()
    {
        LogDebug("[LimitItems] OnPooled_Pre");
        return;
        if (state == null)
        {
            LogError("[LimitItems] Unhandled OnPooled");
            return;
        }

        LogDebug("[LimitItems] Pooled mechlab");
        try
        {
            state?.Dispose();
            state = null;
        }
        catch (Exception e)
        {
            LogException(e);
        }
    }

    public static void OnPooled_Post(MechLabPanel __instance){
        LogDebug("[LimitItems] OnPooled_Post");
        // return;
        try
        {
            state?.Dispose();
            state = null;
        }
        catch (Exception e)
        {
            LogException(e);
        }
        return;
#region fix for accumulating inventory elements by cleaning the pool and loose objects while excluding the prefab
        var prefabCache = __instance.dataManager.GameObjectPool;
        var prefabName = ListElementController_BASE_NotListView.INVENTORY_ELEMENT_PREFAB_NotListView;

        prefabCache.prefabPool.TryGetValue(prefabName, out var prefab);
        if (prefabCache.gameObjectPool.TryGetValue(prefabName, out var linkedList))
        {
            foreach (var @object in linkedList)
            {
                Object.DestroyImmediate(@object);
            }
            linkedList.Clear();
        }

        foreach (var component in Resources.FindObjectsOfTypeAll<InventoryItemElement_NotListView>())
        {
            if (component.transform.parent == null && component.gameObject != prefab)
            {
                Object.DestroyImmediate(component.gameObject);
            }
        }
#endregion
    }

    public static void LateUpdate_Pre(UnityEngine.UI.ScrollRect __instance)
    {
        LogDebug("[LimitItems] LateUpdate_Pre");
        try
        {
            if (state != null && state.inventoryWidget.scrollbarArea == __instance) {
                var newIndex = (int)((state.endIndex) * (1.0f - __instance.verticalNormalizedPosition));
                if (state.filteredInventory.Count < MechLabFixState.itemsOnScreen) {
                    newIndex = 0;
                }
                if (state.index != newIndex) {
                    state.index = newIndex;
                    LogDebug($"[LimitItems] Refresh with: {newIndex} {__instance.verticalNormalizedPosition}");
                    state.Refresh(false);
                }
            }
        }
        catch (Exception e)
        {
            LogException(e);
        }
    }

    public static bool OnAddItem_Pre(MechLabInventoryWidget __instance, IMechLabDraggableItem item)
    {
        LogDebug("[LimitItems] OnAddItem_Pre");
        if (state != null && state.inventoryWidget == __instance) {
            try {
                var nlv = item as InventoryItemElement_NotListView;
                var quantity = nlv == null ? 1 : nlv.controller.quantity;
                var existing = state.FetchItem(item.ComponentRef);
                if (existing == null) {
                    LogDebug($"OnAddItem new {quantity}");
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
                    LogDebug($"OnAddItem existing {quantity}");
                    if (existing.quantity != Int32.MinValue) {
                        existing.ModifyQuantity(quantity);
                    }
                    state.Refresh(false);
                }
            } catch(Exception e) {
                LogException(e);
            }
            return false;
        } else {
            return true;
        }
    }

    public static bool OnRemoveItem_Pre(MechLabInventoryWidget __instance, IMechLabDraggableItem item)
    {
        LogDebug("[LimitItems] OnRemoveItem_Pre");
        if (state != null && state.inventoryWidget == __instance) {
            try {
                var nlv = item as InventoryItemElement_NotListView;

                var existing = state.FetchItem(item.ComponentRef);
                if (existing == null) {
                    LogError($"OnRemoveItem new (should be impossible?) {nlv.controller.quantity}");
                } else {
                    LogDebug($"OnRemoveItem existing {nlv.controller.quantity}");
                    if (existing.quantity != Int32.MinValue) {
                        existing.ModifyQuantity(-1);
                        if (existing.quantity < 1)
                            state.rawInventory.Remove(existing);
                    }
                    state.FilterChanged(false);
                    state.Refresh(false);
                }
            } catch(Exception e) {
                LogException(e);
            }
            return false;
        } else {
            return true;
        }
    }

    public static bool ApplyFiltering_Pre(MechLabInventoryWidget __instance, bool refreshPositioning)
    {
        LogDebug("[LimitItems] ApplyFiltering_Pre");
        try
        {
            if (state != null && state.inventoryWidget == __instance && !MechLabFixState.filterGuard) {
                LogDebug($"OnApplyFiltering (refresh-pos? {refreshPositioning})");
                state.FilterChanged(refreshPositioning);
                return false;
            } else {
                return true;
            }
        }
        catch (Exception e)
        {
            LogException(e);
            throw;
        }
    }

    public static bool ApplySorting_Pre(MechLabInventoryWidget __instance)
    {
        LogDebug("[LimitItems] ApplySorting_Pre");
        try
        {
            if (state != null && state.inventoryWidget == __instance) {
                // it's a mechlab screen, we do our own sort.
                var _cs = __instance.currentSort;
                var cst = _cs.Method;
                LogDebug($"OnApplySorting using {cst.DeclaringType.FullName}::{cst.ToString()}");
                state.FilterChanged(false);
                return false;
            } else {
                return true;
            }
        }
        catch (Exception e)
        {
            LogException(e);
            throw;
        }
    }

    public static bool MechCanEquipItem_Pre(InventoryItemElement_NotListView item)
    {
        LogDebug("[LimitItems] MechCanEquipItem_Pre");
        return item.ComponentRef == null ? false : true;
    }

    public static void OnItemGrab_Pre(MechLabInventoryWidget __instance, ref IMechLabDraggableItem item) {
        LogDebug("[LimitItems] OnItemGrab_Pre");
        if (state != null && state.inventoryWidget == __instance) {
            try {
                LogDebug("OnItemGrab");
                var nlv = item as InventoryItemElement_NotListView;
                // TODO MEMORY LEAK!!!!!!!!!!
                var iw = state.instance.dataManager
                    .PooledInstantiate(ListElementController_BASE_NotListView.INVENTORY_ELEMENT_PREFAB_NotListView, BattleTechResourceType.UIModulePrefabs)
                    .GetComponent<InventoryItemElement_NotListView>();
                var lec = nlv.controller;
                var cref = state.GetRef(lec);
                iw.ClearEverything();
                iw.ComponentRef = cref;
                lec.ItemWidget = iw;
                iw.SetData(lec, state.inventoryWidget, lec.quantity);
                lec.SetupLook(iw);
                iw.gameObject.SetActive(true);
                item = iw;
                Object.DestroyImmediate(nlv);
            } catch(Exception e) {
                LogException(e);
            }
        }
    }

}