using CodeWF.NetWeaver.Base;
using SocketDto;

namespace SocketTest.Client.Dtos;

[NetHead(id: NetConsts.TestDiffVersionObjectId, version:1)]
public class RequestStudentListDiffVersion : INetObject
{
    public int TaskId { get; set; }
}