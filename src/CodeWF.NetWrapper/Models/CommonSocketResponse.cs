using CodeWF.NetWeaver.Base;
using System;
using System.Collections.Generic;
using System.Text;

namespace CodeWF.NetWrapper.Models;

[NetHead(SocketConstants.CommonSocketResponseObjectId, 1)]
public class CommonSocketResponse : INetObject
{
    public string TaskId { get; set; } = null!;

    public byte Status { get; set; }

    public string? Message { get; set; }
}