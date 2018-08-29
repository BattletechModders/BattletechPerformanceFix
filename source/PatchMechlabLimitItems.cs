using HBS.Logging;
using Harmony;
using BattleTech;
using BattleTech.UI;
using System.Collections.Generic;
using System.Collections;
using System;
using System.Linq;
using System.Reflection;
using System.Diagnostics;

namespace BattletechPerformanceFix
{
    // Deprecated, will be removed.
    public class DefAndCount {
        public MechComponentRef ComponentRef;
        public int Count;
        public DefAndCount(MechComponentRef componentRef, int count) {
            this.ComponentRef = componentRef;
            this.Count = count;
        }

        public void Decr() {
            if (Count != int.MinValue) Count--;
        }
        public void Incr() {
            if (Count != int.MinValue) Count++;
        }
    }

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
        MechLabPanel instance;
        MechLabInventoryWidget inventoryWidget;

        // Deprecated, will be removed.
        List<DefAndCount> inventory;

        List<InventoryItemElement_NotListView> ielCache;

        List<ListElementController_BASE_NotListView> rawInventory;
        List<ListElementController_BASE_NotListView> filteredInventory;

        // Index of current item element at the top of scrollrect
        int index = 0;

        int endIndex = 0;

        // Temporary visual element used in the filter process.
        InventoryItemElement_NotListView iieTmp;

        PatchMechlabLimitItems(MechLabPanel instance) {
            try {
                var sw = new Stopwatch();
                sw.Start();
            this.instance = instance;
            this.inventoryWidget = new Traverse(instance).Field("inventoryWidget").GetValue<MechLabInventoryWidget>();

            if (instance.IsSimGame) {
                new Traverse(instance).Field("originalStorageInventory").SetValue(instance.storageInventory);
            }

            Control.Log("Mechbay Patch initialized :simGame? {0}", instance.IsSimGame);

                inventory = instance.storageInventory.Select(mcr =>
                {
                    mcr.DataManager = instance.dataManager;
                    mcr.RefreshComponentDef();
                    var num = !instance.IsSimGame
                        ? int.MinValue
                        : instance.sim.GetItemCount(mcr.Def.Description, mcr.Def.GetType(), instance.sim.GetItemCountDamageType(mcr));
                    return new DefAndCount(mcr, num);
                }).ToList();

            /* Build a list of data only for all components. */
            rawInventory = inventory.Select<DefAndCount, ListElementController_BASE_NotListView>(dac => {
                if (dac.ComponentRef.ComponentDefType == ComponentType.Weapon) {
                    ListElementController_InventoryWeapon_NotListView controller = new ListElementController_InventoryWeapon_NotListView();
                    controller.InitAndFillInSpecificWidget(dac.ComponentRef, null, instance.dataManager, null, dac.Count, false);
                    return controller;
                } else {
                    ListElementController_InventoryGear_NotListView controller = new ListElementController_InventoryGear_NotListView();
                    controller.InitAndFillInSpecificWidget(dac.ComponentRef, null, instance.dataManager, null, dac.Count, false);
                    return controller;
                }
            }).ToList();
            rawInventory = Sort(rawInventory);

            Func<bool, InventoryItemElement_NotListView> mkiie = (bool nonexistant) => {
                var nlv = instance.dataManager.PooledInstantiate( ListElementController_BASE_NotListView.INVENTORY_ELEMENT_PREFAB_NotListView
                                                                                                                              , BattleTechResourceType.UIModulePrefabs, null, null, null)
                                                                                                            .GetComponent<InventoryItemElement_NotListView>();
				if (!nonexistant) {
                    nlv.SetRadioParent(new Traverse(inventoryWidget).Field("inventoryRadioSet").GetValue<HBSRadioSet>());
				    nlv.gameObject.transform.SetParent(new Traverse(inventoryWidget).Field("listParent").GetValue<UnityEngine.Transform>(), false);
				    nlv.gameObject.transform.localScale = UnityEngine.Vector3.one;
                }
                return nlv;
            };

            iieTmp = mkiie(true);

            /* Allocate very few visual elements, as this is extremely slow for both allocation and deallocation.
               It's the difference between a couple of milliseconds and several seconds for many unique items in inventory 
               This is the core of the fix, the rest is just to make it work within HBS's existing code.
               */
            ielCache = Enumerable.Repeat<Func<InventoryItemElement_NotListView>>( () => mkiie(false), itemLimit)
                                 .Select(thunk => thunk())
                                 .ToList();
            var li = new Traverse(inventoryWidget).Field("localInventory").GetValue<List<InventoryItemElement_NotListView>>();
            ielCache.ForEach(iw => li.Add(iw));
            // End



            var lp = new Traverse(inventoryWidget).Field("listParent").GetValue<UnityEngine.Transform>();

            // DummyStart&End are blank rects stored at the beginning and end of the list so that unity knows how big the scrollrect should be
            // "placeholders"
            if (DummyStart == null) DummyStart = new UnityEngine.GameObject().AddComponent<UnityEngine.RectTransform>();
            if (DummyEnd   == null) DummyEnd   = new UnityEngine.GameObject().AddComponent<UnityEngine.RectTransform>();

            DummyStart.SetParent(lp, false);
            DummyEnd.SetParent(lp, false);
            Control.Log(string.Format("[LimitItems] inventory cached in {0} ms", sw.Elapsed.TotalMilliseconds));

            FilterChanged();
            } catch(Exception e) {
                Control.LogException(e);
            }
        }

