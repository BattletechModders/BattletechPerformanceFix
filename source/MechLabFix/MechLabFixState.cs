using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BattleTech;
using BattleTech.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BattletechPerformanceFix.MechLabFix;

/* This patch fixes the slow inventory list creation within the mechlab. Without the fix, it manifests as a very long loadscreen where the indicator is frozen.

   The core of the problem is a lack of separation between Data & Visuals.
   Most of the logic requires operating on visual elements, which come from the asset pool (or a prefab if not in pool)
   additionally, the creation or modification of data causes preperation for re-render of the assets. (UpdateTooltips, UpdateDescription, Update....)

   Solution:
   Separate the data & visual elements entirely.
   Always process the data first, and then only create or re-use a couple of visual elements to display it.
   The user only sees 8 items at once, and they're expensive to create, so only make 8 of them.
*/
internal class MechLabFixState {
    internal static MechLabFixGameObjects GameObjects = new();

    public MechLabPanel instance;
    public MechLabInventoryWidget inventoryWidget;

    public List<ListElementController_BASE_NotListView> rawInventory;
    public List<ListElementController_BASE_NotListView> filteredInventory;

    // Index of current item element at the top of scrollrect
    public int rowToStartLoading = 0;
    public int rowCountBelowScreen = 0;
    public int rowMaxToStartLoading = 0;

    public MechLabFixState(MechLabPanel instance) {
        var sw = new Stopwatch();
        sw.Start();
        this.instance = instance;
        inventoryWidget = instance.inventoryWidget;

        Logging.Debug?.Log($"StorageInventory contains {instance.storageInventory.Count}");

        if (instance.IsSimGame) {
            instance.originalStorageInventory = instance.storageInventory;
        }

        Logging.Debug?.Log($"Mechbay Patch initialized :simGame? {instance.IsSimGame}");

        List<ListElementController_BASE_NotListView> BuildRawInventory()
            => instance.storageInventory.Select<MechComponentRef, ListElementController_BASE_NotListView>(componentRef => {
                componentRef.DataManager = instance.dataManager;
                componentRef.RefreshComponentDef();
                var count = (!instance.IsSimGame
                    ? int.MinValue
                    : instance.sim.GetItemCount((DescriptionDef)componentRef.Def.Description, componentRef.Def.GetType(), instance.sim.GetItemCountDamageType(componentRef)));

                if (componentRef.ComponentDefType == ComponentType.Weapon) {
                    var controller = new ListElementController_InventoryWeapon_NotListView();
                    controller.InitAndFillInSpecificWidget(componentRef, null, instance.dataManager, null, count, false);
                    return controller;
                } else {
                    var controller = new ListElementController_InventoryGear_NotListView();
                    controller.InitAndFillInSpecificWidget(componentRef, null, instance.dataManager, null, count, false);
                    return controller;
                }
            }).ToList();
        /* Build a list of data only for all components. */
        rawInventory = Sort(BuildRawInventory());
        // End
        Logging.Debug?.Log($"[LimitItems] inventory cached in {sw.Elapsed.TotalMilliseconds} ms");

        FilterChanged();
    }

    public ListElementController_BASE_NotListView FetchItem(MechComponentRef mcr)
    {
        return rawInventory.FirstOrDefault(ri => ri.componentDef == mcr.Def && mcr.DamageLevel == GetRef(ri).DamageLevel);
    }

    public ListElementController_BASE_NotListView FetchItem(MechComponentDef def)
    {
        return rawInventory.FirstOrDefault(ri => ri.componentDef == def);
    }

    public MechLabDraggableItemType ToDraggableType(MechComponentDef def) {
        switch(def.ComponentType) {
            case ComponentType.NotSet: return MechLabDraggableItemType.NOT_SET;
            case ComponentType.Weapon: return MechLabDraggableItemType.InventoryWeapon;
            case ComponentType.AmmunitionBox: return MechLabDraggableItemType.InventoryItem;
            case ComponentType.HeatSink: return MechLabDraggableItemType.InventoryGear;
            case ComponentType.JumpJet: return MechLabDraggableItemType.InventoryGear;
            case ComponentType.Upgrade: return MechLabDraggableItemType.InventoryGear;
            case ComponentType.Special: return MechLabDraggableItemType.InventoryGear;
            case ComponentType.MechPart: return MechLabDraggableItemType.InventoryGear;
        }
        return MechLabDraggableItemType.NOT_SET;
    }

