using System;
using UnityEngine;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using RT = BattleTech.BattleTechResourceType;
using BattleTech.UI;

namespace BattletechPerformanceFix;

public static class Extensions {
    public static void AlertUser(string title, string message) {
        var genericPopupBuilder = GenericPopupBuilder.Create(title, message);
        genericPopupBuilder.Render();
    }

    public static void Trap(Action f)
    { try { f(); } catch (Exception e) {
        Log.Main.Error?.Log("Encountered exception", e); }}

    public static T Trap<T>(Func<T> f, Func<T> or = null)
    {
        try { return f(); } catch (Exception e) {
            Log.Main.Error?.Log("Encountered exception", e); return or == null ? default : or(); }
    }
    public static IEnumerable<T> Sequence<T>(params T[] p) => p;

    public static string Dump<T>(this T t, bool indented = true)
    {
        return Trap(() => JsonConvert.SerializeObject(t, indented ? Formatting.Indented : Formatting.None, new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Error = (_, err) => err.ErrorContext.Handled = true
        }), () => "Extensions.Dump.SerializationFailed");
    }
}
