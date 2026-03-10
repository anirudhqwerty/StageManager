using System;
using System.Reflection;
using Microsoft.Win32;

namespace StageManager
{
	public static class AutoStart
	{
		private const string REG_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

		public static void SetStartup(string appName, bool startup)
		{
			// OpenSubKey can return null if the key doesn't exist - was an NRE before
			using var key = Registry.CurrentUser.OpenSubKey(REG_KEY, writable: true);
			if (key is null)
				return;

			if (startup)
				key.SetValue(appName, GetAppPath());
			else
				key.DeleteValue(appName, throwOnMissingValue: false);
		}

		public static bool IsStartup(string appName)
		{
			using var key = Registry.CurrentUser.OpenSubKey(REG_KEY, writable: false);
			return key is not null && IsStartup(key, appName);
		}

		public static bool IsStartup(RegistryKey key, string appName)
			=> GetValueAsString(key, appName).Equals(GetAppPath(), StringComparison.OrdinalIgnoreCase);

		private static string GetValueAsString(RegistryKey key, string appName)
			=> key.GetValue(appName)?.ToString() ?? string.Empty;

		private static string GetAppPath()
			=> $@"""{Environment.ProcessPath}""";
	}
}