    /* Fast sort, which works off data, rather than visual elements. 
           Since only 7 visual elements are allocated, this is required.
        */
    public List<ListElementController_BASE_NotListView> Sort(List<ListElementController_BASE_NotListView> items) {
        Logging.Trace?.Log($"Sorting: {items.Select(item => GetRef(item).ComponentDefID).ToArray().Dump(false)}");

        var sw = Stopwatch.StartNew();
        var cs = inventoryWidget.currentSort;
        Logging.Debug?.Log($"Sort using {cs.Method.DeclaringType.FullName}::{cs.Method}");

        var iieA = GameObjects.iieTmpA;
        var iieB = GameObjects.iieTmpB;

        var tmp = items.ToList();
        tmp.Sort((l,r) => {
            iieA.ComponentRef = GetRef(l);
            iieA.controller = l;
            iieA.controller.ItemWidget = iieA;
            iieA.ItemType = ToDraggableType(l.componentDef);

            iieB.ComponentRef = GetRef(r);
            iieB.controller = r;
            iieB.controller.ItemWidget = iieB;
            iieB.ItemType = ToDraggableType(r.componentDef);

            var res = cs.Invoke(iieA, iieB);
            Logging.Trace?.Log($"Compare {iieA.ComponentRef.ComponentDefID}({iieA != null},{iieA.controller.ItemWidget != null}) & {iieB.ComponentRef.ComponentDefID}({iieB != null},{iieB.controller.ItemWidget != null}) -> {res}");
            return res;
        });

        var delta = sw.Elapsed.TotalMilliseconds;
        Logging.Info?.Log($"Sorted in {delta} ms");

        Logging.Trace?.Log($"Sorted: {tmp.Select(item => GetRef(item).ComponentDefID).ToArray().Dump(false)}");

        return tmp;
    }

    /* Fast filtering code which works off the data, rather than the visual elements.
           Suboptimal due to potential desyncs with normal filter proceedure, but simply required for performance */
    public List<ListElementController_BASE_NotListView> Filter(List<ListElementController_BASE_NotListView> _items) {
        var items = Sort(_items);

        var filter = new InventoryFilter( allAllowed: false //this.filteringAll
            , weaponsAllowed: inventoryWidget.filteringWeapons
            , weaponsBallisticAllowed: inventoryWidget.filterEnabledWeaponBallistic
            , weaponsEnergyAllowed: inventoryWidget.filterEnabledWeaponEnergy
            , weaponsMissileAllowed: inventoryWidget.filterEnabledWeaponMissile
            , weaponsPersonnelAllowed: inventoryWidget.filterEnabledWeaponSmall
            , gearAllowed: inventoryWidget.filteringEquipment
            , gearHeatSinksAllowed: inventoryWidget.filterEnabledHeatsink
            , gearJumpJetsAllowed: inventoryWidget.filterEnabledJumpjet
            , mechTonnageForJumpJets: inventoryWidget.mechTonnage
            , gearUpgradesAllowed: inventoryWidget.filterEnabledUpgrade
            , mechsAllowed: false
            , ammoAllowed: true);

        InventoryDataObject_BASE tmpctl = new InventoryDataObject_InventoryWeapon();

        var current = items.Where(item => { 
            tmpctl.weaponDef = null;
            tmpctl.ammoBoxDef = null;
            tmpctl.componentDef = null;
            var def = item.componentDef;
            switch (def.ComponentType) {
                case ComponentType.Weapon:
                    tmpctl.weaponDef = def as WeaponDef;
                    break;
                case ComponentType.AmmunitionBox:
                    tmpctl.ammoBoxDef = def as AmmunitionBoxDef;
                    break;
                case ComponentType.HeatSink:
                case ComponentType.MechPart:
                case ComponentType.JumpJet:
                case ComponentType.Upgrade:
                    tmpctl.componentDef = def;
                    break;
            }
            var Summary = () =>
            {
                var o = "";
                o += "filteringWeapons? " + inventoryWidget.filteringWeapons + "\n";
                o += "filterEnabledWeaponBallistic? " + inventoryWidget.filterEnabledWeaponBallistic + "\n";
                o += "filterEnabledWeaponEnergy? " + inventoryWidget.filterEnabledWeaponEnergy + "\n";
                o += "filterEnabledWeaponMissile? " + inventoryWidget.filterEnabledWeaponMissile + "\n";
                o += "filterEnabledWeaponSmall? " + inventoryWidget.filterEnabledWeaponSmall + "\n";
                o += "filteringEquipment? " + inventoryWidget.filteringEquipment + "\n";
                o += "filterEnabledHeatsink? " + inventoryWidget.filterEnabledHeatsink + "\n";
                o += "filterEnabledJumpjet? " + inventoryWidget.filterEnabledJumpjet + "\n";
                o += "mechTonnage? " + inventoryWidget.mechTonnage + "\n";
                o += "filterEnabledUpgrade? " + inventoryWidget.filterEnabledUpgrade + "\n";
                o += $"weaponDef? {tmpctl.weaponDef}\n";
                o += $"ammoboxDef? {tmpctl.ammoBoxDef}\n";
                o += $"componentDef? {tmpctl.componentDef}\n";
                o += $"ComponentDefType? {tmpctl.componentDef?.ComponentType}\n";
                o += $"componentDefCSType? {tmpctl.componentDef?.GetType()?.FullName}\n";
                var json = Extensions.Trap(() => tmpctl.componentDef.ToJSON());
                o += "JSON: " + json;
                return o;
            };

            var yes = Extensions.Trap(() => filter.Execute(Enumerable.Repeat(tmpctl, 1)).Any()
                ,() => { Logging.Error?.Log($"Filtering failed\n{Summary()}\n\n"); return false; });
            if (!yes) Logging.Debug?.Log($"[Filter] Removing :id {def.Description.Id} :componentType {def.ComponentType} :quantity {item.quantity}");
            return yes;
        }).ToList();
        return current;
    }

