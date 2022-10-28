using System.Collections.Generic;

namespace BattletechPerformanceFix;

public class Settings {
    public string logLevel = "debug";
    public Dictionary<string,bool> features = new();

    public bool WantContractsLagFixVerify = false;
}