using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Data;
using PatchGUI.Core;

namespace PatchGUI
{
    public class LocalizationManager : INotifyPropertyChanged
    {
        private const string DefaultLang = "zh_CN";
        private readonly Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);

        public static LocalizationManager Instance { get; } = new LocalizationManager();

        public string CurrentLanguage { get; private set; } = DefaultLang;

        public event PropertyChangedEventHandler? PropertyChanged;

        private LocalizationManager()
        {
            // Initial load
            LoadLanguageInstance(DefaultLang);
        }

        public string this[string key]
        {
            get
            {
                if (_strings.TryGetValue(key, out var value))
                    return value;
                return $"[{key}]";
            }
        }

        // Renamed instance method to avoid ambiguity/confusion inside the class
        public void LoadLanguageInstance(string langCode)
        {
            try
            {
                // Try to load from T3ppNative.dll embedded resource first
                string? json = T3ppDiff.GetLangJson(langCode);

                // Fallback to file system if DLL resource not available
                if (string.IsNullOrEmpty(json))
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string path = Path.Combine(baseDir, "lang", $"{langCode}.json");

                    if (File.Exists(path))
                    {
                        json = File.ReadAllText(path);
                    }
                }

                // If still not found, try default language
                if (string.IsNullOrEmpty(json))
                {
                    if (!langCode.Equals(DefaultLang, StringComparison.OrdinalIgnoreCase))
                    {
                        LoadLanguageInstance(DefaultLang);
                    }
                    return;
                }

                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();

                _strings.Clear();
                foreach (var kv in parsed)
                {
                    _strings[kv.Key] = kv.Value;
                }

                CurrentLanguage = langCode;

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(System.Windows.Data.Binding.IndexerName));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
            }
            catch
            {
                // ignore
            }
        }

        public string GetInstance(string key, string? fallback = null)
        {
            if (_strings.TryGetValue(key, out var value))
                return value;
            return fallback ?? key;
        }

        // Static wrappers matching the old API
        public static void LoadLanguage(string langCode) => Instance.LoadLanguageInstance(langCode);
        public static string Get(string key, string fallback) => Instance.GetInstance(key, fallback);
    }
}
