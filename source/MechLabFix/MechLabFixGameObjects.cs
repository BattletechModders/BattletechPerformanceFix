using System.Collections.Generic;
using System.Linq;
using BattleTech;
using BattleTech.UI;
using BattleTech.UI.TMProWrapper;
using UnityEngine;

namespace BattletechPerformanceFix.MechLabFix;

internal class MechLabFixGameObjects
{
    internal RectTransform DummyStart => _DummyStart;
    internal RectTransform DummyEnd => _DummyEnd;
    private static RectTransform _DummyStart;
    private static RectTransform _DummyEnd;

    internal List<InventoryItemElement_NotListView> ielCache;
    // Temporary visual element used in the filter process.
    internal InventoryItemElement_NotListView iieTmp;
    internal void Create()
    {
        iieTmp = CreateIIE();

        /* Allocate very few visual elements, as this is extremely slow for both allocation and deallocation.
                   It's the difference between a couple of milliseconds and several seconds for many unique items in inventory
                   This is the core of the fix, the rest is just to make it work within HBS's existing code.
                */
        ielCache = Enumerable.Repeat(CreateIIE, MechLabFixState.itemLimit)
            .Select(thunk => thunk())
            .ToList();

        // DummyStart&End are blank rects stored at the beginning and end of the list so that unity knows how big the scrollrect should be
        // "placeholders"
        if (_DummyStart == null) _DummyStart = new GameObject().AddComponent<RectTransform>();
        if (_DummyEnd   == null) _DummyEnd   = new GameObject().AddComponent<RectTransform>();
    }

    private InventoryItemElement_NotListView CreateIIE()
    {
        // TODO memory leak here
        return UnityGameInstance.BattleTechGame.DataManager
            .PooledInstantiate(ListElementController_BASE_NotListView.INVENTORY_ELEMENT_PREFAB_NotListView, BattleTechResourceType.UIModulePrefabs)
            .GetComponent<InventoryItemElement_NotListView>();
    }

    internal void Setup(MechLabInventoryWidget inventoryWidget)
    {
        foreach (var nlv in ielCache)
        {
            nlv.SetRadioParent(inventoryWidget.inventoryRadioSet);
            nlv.gameObject.transform.SetParent(inventoryWidget.listParent, false);
            nlv.gameObject.transform.localScale = Vector3.one;
        }
        var li = inventoryWidget.localInventory;
        li.AddRange(ielCache);

        var lp = inventoryWidget.listParent;
        DummyStart.SetParent(lp, false);
        DummyEnd.SetParent(lp, false);
    }

    // TODO memory leak here
    internal void Dispose()
    {
        ielCache.ForEach(ii => ii.controller = null);

        return;
        Object.DestroyImmediate(iieTmp.gameObject);
        foreach (var iie in ielCache)
        {
            Object.DestroyImmediate(iie.gameObject);
        }

        return;
        // fix for objects not getting destroyed
        if (iieTmp != null)
        {
            foreach (var lt in iieTmp.GetComponentsInChildren<LocalizableText>(true))
            {
                if (lt.mesh != null)
                {
                    try
                    {
                        Object.DestroyImmediate(lt.mesh);
                    }
                    catch
                    {
                        //
                    }
                }
            }
            try
            {
                Object.DestroyImmediate(iieTmp.gameObject);
            }
            catch
            {
                //
            }
        }
        foreach (var iie in ielCache)
        {
            if (iie != null)
            {
                foreach (var lt in iie.GetComponentsInChildren<LocalizableText>(true))
                {
                    if (lt.mesh != null)
                    {
                        try
                        {
                            Object.DestroyImmediate(lt.mesh);
                        }
                        catch
                        {
                            //
                        }
                    }
                }
                try
                {
                    Object.DestroyImmediate(iie.gameObject);
                }
                catch
                {
                    //
                }
            }
        }
    }
}