    /* Most mods hook the visual element code to filter. This function will do that as quickly as possible
           by re-using a single visual element.
        */
    public List<ListElementController_BASE_NotListView> FilterUsingHBSCode(List<ListElementController_BASE_NotListView> items) {
        try {
            var sw = new Stopwatch();
            sw.Start();
            var tmp = inventoryWidget.localInventory;
            var iw = GameObjects.iieTmpT;

            // Filter items once using the faster code, then again to handle mods.
            var okItems = Filter(items).Where(lec => {
                inventoryWidget.localInventory = Enumerable.Repeat(iw, 1).ToList();
                var cref = GetRef(lec);
                lec.ItemWidget = iw;
                iw.ComponentRef = cref;
                // Not using SetData here still works, but is much slower
                // TODO: Figure out why.
                iw.SetData(lec, inventoryWidget, lec.quantity, false, null);
                if (!iw.gameObject.activeSelf) { 
                    // Set active is very very slow, only call if absolutely needed
                    // It would be preferable to hook SetActive, but it's an external function.
                    iw.gameObject.SetActive(true); 
                }
                filterGuard = true;
                // Let the main game or any mods filter if needed
                // filter guard is to prevent us from infinitely recursing here, as this is also our triggering patch.
                Extensions.Trap(() => { inventoryWidget.ApplyFiltering(false); return 0; }
                    , () =>
                    {
                        // We don't display bad items
                        iw.gameObject.SetActive(false);

                        var fst = inventoryWidget.localInventory.Count > 0 ? inventoryWidget.localInventory[0] : null;
                        var o = "";
                        o += $"Widget? {fst != null}\n";
                        o += $"Controller? {fst?.controller != null}\n";
                        o += $"ComponentDef? {fst?.controller?.componentDef != null}\n";
                        o += $"ComponentDefType? {fst?.controller?.componentDef?.ComponentType}\n";
                        o += $"componentDefCSType? {fst?.controller?.componentDef?.GetType()?.FullName}\n";
                        o += $"ComponentRef? {fst?.ComponentRef != null}\n";
                        o += $"ComponentRefDef? {fst?.ComponentRef?.Def != null}\n";
                        o += $"ComponentRefType? {fst?.ComponentRef?.Def?.ComponentType}\n";
                        o += $"componentRefCSType? {fst?.ComponentRef?.Def?.GetType()?.FullName}\n";

                        var def = (fst?.controller?.componentDef) ?? (fst?.ComponentRef?.Def);
                        var json = Extensions.Trap(() => def.ToJSON());
                        o += "JSON: " + json;
                        Logging.Error?.Log($"FilterSummary: \n{o}\n\n");
                        return 0;
                    });;
                filterGuard = false;
                lec.ItemWidget = null;
                var yes = iw.gameObject.activeSelf == true;
                if (!yes) Logging.Debug?.Log($"[FilterUsingHBSCode] Removing :id {lec.componentDef.Description.Id} :componentType {lec.componentDef.ComponentType} :quantity {lec.quantity} :tonnage {(inventoryWidget.ParentDropTarget as MechLabPanel)?.activeMechDef?.Chassis?.Tonnage}");
                return yes;
            }).ToList();
            inventoryWidget.localInventory = tmp;
            Logging.Info?.Log($"Filter took {sw.Elapsed.TotalMilliseconds} ms and resulted in {items.Count} -> {okItems.Count} items");

            return okItems;
        } catch (Exception e) {
            Logging.Error?.Log("Encountered exception", e);
            return null;
        }
    }

    public MechComponentRef GetRef(ListElementController_BASE_NotListView lec) {
        if (lec is ListElementController_InventoryWeapon_NotListView lecIw) return lecIw.componentRef;
        if (lec is ListElementController_InventoryGear_NotListView lecIg) return lecIg.componentRef;
        Logging.Error?.Log("[LimitItems] lec is not gear or weapon: " + lec.GetId());
        return null;
    }