        /* Fast sort, which works off data, rather than visual elements. 
           Since only 7 visual elements are allocated, this is required.
        */
        List<ListElementController_BASE_NotListView> Sort(List<ListElementController_BASE_NotListView> items) {
            var sw = Stopwatch.StartNew();
            var _a = new ListElementController_InventoryGear_NotListView();
            var _b = new ListElementController_InventoryGear_NotListView();
            var _ac = new InventoryItemElement_NotListView();
            var _bc = new InventoryItemElement_NotListView();
            _ac.controller = _a;
            _bc.controller = _b;
            var _cs = new Traverse(inventoryWidget).Field("currentSort").GetValue<Comparison<InventoryItemElement_NotListView>>();
            var cst = _cs.Method;
            Control.LogDebug("Sort using {0}", cst.DeclaringType.FullName);

            var tmp = items.ToList();
            tmp.Sort(new Comparison<ListElementController_BASE_NotListView>((l,r) => {
                _ac.ComponentRef = _a.componentRef = GetRef(l);
                _bc.ComponentRef = _b.componentRef = GetRef(r);
                return _cs.Invoke(_ac, _bc);
            }));
            var delta = sw.Elapsed.TotalMilliseconds;
            Control.LogDebug("Sorted in {0} ms", delta);

            return tmp;
        }

