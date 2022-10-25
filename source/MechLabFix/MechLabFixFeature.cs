using System;
using BattleTech;
using BattleTech.UI;
using Harmony;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix.MechLabFix;

public class MechLabFixFeature : Feature {
    public void Activate() {
        "PopulateInventory".Pre<MechLabPanel>();
        "ExitMechLab".Pre<MechLabPanel>();
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

    public static PatchMechlabLimitItems state;

    public static bool PopulateInventory_Pre(MechLabPanel __instance)
    {
        if (state != null) LogError("[LimitItems] PopulateInventory was not properly cleaned");
        LogDebug("[LimitItems] PopulateInventory patching (Mechlab fix)");
        state = new PatchMechlabLimitItems(__instance);
        return false;
    }

    public static void ExitMechLab_Pre(MechLabPanel __instance)
    {
        if (state == null) { LogError("[LimitItems] Unhandled ExitMechLab"); return; }
        LogDebug("[LimitItems] Exiting mechlab");
        state.Dispose();
        state = null;
    }

    public static void LateUpdate_Pre(UnityEngine.UI.ScrollRect __instance)
    {
        if (state != null && state.inventoryWidget.scrollbarArea == __instance) {
            var newIndex = (int)((state.endIndex) * (1.0f - __instance.verticalNormalizedPosition));
            if (state.filteredInventory.Count < PatchMechlabLimitItems.itemsOnScreen) {
                newIndex = 0;
            }
            if (state.index != newIndex) {
                state.index = newIndex;
                LogDebug(string.Format("[LimitItems] Refresh with: {0} {1}", newIndex, __instance.verticalNormalizedPosition));
                state.Refresh(false);
            }
        }
    }

    public static bool OnAddItem_Pre(MechLabInventoryWidget __instance, IMechLabDraggableItem item)
    {
        if (state != null && state.inventoryWidget == __instance) {
            try {
                var nlv = item as InventoryItemElement_NotListView;
                var quantity = nlv == null ? 1 : nlv.controller.quantity;
                var existing = state.FetchItem(item.ComponentRef);
                if (existing == null) {
                    LogDebug(string.Format("OnAddItem new {0}", quantity));
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
                    LogDebug(string.Format("OnAddItem existing {0}", quantity));
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
        if (state != null && state.inventoryWidget == __instance) {
            try {
                var nlv = item as InventoryItemElement_NotListView;

                var existing = state.FetchItem(item.ComponentRef);
                if (existing == null) {
                    LogError(string.Format("OnRemoveItem new (should be impossible?) {0}", nlv.controller.quantity));
                } else {
                    LogDebug(string.Format("OnRemoveItem existing {0}", nlv.controller.quantity));
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
        if (state != null && state.inventoryWidget == __instance && !PatchMechlabLimitItems.filterGuard) {
            LogDebug(string.Format("OnApplyFiltering (refresh-pos? {0})", refreshPositioning));
            state.FilterChanged(refreshPositioning);
            return false;
        } else {
            return true;
        }
    }

    public static bool ApplySorting_Pre(MechLabInventoryWidget __instance)
    {
        if (state != null && state.inventoryWidget == __instance) {
            // it's a mechlab screen, we do our own sort.
            var _cs = __instance.currentSort;
            var cst = _cs.Method;
            LogDebug(string.Format("OnApplySorting using {0}::{1}", cst.DeclaringType.FullName, cst.ToString()));
            state.FilterChanged(false);
            return false;
        } else {
            return true;
        }
    }

    public static bool MechCanEquipItem_Pre(InventoryItemElement_NotListView item)
    {
        return item.ComponentRef == null ? false : true;
    }

    public static void OnItemGrab_Pre(MechLabInventoryWidget __instance, ref IMechLabDraggableItem item) {
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
            } catch(Exception e) {
                LogException(e);
            }
        }
    }

}