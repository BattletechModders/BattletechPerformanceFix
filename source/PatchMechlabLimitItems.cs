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

    public class PatchMechlabLimitItems {
        MechLabPanel instance;
        MechLabInventoryWidget inventoryWidget;

        List<DefAndCount> inventory;

        List<ListElementController_InventoryWeapon_NotListView> weaponControllerCache;
        List<ListElementController_InventoryGear_NotListView> gearControllerCache;

        List<InventoryItemElement_NotListView> ielCache;

        List<ListElementController_BASE_NotListView> rawInventory;
        List<ListElementController_BASE_NotListView> filteredInventory;

        int index = 0;
        int endIndex = 0;

        PatchMechlabLimitItems(MechLabPanel instance) {
            try {
            this.instance = instance;
            this.inventoryWidget = new Traverse(instance).Field("inventoryWidget").GetValue<MechLabInventoryWidget>();

            if (instance.IsSimGame) {
                new Traverse(instance).Field("originalStorageInventory").SetValue(instance.storageInventory);
            }


            inventory = instance.storageInventory.Select(mcr => {
                mcr.DataManager = instance.dataManager;
                mcr.RefreshComponentDef();
                var num = !instance.IsSimGame ? int.MinValue : instance.sim.GetItemCount(mcr.Def.Description, mcr.Def.GetType(), SimGameState.ItemCountType.UNDAMAGED_ONLY); // Undamaged only is wrong, just for testing.
                return new DefAndCount(mcr, num);
            }).ToList();

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

            // InitAndCreate looks for def, so this is to prevent npe.
            var dummyref = new MechComponentRef();
            Control.mod.Logger.Log(string.Format("dummref == null ? {0}", dummyref == null));
            Control.mod.Logger.Log(string.Format("MLI == null ? {0}", inventoryWidget == null));
            // InitAndCreate refreshes display information, which doesn't exist yet so drop this function.
            var h = Hook.Prefix(AccessTools.Method(typeof(ListElementController_InventoryWeapon_NotListView), "RefreshInfoOnWidget"), Fun.fun(() => { Control.mod.Logger.Log("Drop ref on widget"); return false; }).Method);
            var h2 = Hook.Prefix(AccessTools.Method(typeof(InventoryItemElement_NotListView), "RefreshInfo"), Fun.fun(() => { Control.mod.Logger.Log("Drop refresh info"); return false; }).Method);
            var h3 = Hook.Prefix(AccessTools.Method(typeof(InventoryItemElement_NotListView), "SetTooltipData"), Fun.fun(() => false).Method);
            var sw = new Stopwatch();
            sw.Start();
            weaponControllerCache = Enumerable.Repeat<Func<ListElementController_InventoryWeapon_NotListView>>(() => {
                ListElementController_InventoryWeapon_NotListView controller = new ListElementController_InventoryWeapon_NotListView();
				//controller.InitAndCreate(dummyref, instance.dataManager, inventoryWidget, 1, false);
                controller.InitAndFillInSpecificWidget(dummyref, null, instance.dataManager, null, 1, false);
                return controller;
            }, 10000).Select(x => x()).ToList();
            Control.mod.Logger.Log(string.Format("Created 10000 in {0}", sw.Elapsed.TotalMilliseconds));
            ielCache = Enumerable.Repeat<Func<InventoryItemElement_NotListView>>( () => {
                var nlv = instance.dataManager.PooledInstantiate( ListElementController_BASE_NotListView.INVENTORY_ELEMENT_PREFAB_NotListView
                                                                                                                              , BattleTechResourceType.UIModulePrefabs, null, null, null)
                                                                                                            .GetComponent<InventoryItemElement_NotListView>();
				nlv.SetRadioParent(new Traverse(inventoryWidget).Field("inventoryRadioSet").GetValue<HBSRadioSet>());
				nlv.gameObject.transform.SetParent(new Traverse(inventoryWidget).Field("listParent").GetValue<UnityEngine.Transform>(), false);
				nlv.gameObject.transform.localScale = UnityEngine.Vector3.one;
                return nlv; }
                                                                                , itemLimit)
                                 .Select(thunk => thunk())
                                 .ToList();
            var li = new Traverse(inventoryWidget).Field("localInventory").GetValue<List<InventoryItemElement_NotListView>>();
            ielCache.ForEach(iw => li.Add(iw));
            
            var lp = new Traverse(inventoryWidget).Field("listParent").GetValue<UnityEngine.Transform>();
            if (lp == null)
                Control.mod.Logger.LogError("lp null");

            if (DummyStart == null) DummyStart = new UnityEngine.GameObject().AddComponent<UnityEngine.RectTransform>();
            if (DummyEnd   == null) DummyEnd   = new UnityEngine.GameObject().AddComponent<UnityEngine.RectTransform>();

            if (DummyStart == null)
                Control.mod.Logger.LogError("Dummy null");

            DummyStart.SetParent(lp, false);
            DummyEnd.SetParent(lp, false);

            h.Dispose();
            h2.Dispose();
            h3.Dispose();

            FilterChanged();
            } catch(Exception e) {
                Control.mod.Logger.Log(string.Format("exn: {0}", e));
            }
        }

        List<ListElementController_BASE_NotListView> Sort(List<ListElementController_BASE_NotListView> items) {
            var _a = new ListElementController_InventoryGear_NotListView();
            var _b = new ListElementController_InventoryGear_NotListView();
            var _ac = new InventoryItemElement_NotListView();
            var _bc = new InventoryItemElement_NotListView();
            _ac.controller = _a;
            _bc.controller = _b;
            var _cs = new Traverse(inventoryWidget).Field("currentSort").GetValue<Comparison<InventoryItemElement_NotListView>>();
            var tmp = items.ToList();
            tmp.Sort(new Comparison<ListElementController_BASE_NotListView>((l,r) => {
                _a.componentRef = GetRef(l);
                _b.componentRef = GetRef(r);
                return _cs.Invoke(_ac, _bc);
            }));
            return tmp;
        }

        List<ListElementController_BASE_NotListView> Filter(List<ListElementController_BASE_NotListView> items) {
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

            ListElementController_BASE tmpctl = new ListElementController_InventoryGear();

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
                return filter.Execute(Enumerable.Repeat(tmpctl, 1)).Any();
                }).ToList();
            return current;
        }

        MechComponentRef GetRef(ListElementController_BASE_NotListView lec) {
            if (lec is ListElementController_InventoryWeapon_NotListView) return (lec as ListElementController_InventoryWeapon_NotListView).componentRef;
            if (lec is ListElementController_InventoryGear_NotListView) return (lec as ListElementController_InventoryGear_NotListView).componentRef;
            Control.mod.Logger.LogError("lec is not gear or weapon: " + lec.GetId());
            return null;
        }

        public void FilterChanged() {
            Control.mod.Logger.Log("Filter changed");
            index = 0;
            filteredInventory = Filter(rawInventory);
            endIndex = filteredInventory.Count - itemLimit;
            Refresh();
        }

        void Refresh(bool wantClobber = true) {
            Control.mod.Logger.Log(string.Format("Refresh: {0} {1} {2} ", index, filteredInventory.Count, itemLimit));
            if (index > filteredInventory.Count - itemsOnScreen)
                index = filteredInventory.Count - itemsOnScreen;
            if (filteredInventory.Count < itemsOnScreen)
                index = 0;
            if (index < 0)
                index = 0;


            IEnumerable<ListElementController_BASE_NotListView> toShow = filteredInventory.Skip(index).Take(itemLimit);

            var icc = ielCache.ToList();

            toShow.ToList().ForEach(lec => {
                Control.mod.Logger.Log("Showing: " + lec.componentDef.Description.Name);
                var iw = icc[0]; icc.RemoveAt(0);
                var cref = GetRef(lec);
                iw.ClearEverything();
                iw.ComponentRef = cref;
                lec.ItemWidget = iw;
                iw.SetData(lec, inventoryWidget, lec.quantity, false, null);
                lec.SetupLook(iw);
                iw.gameObject.SetActive(true);
            });
            icc.ForEach(unused => unused.gameObject.SetActive(false));

            DummyStart.SetAsFirstSibling();
            DummyStart.sizeDelta = new UnityEngine.Vector2(100, 60 * index);
            DummyEnd.SetAsLastSibling();

            var itemsHanging = filteredInventory.Count - (index + itemsOnScreen);
            Control.mod.Logger.Log("Items hanging: " + itemsHanging);

            DummyEnd.sizeDelta = new UnityEngine.Vector2(100, 60.0f * itemsHanging);
            
            
			new Traverse(instance).Method("RefreshInventorySelectability").GetValue();
            Control.mod.Logger.Log("RefreshDone");

            //if (!wantClobber)
            //    cbase.ToList().ForEach(nw => inventoryWidget.OnAddItem(nw.ItemWidget, false));
            //new Traverse(instance).Method("RefreshInventorySelectability").GetValue();
            //*/
        }

        static int itemsOnScreen = 7;
        static int itemLimit = 7;
        public static UnityEngine.RectTransform DummyStart; 
        public static UnityEngine.RectTransform DummyEnd;
        public static PatchMechlabLimitItems limitItems = null;
        static MethodInfo PopulateInventory = AccessTools.Method(typeof(MechLabPanel), "PopulateInventory");
        static MethodInfo ConfirmRevertMech = AccessTools.Method(typeof(MechLabPanel), "ConfirmRevertMech");
        static MethodInfo ExitMechLab       = AccessTools.Method(typeof(MechLabPanel), "ExitMechLab");
        static MethodInfo OnAddItem         = AccessTools.Method(typeof(MechLabInventoryWidget), "OnAddItem");
        static MethodInfo OnRemoveItem      = AccessTools.Method(typeof(MechLabInventoryWidget), "OnRemoveItem");
        public static void Initialize() {
            Hook.Prefix(PopulateInventory, Fun.fun((MechLabPanel __instance) => { 
                if (limitItems != null) Control.mod.Logger.LogError("PopulateInventory was not properly cleaned");
                Control.mod.Logger.Log("PopulateInventory patching");
                limitItems = new PatchMechlabLimitItems(__instance);
                return false;
            }).Method);

            Hook.Prefix(ConfirmRevertMech, Fun.fun((MechLabPanel __instance) => { 
                if (limitItems == null) Control.mod.Logger.LogError("Unhandled ConfirmRevertMech");
                Control.mod.Logger.Log("Reverting mech");
                limitItems = null;
            }).Method);

            Hook.Prefix(ExitMechLab, Fun.fun((MechLabPanel __instance) => { 
                if (limitItems == null) Control.mod.Logger.LogError("Unhandled ExitMechLab");
                limitItems = null;
            }).Method);

            var onLateUpdate = AccessTools.Method(typeof(UnityEngine.UI.ScrollRect), "LateUpdate");
            Hook.Prefix(onLateUpdate, Fun.fun((UnityEngine.UI.ScrollRect __instance) => {
                if (limitItems != null && new Traverse(limitItems.inventoryWidget).Field("scrollbarArea").GetValue<UnityEngine.UI.ScrollRect>() == __instance) {
                    var newIndex = (int)((limitItems.endIndex) * (1.0f - __instance.verticalNormalizedPosition));
                    if (limitItems.filteredInventory.Count < itemsOnScreen) {
                        newIndex = 0;
                    }
                    if (limitItems.index != newIndex) {
                        limitItems.index = newIndex;
                        Control.mod.Logger.Log("Refresh with: " + newIndex.ToString());
                        limitItems.Refresh(false);
                    }
                }        
            }).Method); 

            var onApplyFiltering = AccessTools.Method(typeof(MechLabInventoryWidget), "ApplyFiltering");
            Hook.Prefix(onApplyFiltering, Fun.fun((MechLabInventoryWidget __instance) => {
                if (limitItems != null && limitItems.inventoryWidget == __instance) {
                    limitItems.FilterChanged();
                    return false;
                } else {
                    return true;
                }
            }).Method);

            var onApplySorting = AccessTools.Method(typeof(MechLabInventoryWidget), "ApplySorting");
            Hook.Prefix(onApplyFiltering, Fun.fun((MechLabInventoryWidget __instance) => {
                if (limitItems != null && limitItems.inventoryWidget == __instance) {
                    // it's a mechlab screen, we do our own sort.
                     return false;
                } else {
                    return true;
                }
            }).Method);            
        }
    }
}