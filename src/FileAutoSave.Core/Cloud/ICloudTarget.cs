namespace FileAutoSave.Core.Cloud
{
    /// <summary>
    /// 一个云目标的写入能力。实现只需支持"把本地文件放到远端相对路径"。
    /// </summary>
    public interface ICloudTarget
    {
        void Upload(string localPath, string remoteRelativePath);
    }
}
