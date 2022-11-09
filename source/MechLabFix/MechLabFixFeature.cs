using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BattleTech;
using BattleTech.UI;
using Harmony;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix.MechLabFix;

internal class MechLabFixFeature : Feature {
    public void Activate() {
        "InitWidgets".Transpile<MechLabPanel>();
        "InitWidgets".Pre<MechLabPanel>();
        "PopulateInventory".Pre<MechLabPanel>();
        "LateUpdate".Pre<UnityEngine.UI.ScrollRect>();
        "ClearInventory".Pre<MechLabInventoryWidget>();
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
        Logging.Debug?.Log("[LimitItems] CreateUIModule");
        try {
            return uiManager.GetOrCreateUIModule<SG_Shop_Screen>(prefabOverride, resort);
        } catch(Exception e) {
            Logging.Error?.Log("Encountered exception", e);
            throw;
        }
    }

    public static void InitWidgets_Pre(MechLabPanel __instance)
    {
        Logging.Debug?.Log("[LimitItems] InitWidgets_Pre");
        try {
            if (__instance.Shop != null)
            {
                __instance.Shop.Pool();
            }
        } catch(Exception e) {
            Logging.Error?.Log("Encountered exception", e);
        }
    }

#endregion

    internal static MechLabFixState state;

    public static bool PopulateInventory_Pre(MechLabPanel __instance)
    {
        Logging.Debug?.Log("[LimitItems] PopulateInventory_Pre");
        // return;
        try
        {
            MechLabFixState.GameObjects.Setup(__instance.inventoryWidget);
            state = new(__instance);
        } catch(Exception e) {
            Logging.Error?.Log("Encountered exception", e);
        }
        return false;
    }

    public static void ClearInventory_Pre(MechLabInventoryWidget __instance)
    {
        Logging.Debug?.Log("[LimitItems] ClearInventory_Pre");
        try
        {
            Logging.Debug?.Log($"inventoryCount={__instance.localInventory?.Count}");
            foreach (var iie in __instance.localInventory)
            {
                // fix NRE within Pool()
                iie.controller = new ListElementController_InventoryGear_NotListView();
                iie.controller.dataManager = iie.dataManager = UnityGameInstance.BattleTechGame.DataManager;
                iie.controller.ItemWidget = iie;
            }
        } catch(Exception e) {
            Logging.Error?.Log("Encountered exception", e);
        }
    }

    public static void LateUpdate_Pre(UnityEngine.UI.ScrollRect __instance)
    {
        try
        {
            if (state != null && state.inventoryWidget.scrollbarArea == __instance) {
                var newIndex = (int)((state.endIndex) * (1.0f - __instance.verticalNormalizedPosition));
                if (state.filteredInventory.Count < MechLabFixState.itemsOnScreen) {
                    newIndex = 0;
                }
                if (state.index != newIndex) {
                    state.index = newIndex;
                    Logging.Debug?.Log($"[LimitItems] Refresh with: {newIndex} {__instance.verticalNormalizedPosition}");
                    state.Refresh(false);
                }
            }
        }
        catch (Exception e)
        {
            Logging.Error?.Log("Encountered exception", e);
        }
    }

    public static bool OnAddItem_Pre(MechLabInventoryWidget __instance, IMechLabDraggableItem item)
    {
        Logging.Debug?.Log("[LimitItems] OnAddItem_Pre");
        if (state != null && state.inventoryWidget == __instance) {
            try {
                var nlv = item as InventoryItemElement_NotListView;
                var quantity = nlv == null ? 1 : nlv.controller.quantity;
                var existing = state.FetchItem(item.ComponentRef);
                if (existing == null) {
                    Logging.Debug?.Log($"OnAddItem new {quantity}");
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
                    Logging.Debug?.Log($"OnAddItem existing {quantity}");
                    if (existing.quantity != Int32.MinValue) {
                        existing.ModifyQuantity(quantity);
                    }
                    state.Refresh(false);
                }
            } catch(Exception e) {
                Logging.Error?.Log("Encountered exception", e);
            }
            return false;
        }
        return true;
    }

    public static bool OnRemoveItem_Pre(MechLabInventoryWidget __instance, IMechLabDraggableItem item)
    {
        Logging.Debug?.Log("[LimitItems] OnRemoveItem_Pre");
        if (state != null && state.inventoryWidget == __instance) {
            try {
                var nlv = item as InventoryItemElement_NotListView;

                var existing = state.FetchItem(item.ComponentRef);
                if (existing == null) {
                    Logging.Error?.Log($"OnRemoveItem new (should be impossible?) {nlv.controller.quantity}");
                } else {
                    Logging.Debug?.Log($"OnRemoveItem existing {nlv.controller.quantity}");
                    if (existing.quantity != Int32.MinValue) {
                        existing.ModifyQuantity(-1);
                        if (existing.quantity < 1)
                            state.rawInventory.Remove(existing);
                    }
                    state.FilterChanged(false);
                    state.Refresh(false);
                }
            } catch(Exception e) {
                Logging.Error?.Log("Encountered exception", e);
            }
            return false;
        }
        return true;
    }

    public static bool ApplyFiltering_Pre(MechLabInventoryWidget __instance, bool refreshPositioning)
    {
        Logging.Debug?.Log("[LimitItems] ApplyFiltering_Pre");
        try
        {
            if (state != null && state.inventoryWidget == __instance && !MechLabFixState.filterGuard) {
                Logging.Debug?.Log($"OnApplyFiltering (refresh-pos? {refreshPositioning})");
                state.FilterChanged(refreshPositioning);
                return false;
            } else {
                return true;
            }
        }
        catch (Exception e)
        {
            Logging.Error?.Log("Encountered exception", e);
            throw;
        }
    }

    public static bool ApplySorting_Pre(MechLabInventoryWidget __instance)
    {
        Logging.Debug?.Log("[LimitItems] ApplySorting_Pre");
        try
        {
            if (state != null && state.inventoryWidget == __instance) {
                // it's a mechlab screen, we do our own sort.
                var _cs = __instance.currentSort;
                var cst = _cs.Method;
                Logging.Debug?.Log($"OnApplySorting using {cst.DeclaringType.FullName}::{cst}");
                state.FilterChanged(false);
                return false;
            } else {
                return true;
            }
        }
        catch (Exception e)
        {
            Logging.Error?.Log("Encountered exception", e);
            throw;
        }
    }

    public static bool MechCanEquipItem_Pre(InventoryItemElement_NotListView item)
    {
        Logging.Spam?.Log("[LimitItems] MechCanEquipItem_Pre");

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
        Logging.Debug?.Log("[LimitItems] OnItemGrab_Pre");
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
            Logging.Error?.Log("Encountered exception", e);
            throw;
        }
    }
}