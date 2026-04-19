namespace SocketDto.AutoCommand;

/// <summary>
/// 进程结构变化信息（用于增量更新）
/// </summary>
[NetHead(NetConsts.ChangeProcessListObjectId, 1)]
public class ChangeProcessList : INetObject
{
}