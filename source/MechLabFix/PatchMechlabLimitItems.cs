using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BattleTech;
using BattleTech.UI;

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
public class PatchMechlabLimitItems {
    public MechLabPanel instance;
    public MechLabInventoryWidget inventoryWidget;

    public List<InventoryItemElement_NotListView> ielCache;

    public List<ListElementController_BASE_NotListView> rawInventory;
    public List<ListElementController_BASE_NotListView> filteredInventory;

    // Index of current item element at the top of scrollrect
    public int index = 0;

    public int endIndex = 0;

    // Temporary visual element used in the filter process.
    public InventoryItemElement_NotListView iieTmp;

    public PatchMechlabLimitItems(MechLabPanel instance) {
        try {
            var sw = new Stopwatch();
            sw.Start();
            this.instance = instance;
            this.inventoryWidget = instance.inventoryWidget.LogIfNull("inventoryWidget is null");

            Extensions.LogDebug($"StorageInventory contains {instance.storageInventory.Count}");

            if (instance.IsSimGame) {
                instance.originalStorageInventory = instance.storageInventory.LogIfNull("storageInventory is null");
            }

            Extensions.LogDebug($"Mechbay Patch initialized :simGame? {instance.IsSimGame}");

            List<ListElementController_BASE_NotListView> BuildRawInventory()
                => instance.storageInventory.Select<MechComponentRef, ListElementController_BASE_NotListView>(componentRef => {
                    Extensions.LogIfNull<MechComponentRef>(componentRef, "componentRef is null");
                    componentRef.DataManager = instance.dataManager.LogIfNull("(MechLabPanel instance).dataManager is null");
                    componentRef.RefreshComponentDef();
                    Extensions.LogIfNull<MechComponentDef>(componentRef.Def, "componentRef.Def is null");
                    var count = (!instance.IsSimGame
                        ? int.MinValue
                        : instance.sim.GetItemCount((DescriptionDef)componentRef.Def.Description, componentRef.Def.GetType(), instance.sim.GetItemCountDamageType(componentRef)));

                    if (componentRef.ComponentDefType == ComponentType.Weapon) {
                        ListElementController_InventoryWeapon_NotListView controller = new ListElementController_InventoryWeapon_NotListView();
                        controller.InitAndFillInSpecificWidget(componentRef, null, instance.dataManager, null, count, false);
                        return controller;
                    } else {
                        ListElementController_InventoryGear_NotListView controller = new ListElementController_InventoryGear_NotListView();
                        controller.InitAndFillInSpecificWidget(componentRef, null, instance.dataManager, null, count, false);
                        return controller;
                    }
                }).ToList();
            /* Build a list of data only for all components. */
            rawInventory = Sort(BuildRawInventory());

            InventoryItemElement_NotListView mkiie(bool nonexistant) {
                // TODO MEMORY LEAK!!!!!!!!!!
                var nlv = instance.dataManager
                    .PooledInstantiate(ListElementController_BASE_NotListView.INVENTORY_ELEMENT_PREFAB_NotListView, BattleTechResourceType.UIModulePrefabs)
                    .LogIfNull("Unable to instantiate INVENTORY_ELEMENT_PREFAB_NotListView")
                    .GetComponent<InventoryItemElement_NotListView>()
                    .LogIfNull("Inventory_Element_prefab does not contain a NLV");
                nlv.gameObject.IsDestroyedError("NLV gameObject has been destroyed");
                nlv.gameObject.LogIfNull("NLV gameObject has been destroyed");
                if (!nonexistant) {
                    nlv.SetRadioParent(inventoryWidget.inventoryRadioSet.LogIfNull("inventoryRadioSet is null"));
                    nlv.gameObject.transform.SetParent(inventoryWidget.listParent.LogIfNull("listParent is null"), false);
                    nlv.gameObject.transform.localScale = UnityEngine.Vector3.one;
                }
                return nlv;
            };

            iieTmp = mkiie(true);

            /* Allocate very few visual elements, as this is extremely slow for both allocation and deallocation.
                   It's the difference between a couple of milliseconds and several seconds for many unique items in inventory 
                   This is the core of the fix, the rest is just to make it work within HBS's existing code.
                */
            List<InventoryItemElement_NotListView> make_ielCache()
                => Enumerable.Repeat<Func<InventoryItemElement_NotListView>>( () => mkiie(false), itemLimit)
                    .Select(thunk => thunk())
                    .ToList();
            ielCache = make_ielCache();
                    
            var li = inventoryWidget.localInventory;
            ielCache.ForEach(iw => li.Add(iw));
            // End

            var lp = inventoryWidget.listParent;

            // DummyStart&End are blank rects stored at the beginning and end of the list so that unity knows how big the scrollrect should be
            // "placeholders"
            if (DummyStart == null) DummyStart = new UnityEngine.GameObject().AddComponent<UnityEngine.RectTransform>();
            if (DummyEnd   == null) DummyEnd   = new UnityEngine.GameObject().AddComponent<UnityEngine.RectTransform>();

            DummyStart.SetParent(lp, false);
            DummyEnd.SetParent(lp, false);
            Extensions.LogDebug(string.Format("[LimitItems] inventory cached in {0} ms", sw.Elapsed.TotalMilliseconds));

            FilterChanged();
        } catch(Exception e) {
            Extensions.LogException(e);
        }
    }