        /* Fast filtering code which works off the data, rather than the visual elements.
           Suboptimal due to potential desyncs with normal filter proceedure, but simply required for performance */
        List<ListElementController_BASE_NotListView> Filter(List<ListElementController_BASE_NotListView> _items) {
            var items = Sort(_items);

            var iw = new Traverse(inventoryWidget);
            Func<string,bool> f = (n) => iw.Field(n).GetValue<bool>();
            var filter = new InventoryFilter( false //this.filteringAll
                                            , f("filteringWeapons")
                                            , f("filterEnabledWeaponBallistic")
                                            , f("filterEnabledWeaponEnergy")
                                            , f("filterEnabledWeaponMissile")
                                            , f("filterEnabledWeaponSmall")
                                            , f("filteringEquipment")
                                            , f("filterEnabledHeatsink")
                                            , f("filterEnabledJumpjet")
                                            , iw.Field("mechTonnage").GetValue<float>()
                                            , f("filterEnabledUpgrade")
                                            , false );

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
                var yes = filter.Execute(Enumerable.Repeat(tmpctl, 1)).Any();
                if (!yes) Control.LogDebug("[Filter] Removing :id {0} :componentType {1} :quantity {2}", def.Description.Id, def.ComponentType, item.quantity);
                return yes;
                }).ToList();
            return current;
        }

        /* Most mods hook the visual element code to filter. This function will do that as quickly as possible
           by re-using a single visual element.
        */
        List<ListElementController_BASE_NotListView> FilterUsingHBSCode(List<ListElementController_BASE_NotListView> items) {
            try {
                var sw = new Stopwatch();
                sw.Start();
            var tmp = inventoryWidget.localInventory;
            var iw = iieTmp;
            inventoryWidget.localInventory = Enumerable.Repeat(iw, 1).ToList();

            // Filter items once using the faster code, then again to handle mods.
            var okItems = Filter(items).Where(lec => {
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
                inventoryWidget.ApplyFiltering(false);
                filterGuard = false;
                lec.ItemWidget = null;
                var yes = iw.gameObject.activeSelf == true;
                if (!yes) Control.LogDebug("[FilterUsingHBSCode] Removing :id {0} :componentType {1} :quantity {2}", lec.componentDef.Description.Id, lec.componentDef.ComponentType, lec.quantity);
                return yes;
            }).ToList();
            inventoryWidget.localInventory = tmp;
            Control.Log(string.Format("Filter took {0} ms and resulted in {1} items", sw.Elapsed.TotalMilliseconds, okItems.Count));

            return okItems;
            } catch (Exception e) {
                Control.LogException(e);
                return null;
            }
        }

        MechComponentRef GetRef(ListElementController_BASE_NotListView lec) {
            if (lec is ListElementController_InventoryWeapon_NotListView) return (lec as ListElementController_InventoryWeapon_NotListView).componentRef;
            if (lec is ListElementController_InventoryGear_NotListView) return (lec as ListElementController_InventoryGear_NotListView).componentRef;
            Control.LogError("[LimitItems] lec is not gear or weapon: " + lec.GetId());
            return null;
        }

        /* The user has changed a filter, and we rebuild the item cache. */
        public void FilterChanged(bool resetIndex = true) {
            try {
                var iw = new Traverse(inventoryWidget);
                Func<string,bool> f = (n) => iw.Field(n).GetValue<bool>();

                Control.Log("[LimitItems] Filter changed (reset? {9}):\n  :weapons {0}\n  :equip {1}\n  :ballistic {2}\n  :energy {3}\n  :missile {4}\n  :small {5}\n  :heatsink {6}\n  :jumpjet {7}\n  :upgrade {8}"
                           , f("filteringWeapons")
                           , f("filteringEquipment")
                           , f("filterEnabledWeaponBallistic")
                           , f("filterEnabledWeaponEnergy")
                           , f("filterEnabledWeaponMissile")
                           , f("filterEnabledWeaponSmall")
                           , f("filterEnabledHeatsink")
                           , f("filterEnabledJumpjet")
                           , f("filterEnabledUpgrade")
                           , resetIndex);
            if (resetIndex) {
                new Traverse(inventoryWidget).Field("scrollbarArea").GetValue<UnityEngine.UI.ScrollRect>().verticalNormalizedPosition = 1.0f;
                index = 0;
            }

            filteredInventory = FilterUsingHBSCode(rawInventory);
            endIndex = filteredInventory.Count - itemsOnScreen;
            Refresh();
             } catch (Exception e) {
                Control.LogException(e);
            }
        }

        void Refresh(bool wantClobber = true) {
            Control.LogDebug(string.Format("[LimitItems] Refresh: {0} {1} {2} {3}", index, filteredInventory.Count, itemLimit, new Traverse(inventoryWidget).Field("scrollbarArea").GetValue<UnityEngine.UI.ScrollRect>().verticalNormalizedPosition));
            if (index > filteredInventory.Count - itemsOnScreen) {
                index = filteredInventory.Count - itemsOnScreen;
            }
            if (filteredInventory.Count < itemsOnScreen) {
                index = 0;
            }
            if (index < 0) {
                index = 0;
            }
            #if !VVV
            Control.LogDebug(string.Format("[LimitItems] Refresh(F): {0} {1} {2} {3}", index, filteredInventory.Count, itemLimit, new Traverse(inventoryWidget).Field("scrollbarArea").GetValue<UnityEngine.UI.ScrollRect>().verticalNormalizedPosition));
            #endif


            var toShow = filteredInventory.Skip(index).Take(itemLimit).ToList();

            var icc = ielCache.ToList();

            Func<ListElementController_BASE_NotListView,string> pp = lec => {
                return string.Format( "[id:{0},damage:{1},quantity:{2},id:{3}]"
                                    , GetRef(lec).ComponentDefID
                                    , GetRef(lec).DamageLevel
                                    , lec.quantity
                                    , lec.GetId());
            };

            #if !VVV
            Control.LogDebug("[LimitItems] Showing: " + string.Join(", ", toShow.Select(pp).ToArray()));
            #endif

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

            var iw_corrupted_add = inventoryWidget.localInventory.Where(x => !ielCache.Contains(x)).ToList();
            if (iw_corrupted_add.Count > 0) {
                Control.LogError("inventoryWidget has been corrupted, items were added: " + string.Join(", ", iw_corrupted_add.Select(c => c.controller).Select(pp).ToArray()));
                OnExitMechLab.Invoke(instance, new object[] {});
            }
            var iw_corrupted_remove = ielCache.Where(x => !inventoryWidget.localInventory.Contains(x)).ToList();
            if (iw_corrupted_remove.Count > 0) {
                Control.LogError("inventoryWidget has been corrupted, items were removed");
                OnExitMechLab.Invoke(instance, new object[] {});
            }

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

            Control.LogDebug(string.Format("[LimitItems] Items prefixing {0} hanging {1} total {2} {3}/{4}", index, itemsHanging, filteredInventory.Count, ap1, ap2));



            var virtualEndSize = tsize * itemsHanging - spacerHalf;
            DummyEnd.gameObject.SetActive(itemsHanging > 0); //If nothing postfixing, must disable to prevent halfspacer offset.
            DummyEnd.sizeDelta = new UnityEngine.Vector2(100, virtualEndSize);
            DummyEnd.SetAsLastSibling();
            
			new Traverse(instance).Method("RefreshInventorySelectability").GetValue();
            #if !VVV
            var sr = new Traverse(inventoryWidget).Field("scrollbarArea").GetValue<UnityEngine.UI.ScrollRect>();
            Control.LogDebug(string.Format( "[LimitItems] RefreshDone dummystart {0} dummyend {1} vnp {2} lli {3}"
                                                , DummyStart.anchoredPosition.y
                                                , DummyEnd.anchoredPosition.y
                                                , sr.verticalNormalizedPosition
                                                , "(" + string.Join(", ", details.ToArray()) + ")"
                                                ));
            #endif
        }

        void Dispose() {
            inventoryWidget.localInventory.ForEach(ii => ii.controller = null);
        }

        static int itemsOnScreen = 7;

        // Maximum # of visual elements to allocate (will be used for slightly off screen elements.)
        static int itemLimit = 8;
        public static UnityEngine.RectTransform DummyStart; 
        public static UnityEngine.RectTransform DummyEnd;
        public static PatchMechlabLimitItems limitItems = null;
        static MethodInfo OnPopulateInventory = AccessTools.Method(typeof(MechLabPanel), "PopulateInventory");
        static MethodInfo OnConfirmRevertMech = AccessTools.Method(typeof(MechLabPanel), "ConfirmRevertMech");
        static MethodInfo OnExitMechLab       = AccessTools.Method(typeof(MechLabPanel), "ExitMechLab");

        static bool filterGuard = false;
        public static void Initialize() {
            var self = typeof(PatchMechlabLimitItems);
            Hook.Prefix( AccessTools.Method(typeof(AAR_SalvageScreen), "BeginSalvageScreen")
                       , self.GetMethod("OpenSalvageScreen"));
            Hook.Prefix(OnPopulateInventory, self.GetMethod("PopulateInventory"));

            Hook.Prefix(OnConfirmRevertMech, self.GetMethod("ConfirmRevertMech"));

            Hook.Prefix(OnExitMechLab, self.GetMethod("ExitMechLab"));

            var onLateUpdate = AccessTools.Method(typeof(UnityEngine.UI.ScrollRect), "LateUpdate");
            Hook.Prefix(onLateUpdate, self.GetMethod("LateUpdate"));

            var onAddItem = AccessTools.Method(typeof(MechLabInventoryWidget), "OnAddItem");
            Hook.Prefix(onAddItem, self.GetMethod("OnAddItem"));

            var onRemoveItem = AccessTools.Method(typeof(MechLabInventoryWidget), "OnRemoveItem");
            Hook.Prefix(onRemoveItem, self.GetMethod("OnRemoveItem"));

            var onItemGrab = AccessTools.Method(typeof(MechLabInventoryWidget), "OnItemGrab");
            Hook.Prefix(onItemGrab, AccessTools.Method(typeof(PatchMechlabLimitItems), "ItemGrabPrefix"));

            var onApplyFiltering = AccessTools.Method(typeof(MechLabInventoryWidget), "ApplyFiltering");
            Hook.Prefix(onApplyFiltering, self.GetMethod("OnApplyFiltering"));

            /* FIXME: It's possible for some elements to be in an improper state to this function call. Drop if so.
             */
            Hook.Prefix(AccessTools.Method(typeof(MechLabPanel), "MechCanEquipItem"), self.GetMethod("MechCanEquipItem"));

            var onApplySorting = AccessTools.Method(typeof(MechLabInventoryWidget), "ApplySorting");
            Hook.Prefix(onApplySorting, self.GetMethod("OnApplySorting"), Priority.Last);
        }

        public static bool PopulateInventory(MechLabPanel __instance)
        {
            if (limitItems != null) Control.LogError("[LimitItems] PopulateInventory was not properly cleaned");
            Control.Log("[LimitItems] PopulateInventory patching (Mechlab fix)");
            limitItems = new PatchMechlabLimitItems(__instance);
            return false;
        }

        public static void OpenSalvageScreen()
        {
            // Only for logging purposes.
            Control.Log("[LimitItems] Open Salvage screen");
        }

        public static void ConfirmRevertMech()
        {
            Control.Log("[LimitItems] RevertMech");
        }

        public static void ExitMechLab(MechLabPanel __instance)
        {
            if (limitItems == null) { Control.LogError("[LimitItems] Unhandled ExitMechLab"); return; }
            Control.Log("[LimitItems] Exiting mechlab");
            limitItems.Dispose();
            limitItems = null;
        }

        public static void LateUpdate(UnityEngine.UI.ScrollRect __instance)
        {
            if (limitItems != null && new Traverse(limitItems.inventoryWidget).Field("scrollbarArea").GetValue<UnityEngine.UI.ScrollRect>() == __instance) {
                var newIndex = (int)((limitItems.endIndex) * (1.0f - __instance.verticalNormalizedPosition));
                if (limitItems.filteredInventory.Count < itemsOnScreen) {
                    newIndex = 0;
                }
                if (limitItems.index != newIndex) {
                    limitItems.index = newIndex;
                    Control.LogDebug(string.Format("[LimitItems] Refresh with: {0} {1}", newIndex, __instance.verticalNormalizedPosition));
                    limitItems.Refresh(false);
                }
            }        
        }

        public static bool OnAddItem(MechLabInventoryWidget __instance, IMechLabDraggableItem item)
        {
                if (limitItems != null && limitItems.inventoryWidget == __instance) {
                    try {
                        var nlv = item as InventoryItemElement_NotListView;
                        var quantity = nlv == null ? 1 : nlv.controller.quantity;
                        var existing = limitItems.rawInventory.Where(ri => ri.componentDef == item.ComponentRef.Def).FirstOrDefault();
                        if (existing == null) {
                            Control.LogDebug(string.Format("OnAddItem new {0}", quantity));
                            var controller = nlv == null ? null : nlv.controller;
                            if (controller == null) {
                                if (item.ComponentRef.ComponentDefType == ComponentType.Weapon) {
                                    var ncontroller = new ListElementController_InventoryWeapon_NotListView();
                                    ncontroller.InitAndCreate(item.ComponentRef, limitItems.instance.dataManager, limitItems.inventoryWidget, quantity, false);
                                    controller = ncontroller;
                                } else {
                                    var ncontroller = new ListElementController_InventoryGear_NotListView();
                                    ncontroller.InitAndCreate(item.ComponentRef, limitItems.instance.dataManager, limitItems.inventoryWidget, quantity, false);
                                    controller = ncontroller;
                                }
                            }
                            limitItems.rawInventory.Add(controller);
                            limitItems.rawInventory = limitItems.Sort(limitItems.rawInventory);
                            limitItems.FilterChanged(false);
                        } else {
                            Control.LogDebug(string.Format("OnAddItem existing {0}", quantity));
                            if (existing.quantity != Int32.MinValue) {
                                existing.ModifyQuantity(quantity);
                            }
                            limitItems.Refresh(false);
                        }            
                    } catch(Exception e) {
                        Control.LogException(e);
                    }
                    return false;
                } else {
                    return true;
                }
        }

        public static bool OnRemoveItem(MechLabInventoryWidget __instance, IMechLabDraggableItem item)
        {
                if (limitItems != null && limitItems.inventoryWidget == __instance) {
                    try {
                        var nlv = item as InventoryItemElement_NotListView;

                        var existing = limitItems.rawInventory.Where(ri => ri.componentDef == nlv.controller.componentDef).FirstOrDefault();
                        if (existing == null) {
                            Control.LogError(string.Format("OnRemoveItem new (should be impossible?) {0}", nlv.controller.quantity));
                        } else {
                            Control.LogDebug(string.Format("OnRemoveItem existing {0}", nlv.controller.quantity));
                            if (existing.quantity != Int32.MinValue) {
                                existing.ModifyQuantity(-1);
                                if (existing.quantity < 1)
                                    limitItems.rawInventory.Remove(existing);
                            }
                            limitItems.FilterChanged(false);
                            limitItems.Refresh(false);
                        }            
                    } catch(Exception e) {
                        Control.LogException(e);
                    }
                    return false;
                } else {
                    return true;
                }
        }

        public static bool OnApplyFiltering(MechLabInventoryWidget __instance, bool refreshPositioning)
        {
                if (limitItems != null && limitItems.inventoryWidget == __instance && !filterGuard) {
                    Control.LogDebug("OnApplyFiltering (refresh-pos? {0})", refreshPositioning);
                    limitItems.FilterChanged(refreshPositioning);
                    return false;
                } else {
                    return true;
                }
        }

        public static bool OnApplySorting(MechLabInventoryWidget __instance)
        {
                if (limitItems != null && limitItems.inventoryWidget == __instance) {
                    // it's a mechlab screen, we do our own sort.
                    limitItems.FilterChanged(false);
                    return false;
                } else {
                    return true;
                }
        }

        public static bool MechCanEquipItem(InventoryItemElement_NotListView item)
        {
            return item.ComponentRef == null ? false : true;
        }
        
        public static void ItemGrabPrefix(MechLabInventoryWidget __instance, ref IMechLabDraggableItem item) {
            if (limitItems != null && limitItems.inventoryWidget == __instance) {
                try {
                    Control.LogDebug(string.Format("OnItemGrab"));
                    var nlv = item as InventoryItemElement_NotListView;
                    var nlvtmp = limitItems.instance.dataManager.PooledInstantiate( ListElementController_BASE_NotListView.INVENTORY_ELEMENT_PREFAB_NotListView
                                                                                                                              , BattleTechResourceType.UIModulePrefabs, null, null, null)
                                                                                                            .GetComponent<InventoryItemElement_NotListView>();
                    var lec = nlv.controller;
                    var iw = nlvtmp;
                    var cref = limitItems.GetRef(lec);
                    iw.ClearEverything();
                    iw.ComponentRef = cref;
                    lec.ItemWidget = iw;
                    iw.SetData(lec, limitItems.inventoryWidget, lec.quantity, false, null);
                    lec.SetupLook(iw);
                    iw.gameObject.SetActive(true);
                    item = iw;
                } catch(Exception e) {
                    Control.LogException(e);
                }
            }
        }
    }
}