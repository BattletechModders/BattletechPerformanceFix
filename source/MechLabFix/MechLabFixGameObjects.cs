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

    internal List<InventoryItemElement_NotListView> ielCache;

    // temporary elements
    internal InventoryItemElement_NotListView iieTmpT => _iieTmpT;
    private static InventoryItemElement_NotListView _iieTmpT;
    internal InventoryItemElement_NotListView iieTmpA => _iieTmpA;
    private static InventoryItemElement_NotListView _iieTmpA;
    internal InventoryItemElement_NotListView iieTmpB => _iieTmpB;
    private static InventoryItemElement_NotListView _iieTmpB;
    internal InventoryItemElement_NotListView iieTmpG => _iieTmpG;
    private static InventoryItemElement_NotListView _iieTmpG;

    private static Transform ContainerTransform;

    internal void Setup(MechLabInventoryWidget inventoryWidget)
    {
        Logging.Debug?.Log($"inventoryCount={inventoryWidget.localInventory?.Count}");

        if (inventoryWidget.localInventory == null)
        {
            throw new("WTF");
        }

        if (ContainerTransform == null)
        {
            var containerGo = new GameObject(Main.ModName);
            containerGo.SetActive(false);
            Object.DontDestroyOnLoad(containerGo);
            ContainerTransform = containerGo.transform;
        }

        if (_iieTmpT == null)
        {
            _iieTmpT = GetIIE("T");
        }
        if (_iieTmpA == null)
        {
            _iieTmpA = GetIIE("A");
        }
        if (_iieTmpB == null)
        {
            _iieTmpB = GetIIE("B");
        }
        if (_iieTmpG == null)
        {
            _iieTmpG = GetIIE("G");
        }

        /* Allocate very few visual elements, as this is extremely slow for both allocation and deallocation.
                   It's the difference between a couple of milliseconds and several seconds for many unique items in inventory
                   This is the core of the fix, the rest is just to make it work within HBS's existing code.
                */
        ielCache = Enumerable.Repeat((string)null, MechLabFixState.itemLimit).Select(GetIIE).ToList();

        foreach (var nlv in ielCache)
        {
            nlv.SetRadioParent(inventoryWidget.inventoryRadioSet);
            nlv.gameObject.transform.SetParent(inventoryWidget.listParent, false);
            nlv.gameObject.transform.localScale = Vector3.one;
        }
        inventoryWidget.localInventory.AddRange(ielCache);
    }

    private InventoryItemElement_NotListView GetIIE(string id = null)
    {
        var iieGo = DataManager.PooledInstantiate(ListElementController_BASE_NotListView.INVENTORY_ELEMENT_PREFAB_NotListView, BattleTechResourceType.UIModulePrefabs);
        if (id != null)
        {
            iieGo.name = $"{ListElementController_BASE_NotListView.INVENTORY_ELEMENT_PREFAB_NotListView} [{id}]";
        }
        iieGo.transform.SetParent(ContainerTransform, false);
        iieGo.SetActive(false); // everything from pool should already be deactivated
        return iieGo.GetComponent<InventoryItemElement_NotListView>();
    }
}