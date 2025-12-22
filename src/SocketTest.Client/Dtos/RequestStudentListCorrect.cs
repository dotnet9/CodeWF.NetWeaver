using CodeWF.NetWeaver.Base;
using SocketDto;

namespace SocketTest.Client.Dtos;

[NetHead(id: NetConsts.TestCorrectObjectId, version:1)]
public class RequestStudentListCorrect : INetObject
{
    public int TaskId { get; set; }
}