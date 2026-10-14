using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CommonSuite;

namespace T7
{
    /// <summary>T7's settings: the T7 section of the program's (T7SuitePro's imported once, ImportOnce).</summary>
    public class T7SuiteRegistry : SuiteRegistry
    {
        /// <summary>T7Suite's own settings name, imported once.</summary>
        public const string Former = "T7SuitePro";

        public const string Section = SettingsKey.Program + "/T7";

        override public string getRegistryPath()
        {
            return Section;
        }
    }
}
