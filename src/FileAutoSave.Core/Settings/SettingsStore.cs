using System;
using System.IO;
using FileAutoSave.Core.Storage;
using Newtonsoft.Json;

namespace FileAutoSave.Core.Settings
{
    /// <summary>
    /// 读写 settings.json。损坏或缺失时回退到默认配置，绝不让加载失败拖垮宿主。
    /// </summary>
    public sealed class SettingsStore
    {
        private readonly string _path;

        public SettingsStore(string? path = null)
        {
            _path = path ?? AppPaths.SettingsFile;
        }

        public AddInSettings Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return new AddInSettings();
                }

                var json = File.ReadAllText(_path);
                return JsonConvert.DeserializeObject<AddInSettings>(json) ?? new AddInSettings();
            }
            catch (Exception)
            {
                return new AddInSettings();
            }
        }

        public void Save(AddInSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, JsonConvert.SerializeObject(settings, Formatting.Indented));
        }
    }
}
