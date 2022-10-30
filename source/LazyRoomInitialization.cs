using Harmony;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using BattleTech.UI;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix;

public class LazyRoomInitialization : Feature
{
    public void Activate()
    {
        var specnames = new List<string> { "LeaveRoom", "InitWidgets" };
        Assembly
            .GetAssembly(typeof(SGRoomControllerBase))
            .GetTypes()
            .Where(type => type.IsSubclassOf(typeof(SGRoomControllerBase)))
            .Where(type => type != typeof(SGRoomController_Ship))
            .ToList()
            .ForEach(room =>
            {
                var meths = AccessTools.GetDeclaredMethods(room);
                foreach (MethodBase meth in meths)
                {
                    try
                    {
                        var sn = specnames.Where(x => meth.Name == x).ToList();
                        var patchfun = sn.Any() ? sn[0] : "Other";
                        if (patchfun != null)
                        {
                            Logging.Info?.Log($"LazyRoomInitialization methname {meth.Name}, patchfun {patchfun}");
                            Main.harmony.Patch(meth, new(typeof(LazyRoomInitialization), patchfun), null);
                        }
                    }
                    catch (Exception e)
                    {
                        Logging.Info?.Log($"Exception {e}");
                    }
                }
            });
        Main.harmony.Patch(AccessTools.Method(typeof(BattleTech.SimGameState), nameof(CompleteLanceConfigurationPrep))
            , new(typeof(LazyRoomInitialization), nameof(CompleteLanceConfigurationPrep), null));

    }

    public static void CompleteLanceConfigurationPrep(BattleTech.SimGameState __instance)
    {
        Logging.Info?.Log("New game ensure CmdCenterRoom is initialized");
        InitializeRoom(__instance.RoomManager.CmdCenterRoom);
    }

    public static void InitializeRoom(SGRoomControllerBase room)
    {
        allowInit = true;
        room.InitWidgets();
        allowInit = false;
    }

    public static Dictionary<SGRoomControllerBase, bool> DB = new();
    public static bool allowInit = false;
    public static bool InitWidgets(SGRoomControllerBase __instance)
    {
        return Trap(() =>
        {
            Logging.Info?.Log($"SGRoomControllerBase.InitWidgets (want initialize? {allowInit})");
            if (!allowInit)
            {
                DB[__instance] = false;
                return false;
            }
            DB[__instance] = true;
            return true;
        });
    }

    public static bool LeaveRoom(bool ___roomActive)
    {
        return Trap(() =>
        {
            Logging.Info?.Log("SGRoomControllerBase_LeaveRoom");
            if (___roomActive)
                return true;
            return false;
        });
    }
    public static void Other(SGRoomControllerBase __instance, MethodBase __originalMethod)
    {
        Trap(() =>
        {
            Logging.Info?.Log($"SGRoomControllerBase_Other {__originalMethod.Name}");

            if (DB[__instance] == false)
            {
                Logging.Info?.Log("Initialize Widgets");
                InitializeRoom(__instance);
            }
        });
    }
}