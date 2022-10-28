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
    // Temporary visual element used in the filter process.
    internal InventoryItemElement_NotListView iieTmp => _iieTmp;
    internal static InventoryItemElement_NotListView _iieTmp;

    internal void Setup(MechLabInventoryWidget inventoryWidget)
    {
        Extensions.LogError($"inventoryCount={inventoryWidget.localInventory?.Count}");

        if (inventoryWidget.localInventory == null)
        {
            throw new("WTF");
        }

        // DummyStart&End are blank rects stored at the beginning and end of the list so that unity knows how big the scrollrect should be
        // "placeholders"
        if (_DummyStart == null) _DummyStart = new GameObject().AddComponent<RectTransform>();
        if (_DummyEnd   == null) _DummyEnd   = new GameObject().AddComponent<RectTransform>();

        if (_iieTmp == null)
        {
            _iieTmp = GetIIE();
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