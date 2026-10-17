using System;

namespace FileAutoSave.Core
{
    /// <summary>
    /// 自动保存间隔。取值 1 分钟至 1 小时。
    /// </summary>
    public readonly struct AutoSaveInterval
    {
        public static readonly TimeSpan Min = TimeSpan.FromMinutes(1);
        public static readonly TimeSpan Max = TimeSpan.FromHours(1);

        public AutoSaveInterval(TimeSpan value)
        {
            if (value < Min || value > Max)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    $"间隔必须在 {Min} 至 {Max} 之间。");
            }

            Value = value;
        }

        public TimeSpan Value { get; }

        public static AutoSaveInterval FromMinutes(int minutes) =>
            new AutoSaveInterval(TimeSpan.FromMinutes(minutes));
    }
}
