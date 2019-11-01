using Akka.Actor;
using Akka.IO;
using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;

namespace Neo.Network.P2P
{
    public abstract class Connection : UntypedActor
    {
        internal class Timer { public static Timer Instance = new Timer(); }
        internal class Ack : Tcp.Event { public static Ack Instance = new Ack(); }

        /// <summary>
        /// connection initial timeout (in seconds) before any package has been accepted
        /// </summary>
        private const int connectionTimeoutLimitStart = 10;
        /// <summary>
        /// connection timeout (in seconds) after every `OnReceived(ByteString data)` event
        /// </summary>
        private const int connectionTimeoutLimit = 60;

        public IPEndPoint Remote { get; }
        public IPEndPoint Local { get; }

        private ICancelable timer;
        private readonly IActorRef tcp;
        private readonly WebSocket ws;
        private bool disconnected = false;
        protected Connection(object connection, IPEndPoint remote, IPEndPoint local)
        {
            this.Remote = remote;
            this.Local = local;
            this.timer = Context.System.Scheduler.ScheduleTellOnceCancelable(TimeSpan.FromSeconds(connectionTimeoutLimitStart), Self, Timer.Instance, ActorRefs.NoSender);
            switch (connection)
            {
                case IActorRef tcp:
                    this.tcp = tcp;
                    break;
                case WebSocket ws:
                    this.ws = ws;
                    WsReceive();
                    break;
            }
        }

        private void WsReceive()
        {
            byte[] buffer = new byte[512];
            ArraySegment<byte> segment = new ArraySegment<byte>(buffer);
            ws.ReceiveAsync(segment, CancellationToken.None).PipeTo(Self,
                success: p =>
                {
                    switch (p.MessageType)
                    {
                        case WebSocketMessageType.Binary:
                            return new Tcp.Received(ByteString.FromBytes(buffer, 0, p.Count));
                        case WebSocketMessageType.Close:
                            return Tcp.PeerClosed.Instance;
                        default:
                            ws.Abort();
                            return Tcp.Aborted.Instance;
                    }
                },
                failure: ex => new Tcp.ErrorClosed(ex.Message));
        }

        protected void Disconnect(byte[] lastMessage = null)
        {
            disconnected = true;

            if (lastMessage == null)
            {
                if (tcp != null)
                {
                    tcp.Tell(Tcp.Abort.Instance);
                }
                else
                {
                    ws.Abort();
                }
                Context.Stop(Self);
                return;
            }

            if (tcp != null)
            {
                tcp.Tell(Tcp.Write.Create(ByteString.FromBytes(lastMessage), Ack.Instance));
                Context.SetReceiveTimeout(TimeSpan.FromSeconds(2.5));
                Become(msg =>
                {
                    if (msg is Ack || msg is ReceiveTimeout)
                    {
                        tcp.Tell(Tcp.Abort.Instance);
                        Context.Stop(Self);
                    }
                });
            }
            else
            {
                ArraySegment<byte> segment = new ArraySegment<byte>(lastMessage);
                var task = ws.SendAsync(segment, WebSocketMessageType.Binary, true, CancellationToken.None).PipeTo(Self,
                    success: () => Ack.Instance,
                    failure: ex => new Tcp.ErrorClosed(ex.Message));
                task.ContinueWith(t =>
                {
                    ws.Abort();
                    Context.Stop(Self);
                });
            }
        }

        protected virtual void OnAck()
        {
        }

        protected abstract void OnData(ByteString data);

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Timer _:
                    Disconnect();
                    break;
                case Ack _:
                    OnAck();
                    break;
                case Tcp.Received received:
                    OnReceived(received.Data);
                    break;
                case Tcp.ConnectionClosed _:
                    Context.Stop(Self);
                    break;
            }
        }

        private void OnReceived(ByteString data)
        {
            timer.CancelIfNotNull();
            timer = Context.System.Scheduler.ScheduleTellOnceCancelable(TimeSpan.FromSeconds(connectionTimeoutLimit), Self, Timer.Instance, ActorRefs.NoSender);
            try
            {
                OnData(data);
            }
            catch
            {
                Disconnect();
            }
        }

        protected override void PostStop()
        {
            if (!disconnected)
                tcp?.Tell(Tcp.Close.Instance);
            timer.CancelIfNotNull();
            ws?.Dispose();
            base.PostStop();
        }

        protected void SendData(ByteString data)
        {
            if (tcp != null)
            {
                tcp.Tell(Tcp.Write.Create(data, Ack.Instance));
            }
            else
            {
                ArraySegment<byte> segment = new ArraySegment<byte>(data.ToArray());
                ws.SendAsync(segment, WebSocketMessageType.Binary, true, CancellationToken.None).PipeTo(Self,
                    success: () => Ack.Instance,
                    failure: ex => new Tcp.ErrorClosed(ex.Message));
            }
        }
    }
}
