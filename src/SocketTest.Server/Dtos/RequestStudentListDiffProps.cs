using CodeWF.NetWeaver.Base;
using SocketDto;

namespace SocketTest.Server.Dtos;

[NetHead(id: NetConsts.TestDiffPropsObjectId, version:1)]
public class RequestStudentListDiffProps : INetObject
{
    public int TaskId { get; set; }

    public string Class { get; set; }
}