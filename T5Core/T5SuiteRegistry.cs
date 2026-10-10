using CommonSuite;

namespace Trionic5Tools
{
    /// <summary>T5Suite 2.0's settings key: HKCU\Software\T5Suite2, outside MattiasC, which settings.json imports once.</summary>
    public class T5SuiteRegistry : SuiteRegistry
    {
        public T5SuiteRegistry() => SettingsKey.RegistryPaths[T5AppSettings.Suite] = @"Software\T5Suite2";

        public override string getRegistryPath() => T5AppSettings.Suite;
    }
}
