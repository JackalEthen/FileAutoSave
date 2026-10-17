using System.Drawing;
using System.Windows.Forms;

namespace FileAutoSave.Core.Ui
{
    /// <summary>
    /// 把 GDI+ 图像转成 COM 的 IPictureDisp——功能区的自定义图标要求这个类型。
    /// <see cref="AxHost.GetIPictureDispFromPicture(Image)"/> 是 protected，只能靠子类暴露。
    /// </summary>
    public sealed class PictureConverter : AxHost
    {
        private PictureConverter()
            : base(string.Empty)
        {
        }

        public static object? ToPictureDisp(Image? image) =>
            image == null ? null : GetIPictureDispFromPicture(image);
    }
}
