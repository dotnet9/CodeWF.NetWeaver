using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace CodeWF.NetWrapper.Helpers;

public class TcpSession
{
    public Socket? TcpSocket { get; set; }
    public CancellationTokenSource? TokenSource { get; set; }
    public DateTime? ActiveTime { get; set; }
}