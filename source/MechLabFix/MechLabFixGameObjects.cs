using System.Collections.Generic;
using System.Linq;
using BattleTech;
using BattleTech.Data;
using BattleTech.UI;
using UnityEngine;

namespace BattletechPerformanceFix.MechLabFix;

internal class MechLabFixGameObjects
{
    private DataManager DataManager => UnityGameInstance.BattleTechGame.DataManager;

    internal RectTransform DummyStart => _DummyStart;
    internal RectTransform DummyEnd => _DummyEnd;
    private static RectTransform _DummyStart;
    private static RectTransform _DummyEnd;

    internal List<InventoryItemElement_NotListView> ielCache;

    // temporary elements
    internal InventoryItemElement_NotListView iieTmpT => _iieTmpT;
    private static InventoryItemElement_NotListView _iieTmpT;
    internal InventoryItemElement_NotListView iieTmpA => _iieTmpA;
    private static InventoryItemElement_NotListView _iieTmpA;
    internal InventoryItemElement_NotListView iieTmpB => _iieTmpB;
    private static InventoryItemElement_NotListView _iieTmpB;

    internal void Setup(MechLabInventoryWidget inventoryWidget)
    {
        Logging.Debug?.Log($"inventoryCount={inventoryWidget.localInventory?.Count}");

        if (inventoryWidget.localInventory == null)
        {
            throw new("WTF");
        }

        // DummyStart&End are blank rects stored at the beginning and end of the list so that unity knows how big the scrollrect should be
        // "placeholders"
        if (_DummyStart == null) _DummyStart = new GameObject().AddComponent<RectTransform>();
        if (_DummyEnd   == null) _DummyEnd   = new GameObject().AddComponent<RectTransform>();

        // TODO find a good place to put these
        if (_iieTmpT == null)
        {
            _iieTmpT = GetIIE();
            _iieTmpT.name = ListElementController_BASE_NotListView.INVENTORY_ELEMENT_PREFAB_NotListView + " [T] (" + Main.ModName +")";
            _iieTmpT.gameObject.SetActive(false);
        }
        if (_iieTmpA == null)
        {
            _iieTmpA = GetIIE();
            _iieTmpA.name = ListElementController_BASE_NotListView.INVENTORY_ELEMENT_PREFAB_NotListView + " [A] (" + Main.ModName +")";
            _iieTmpA.gameObject.SetActive(false);
        }
        if (_iieTmpB == null)
        {
            _iieTmpB = GetIIE();
            _iieTmpB.name = ListElementController_BASE_NotListView.INVENTORY_ELEMENT_PREFAB_NotListView + " [B] (" + Main.ModName +")";
            _iieTmpB.gameObject.SetActive(false);
        }

        /* Allocate very few visual elements, as this is extremely slow for both allocation and deallocation.
                   It's the difference between a couple of milliseconds and several seconds for many unique items in inventory
                   This is the core of the fix, the rest is just to make it work within HBS's existing code.
                */
        ielCache = Enumerable.Repeat(GetIIE, MechLabFixState.itemLimit)
            .Select(thunk => thunk())
            .ToList();

        foreach (var nlv in ielCache)
        {
            nlv.SetRadioParent(inventoryWidget.inventoryRadioSet);
            nlv.gameObject.transform.SetParent(inventoryWidget.listParent, false);
            nlv.gameObject.transform.localScale = Vector3.one;
        }
        inventoryWidget.localInventory.AddRange(ielCache);

        DummyStart.SetParent(inventoryWidget.listParent, false);
        DummyEnd.SetParent(inventoryWidget.listParent, false);
    }

    private InventoryItemElement_NotListView GetIIE()
    {
        // TODO memory leak here
        return DataManager
            .PooledInstantiate(ListElementController_BASE_NotListView.INVENTORY_ELEMENT_PREFAB_NotListView, BattleTechResourceType.UIModulePrefabs)
            .GetComponent<InventoryItemElement_NotListView>();
    }
}