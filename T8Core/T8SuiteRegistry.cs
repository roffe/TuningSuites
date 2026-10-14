using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CommonSuite;

namespace T8SuitePro
{
    /// <summary>T8's settings: the T8 section of the program's (T8SuitePro's imported once, ImportOnce).</summary>
    public class T8SuiteRegistry : SuiteRegistry
    {
        /// <summary>T8Suite's own settings name, imported once.</summary>
        public const string Former = "T8SuitePro";

        public const string Section = SettingsKey.Program + "/T8";

        override public string getRegistryPath()
        {
            return Section;
        }
    }
}
