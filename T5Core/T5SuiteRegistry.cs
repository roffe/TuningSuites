using CommonSuite;

namespace Trionic5Tools
{
    /// <summary>T5SuitePro's settings, with T5Suite 2.0's keys and folder name to take over (T5AppSettings.Register).</summary>
    public class T5SuiteRegistry : SuiteRegistry
    {
        public T5SuiteRegistry() => T5AppSettings.Register();

        public override string getRegistryPath() => T5AppSettings.Suite;
    }
}
