using System;
using System.Drawing;

namespace FileAutoSave.Core.Ui
{
    /// <summary>
    /// 功能区图标：内嵌 PNG（由 lucide 图标栅格化而来），按名加载。
    /// 名字对应 Resources\ribbon.&lt;name&gt;.png。
    /// </summary>
    public static class RibbonIcons
    {
        public static Image? Load(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            using (var stream = typeof(RibbonIcons).Assembly
                       .GetManifestResourceStream("FileAutoSave.Core.ribbon." + name + ".png"))
            {
                if (stream == null)
                {
                    return null;
                }

                using (var image = Image.FromStream(stream))
                {
                    // 复制一份，脱离资源流，避免后续使用时报"流已关闭"。
                    return new Bitmap(image);
                }
            }
        }
    }
}
