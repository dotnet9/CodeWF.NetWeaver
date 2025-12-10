namespace CodeWF.NetWeaver.Base
{
    /// <summary>
    /// 网络头信息类，用于封装网络传输中的头部信息
    /// </summary>
    public class NetHeadInfo
    {
        /// <summary>
        /// 获取或设置缓冲区长度
        /// </summary>
        public int BufferLen { get; set; }

        /// <summary>
        /// 获取或设置系统 ID
        /// </summary>
        public long SystemId { get; set; }

        /// <summary>
        /// 获取或设置对象 ID
        /// </summary>
        public ushort ObjectId { get; set; }

        /// <summary>
        /// 获取或设置对象版本
        /// </summary>
        public byte ObjectVersion { get; set; }

        /// <summary>
        /// 获取或设置 Unix 时间戳（毫秒）
        /// </summary>
        public long UnixTimeMilliseconds { get; set; }

        /// <summary>
        /// 返回网络头信息的字符串表示
        /// </summary>
        /// <returns>网络头信息的字符串表示</returns>
        public override string ToString()
        {
            return
                $"{nameof(BufferLen)}: {BufferLen}, {nameof(SystemId)}: {SystemId}，{nameof(ObjectId)}: {ObjectId}，{nameof(ObjectVersion)}: {ObjectVersion}";
        }
    }
}