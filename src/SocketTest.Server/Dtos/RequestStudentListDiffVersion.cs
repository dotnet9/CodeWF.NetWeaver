using CodeWF.NetWeaver.Base;
using SocketDto;

namespace SocketTest.Server.Dtos;

[NetHead(id: NetConsts.TestDiffVersionObjectId, version:2)]
public class RequestStudentListDiffVersion : INetObject
{
    public int TaskId { get; set; }
}