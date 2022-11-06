using System;
using System.Collections.Generic;
using BattleTech.UI;

namespace BattletechPerformanceFix.MechLabFix;

// this stuff is used by CustomComponents
public static class MechLabFixPublic
{
    public static Func<List<ListElementController_BASE_NotListView>, List<ListElementController_BASE_NotListView>>
        FilterFunc = list => MechLabFixFeature.state?.FilterUsingHBSCode(list);

    public static void FilterChanged()
    {
        MechLabFixFeature.state?.FilterChanged();
    }

    public static List<ListElementController_BASE_NotListView> RawInventory => MechLabFixFeature.state?.rawInventory;
}