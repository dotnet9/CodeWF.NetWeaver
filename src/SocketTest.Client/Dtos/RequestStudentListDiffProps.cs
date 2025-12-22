using CodeWF.NetWeaver.Base;
using SocketDto;

namespace SocketTest.Client.Dtos;

[NetHead(id: NetConsts.TestDiffPropsObjectId, version: 1)]
public class RequestStudentListDiffProps : INetObject
{
    public string TaskId { get; set; }
    public string Class { get; set; }
}