    public ListElementController_BASE_NotListView FetchItem(MechComponentRef mcr)
    {
        return rawInventory.Where(ri => ri.componentDef == mcr.Def && mcr.DamageLevel == GetRef(ri).DamageLevel).FirstOrDefault();
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
        Extensions.LogSpam($"Sorting: {items.Select(item => GetRef(item).ComponentDefID).ToArray().Dump(false)}");

        var sw = Stopwatch.StartNew();
        var _a = new ListElementController_InventoryGear_NotListView();
        var _b = new ListElementController_InventoryGear_NotListView();
        var go = new UnityEngine.GameObject();
        var _ac = go.AddComponent<InventoryItemElement_NotListView>(); //new InventoryItemElement_NotListView();
        var go2 = new UnityEngine.GameObject();
        var _bc = go2.AddComponent<InventoryItemElement_NotListView>();
        _ac.controller = _a;
        _bc.controller = _b;
        var _cs = inventoryWidget.currentSort;
        var cst = _cs.Method;
        Extensions.LogDebug(string.Format("Sort using {0}::{1}", cst.DeclaringType.FullName, cst.ToString()));

        var tmp = items.ToList();
        tmp.Sort(new Comparison<ListElementController_BASE_NotListView>((l,r) => {
            _ac.ComponentRef = _a.componentRef = GetRef(l);
            _bc.ComponentRef = _b.componentRef = GetRef(r);
            _ac.controller = l;
            _bc.controller = r;
            _ac.controller.ItemWidget = _ac;
            _bc.controller.ItemWidget = _bc;
            _ac.ItemType = ToDraggableType(l.componentDef);
            _bc.ItemType = ToDraggableType(r.componentDef);
            var res = _cs.Invoke(_ac, _bc);
            Extensions.LogSpam($"Compare {_a.componentRef.ComponentDefID}({_ac != null},{_ac.controller.ItemWidget != null}) & {_b.componentRef.ComponentDefID}({_bc != null},{_bc.controller.ItemWidget != null}) -> {res}");
            return res;
        }));

        UnityEngine.GameObject.Destroy(go);
        UnityEngine.GameObject.Destroy(go2);

        var delta = sw.Elapsed.TotalMilliseconds;
        Extensions.LogInfo(string.Format("Sorted in {0} ms", delta));

        Extensions.LogSpam($"Sorted: {tmp.Select(item => GetRef(item).ComponentDefID).ToArray().Dump(false)}");

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
            Func<string> Summary = () =>
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
                ,() => { Extensions.LogError($"Filtering failed\n{Summary()}\n\n"); return false; });
            if (!yes) Extensions.LogDebug(string.Format("[Filter] Removing :id {0} :componentType {1} :quantity {2}", def.Description.Id, def.ComponentType, item.quantity));
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
            var iw = iieTmp;

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
                        Extensions.LogError($"FilterSummary: \n{o}\n\n");
                        return 0;
                    });;
                filterGuard = false;
                lec.ItemWidget = null;
                var yes = iw.gameObject.activeSelf == true;
                if (!yes) Extensions.LogDebug(string.Format( "[FilterUsingHBSCode] Removing :id {0} :componentType {1} :quantity {2} :tonnage {3}"
                    , lec.componentDef.Description.Id
                    , lec.componentDef.ComponentType
                    , lec.quantity
                    , (inventoryWidget.ParentDropTarget as MechLabPanel)?.activeMechDef?.Chassis?.Tonnage));
                return yes;
            }).ToList();
            inventoryWidget.localInventory = tmp;
            Extensions.LogInfo(string.Format("Filter took {0} ms and resulted in {1} -> {2} items", sw.Elapsed.TotalMilliseconds, items.Count, okItems.Count));

            return okItems;
        } catch (Exception e) {
            Extensions.LogException(e);
            return null;
        }
    }

    public MechComponentRef GetRef(ListElementController_BASE_NotListView lec) {
        if (lec is ListElementController_InventoryWeapon_NotListView) return (lec as ListElementController_InventoryWeapon_NotListView).componentRef;
        if (lec is ListElementController_InventoryGear_NotListView) return (lec as ListElementController_InventoryGear_NotListView).componentRef;
        Extensions.LogError("[LimitItems] lec is not gear or weapon: " + lec.GetId());
        return null;
    }

    /* The user has changed a filter, and we rebuild the item cache. */
    public void FilterChanged(bool resetIndex = true) {
        try {
            Extensions.LogDebug(string.Format("[LimitItems] Filter changed (reset? {9}):\n  :weapons {0}\n  :equip {1}\n  :ballistic {2}\n  :energy {3}\n  :missile {4}\n  :small {5}\n  :heatsink {6}\n  :jumpjet {7}\n  :upgrade {8}"
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
                index = 0;
            }

            filteredInventory = FilterUsingHBSCode(rawInventory);
            endIndex = filteredInventory.Count - itemsOnScreen;
            Refresh();
        } catch (Exception e) {
            Extensions.LogException(e);
        }
    }

    public void Refresh(bool wantClobber = true) {
        Extensions.LogDebug(string.Format("[LimitItems] Refresh: {0} {1} {2} {3}", index, filteredInventory.Count, itemLimit, inventoryWidget.scrollbarArea.verticalNormalizedPosition));
        if (index > filteredInventory.Count - itemsOnScreen) {
            index = filteredInventory.Count - itemsOnScreen;
        }
        if (filteredInventory.Count < itemsOnScreen) {
            index = 0;
        }
        if (index < 0) {
            index = 0;
        }
        if (Extensions.Spam) Extensions.LogSpam(string.Format("[LimitItems] Refresh(F): {0} {1} {2} {3}", index, filteredInventory.Count, itemLimit, inventoryWidget.scrollbarArea.verticalNormalizedPosition));

        Func<ListElementController_BASE_NotListView, string> pp = lec => {
            return string.Format("[id:{0},damage:{1},quantity:{2},id:{3}]"
                , GetRef(lec).ComponentDefID
                , GetRef(lec).DamageLevel
                , lec.quantity
                , lec.GetId());
        };

        var iw_corrupted_add = inventoryWidget.localInventory.Where(x => !ielCache.Contains(x)).ToList();
        if (iw_corrupted_add.Count > 0)
        {
            Extensions.LogError("inventoryWidget has been corrupted, items were added directly: " + string.Join(", ", iw_corrupted_add.Select(c => c.controller).Select(pp).ToArray()));
        }
        var iw_corrupted_remove = ielCache.Where(x => !inventoryWidget.localInventory.Contains(x)).ToList();
        if (iw_corrupted_remove.Count > 0)
        {
            Extensions.LogError("inventoryWidget has been corrupted, iel elements were removed.");
        }

        if (iw_corrupted_add.Any() || iw_corrupted_remove.Any())
        {
            Extensions.LogWarning("Restoring to last good state. Duplication or item loss may occur.");
            inventoryWidget.localInventory = ielCache.ToArray().ToList();
        }

        var toShow = filteredInventory.Skip(index).Take(itemLimit).ToList();

        var icc = ielCache.ToList();

            

        if (Extensions.Spam) Extensions.LogSpam("[LimitItems] Showing: " + string.Join(", ", toShow.Select(pp).ToArray()));

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
            details.Insert(0, string.Format("enabled {0} {1}", iw.ComponentRef.ComponentDefID, iw.GetComponent<UnityEngine.RectTransform>().anchoredPosition));
        });
        icc.ForEach(unused => unused.gameObject.SetActive(false));

        var listElemSize = 64.0f;
        var spacerTotal  = 16.0f; // IEL elements are 64 tall, but have a total of 80 pixels between each when considering spacing.
        var spacerHalf   = spacerTotal * .5f;
        var tsize        = listElemSize + spacerTotal;
            
        var virtualStartSize = tsize * index - spacerHalf;
        DummyStart.gameObject.SetActive(index > 0); //If nothing prefixing, must disable to prevent halfspacer offset.
        DummyStart.sizeDelta = new UnityEngine.Vector2(100, virtualStartSize);
        DummyStart.SetAsFirstSibling();

        var itemsHanging = filteredInventory.Count - (index + ielCache.Count(ii => ii.gameObject.activeSelf));

        var ap1 = ielCache[0].GetComponent<UnityEngine.RectTransform>().anchoredPosition;
        var ap2 = ielCache[1].GetComponent<UnityEngine.RectTransform>().anchoredPosition;

        Extensions.LogDebug(string.Format("[LimitItems] Items prefixing {0} hanging {1} total {2} {3}/{4}", index, itemsHanging, filteredInventory.Count, ap1, ap2));



        var virtualEndSize = tsize * itemsHanging - spacerHalf;
        DummyEnd.gameObject.SetActive(itemsHanging > 0); //If nothing postfixing, must disable to prevent halfspacer offset.
        DummyEnd.sizeDelta = new UnityEngine.Vector2(100, virtualEndSize);
        DummyEnd.SetAsLastSibling();

        instance.RefreshInventorySelectability();
        if (Extensions.Spam) { var sr = inventoryWidget.scrollbarArea;
            Extensions.LogSpam(string.Format( "[LimitItems] RefreshDone dummystart {0} dummyend {1} vnp {2} lli {3}"
                , DummyStart.anchoredPosition.y
                , DummyEnd.anchoredPosition.y
                , sr.verticalNormalizedPosition
                , "(" + string.Join(", ", details.ToArray()) + ")"
            ));
        }
    }

    public void Dispose() {
        inventoryWidget.localInventory.ForEach(ii => ii.controller = null);
    }

    public readonly static int itemsOnScreen = 7;

    // Maximum # of visual elements to allocate (will be used for slightly off screen elements.)
    public readonly static int itemLimit = 8;
    public static UnityEngine.RectTransform DummyStart; 
    public static UnityEngine.RectTransform DummyEnd;

    public static bool filterGuard = false;
}