    /* The user has changed a filter, and we rebuild the item cache. */
    public void FilterChanged(bool resetIndex = true) {
        try {
            Logging.Debug?.Log(string.Format("[LimitItems] Filter changed (reset? {9}):\n  :weapons {0}\n  :equip {1}\n  :ballistic {2}\n  :energy {3}\n  :missile {4}\n  :small {5}\n  :heatsink {6}\n  :jumpjet {7}\n  :upgrade {8}"
                , inventoryWidget.filteringWeapons
                , inventoryWidget.filteringEquipment
                , inventoryWidget.filterEnabledWeaponBallistic
                , inventoryWidget.filterEnabledWeaponEnergy
                , inventoryWidget.filterEnabledWeaponMissile
                , inventoryWidget.filterEnabledWeaponSmall
                , inventoryWidget.filterEnabledHeatsink
                , inventoryWidget.filterEnabledJumpjet
                , inventoryWidget.filterEnabledUpgrade
                , resetIndex));
            if (resetIndex) {
                inventoryWidget.scrollbarArea.verticalNormalizedPosition = 1.0f;
                rowToStartLoading = 0;
            }

            filteredInventory = MechLabFixPublic.FilterFunc(rawInventory);
            rowCountBelowScreen = Mathf.Max(0, filteredInventory.Count - itemsOnScreen);
            rowMaxToStartLoading = Mathf.Max(0, rowCountBelowScreen - rowBufferCount);
            rowToStartLoading = Mathf.Clamp(rowToStartLoading, 0, rowMaxToStartLoading);
            Refresh();
        } catch (Exception e) {
            Logging.Error?.Log("Encountered exception", e);
        }
    }

    public void Refresh() {
        Logging.Debug?.Log($"[LimitItems] Refresh: {rowToStartLoading} {filteredInventory.Count} {itemLimit} {inventoryWidget.scrollbarArea.verticalNormalizedPosition}");

        Func<ListElementController_BASE_NotListView, string> pp = lec => $"[id:{GetRef(lec).ComponentDefID},damage:{GetRef(lec).DamageLevel},quantity:{lec.quantity},id:{lec.GetId()}]";

        var toShow = filteredInventory.Skip(rowToStartLoading).Take(itemLimit).ToList();
        var icc = GameObjects.ielCache.ToList();

        Logging.Trace?.Log("[LimitItems] Showing: " + string.Join(", ", toShow.Select(pp).ToArray()));

        var details = new List<string>();

        toShow.ForEach(lec => {
            var iw = icc[0]; icc.RemoveAt(0);
            var cref = GetRef(lec);
            iw.ClearEverything();
            iw.ComponentRef = cref;
            lec.ItemWidget = iw;
            iw.SetData(lec, inventoryWidget, lec.quantity, false, null);
            lec.SetupLook(iw);
            iw.gameObject.SetActive(true);
            details.Insert(0,
                $"enabled {iw.ComponentRef.ComponentDefID} {iw.GetComponent<UnityEngine.RectTransform>().anchoredPosition}");
        });
        icc.ForEach(unused => unused.gameObject.SetActive(false));

        var elementHeight = 64;
        var spacingY = 16;
        var halfSpacingY = spacingY / 2;
        var paddingY = elementHeight + spacingY;

        var topCount = rowToStartLoading;
        var bottomCount = filteredInventory.Count - (rowToStartLoading + GameObjects.ielCache.Count(ii => ii.gameObject.activeSelf));

        var vlg = inventoryWidget.listParent.GetComponent<VerticalLayoutGroup>();
        var padding = vlg.padding;
        padding.top = 12 + (topCount > 0 ? paddingY * topCount - halfSpacingY : 0);
        padding.bottom = 12 + (bottomCount > 0 ? paddingY * bottomCount - halfSpacingY : 0);
        vlg.padding = padding;

        LayoutRebuilder.MarkLayoutForRebuild(vlg.GetComponent<RectTransform>());

        instance.RefreshInventorySelectability();
        Logging.Trace?.Log($"[LimitItems] RefreshDone dummystart {padding} vnp {inventoryWidget.scrollbarArea.verticalNormalizedPosition} lli {"(" + string.Join(", ", details.ToArray()) + ")"}");
    }

    public static readonly int itemsOnScreen = 7;
    // Maximum # of visual elements to allocate (will be used for slightly off screen elements.)
    internal static readonly int rowBufferCount = 1;
    internal static readonly int itemLimit = itemsOnScreen + rowBufferCount;

    public static bool filterGuard = false;
}