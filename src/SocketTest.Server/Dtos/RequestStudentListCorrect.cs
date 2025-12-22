using CodeWF.NetWeaver.Base;
using SocketDto;

namespace SocketTest.Server.Dtos;

[NetHead(id: NetConsts.TestCorrectObjectId, version:1)]
public class RequestStudentListCorrect : INetObject
{
    public int TaskId { get; set; }
}