using System;
using System.Collections.Generic;
using System.Linq;
using NLog;

namespace CommonSuite
{
    public class LogFilters
    {
        private Logger logger = LogManager.GetCurrentClassLogger();

        SuiteRegistry _suiteRegistry;

        public LogFilters(SuiteRegistry suiteRegistry)
        {
            _suiteRegistry = suiteRegistry;
        }

        public void SaveFiltersToRegistry(LogFilterCollection filters)
        {
            // removed filters go too (T7Suite never deleted them, so they came back)
            using (SettingsKey all = SettingsKey.Open(_suiteRegistry.getRegistryPath(), "LogFilters"))
            {
                foreach (string index in all.GetSubKeyNames())
                {
                    using SettingsKey old = SettingsKey.Open(_suiteRegistry.getRegistryPath(), "LogFilters\\" + index);
                    foreach (string name in old.GetValueNames()) old.DeleteValue(name);
                }
            }
            foreach (LogFilter filter in filters)
            {
                SaveFilter(filter);
            }
        }

        public LogFilterCollection GetFiltersFromRegistry()
        {
            LogFilterCollection filters = new LogFilterCollection();
            try
            {
                using (SettingsKey Settings = SettingsKey.Open(_suiteRegistry.getRegistryPath(), "LogFilters"))
                {
                    if (Settings != null)
                    {
                        string[] vals = Settings.GetSubKeyNames();
                        foreach (string a in vals)
                        {
                            try
                            {
                                LogFilter filter = LoadFilter(a);
                                filters.Add(filter);
                            }
                            catch (Exception E)
                            {
                                logger.Debug(E.Message);
                            }
                        }
                    }
                }
            }
            catch (Exception E2)
            {
                logger.Debug(E2.Message);
            }
            return filters;
        }


        private void SaveFilter(LogFilter filter)
        {
            if (filter.Symbol != "")
            {
                using (SettingsKey saveSettings = SettingsKey.Open(_suiteRegistry.getRegistryPath(), "LogFilters\\" + filter.Index.ToString()))
                {
                    saveSettings.SetValue("value", filter.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    saveSettings.SetValue("type", (int)filter.Type);
                    saveSettings.SetValue("index", filter.Index);
                    saveSettings.SetValue("symbol", filter.Symbol);
                    saveSettings.SetValue("active", filter.Active);
                }
            }
        }


        private LogFilter LoadFilter(string index)
        {
            LogFilter filter = new LogFilter();
            if (index != "")
            {
                using (SettingsKey Settings = SettingsKey.Open(_suiteRegistry.getRegistryPath(), "LogFilters\\" + index))
                {
                    try
                    {
                        filter.Index = Convert.ToInt32(index);
                        filter.Active = Convert.ToBoolean(Settings.GetValue("active").ToString());
                        filter.Type = (LogFilter.MathType)Convert.ToInt32(Settings.GetValue("type"));
                        filter.Symbol = Settings.GetValue("symbol").ToString();
                        filter.Value = (float)ConvertToDouble(Settings.GetValue("value").ToString());
                    }
                    catch (Exception E)
                    {
                        logger.Debug(E.Message);
                    }
                }
            }
            return filter;

        }


        private double ConvertToDouble(string v)
        {
            // either decimal separator (the culture trick read "12.5" as 0 on sv-SE)
            return LogFile.Number(v);
        }
    }
}
