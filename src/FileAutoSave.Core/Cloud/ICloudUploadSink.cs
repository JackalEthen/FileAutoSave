namespace FileAutoSave.Core.Cloud
{
    /// <summary>
    /// 云端上传队列的入口。自动保存产生快照后，把要上传的文件塞进这里，由队列在后台重试。
    /// </summary>
    public interface ICloudUploadSink
    {
        /// <param name="localSnapshotPath">本地快照文件的绝对路径。</param>
        /// <param name="remoteRelativePath">云端相对路径，形如 {documentKey}/{fileName}。</param>
        void Enqueue(string localSnapshotPath, string remoteRelativePath);
    }
}
