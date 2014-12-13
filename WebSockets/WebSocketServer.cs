﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Security.Authentication;

namespace WebSockets
{
    public class WebSocketServer
    {
        private TcpListener listener;

        public ConcurrentDictionary<Guid, SocketClient> Clients = new ConcurrentDictionary<Guid, SocketClient>();

        public Action<string> onLog;
		public Action<WebSocketClient> onClientJoined;
        public Action<HttpSocketClient> onHttpRequest;

        public int Port;

        public WebSocketServer(int Port)
        {
            this.Port = Port;
        }

        ~WebSocketServer()
        {
            listener.Stop();
        }

        public IEnumerable<WebSocketClient> WebSocketClients
        {
            get
            {
                return this.Clients.Values.Where(x => x is WebSocketClient).Select(x => x as WebSocketClient);
            }
        }

        public IEnumerable<JSONClient> JsonClients
        {
            get
            {
                return this.WebSocketClients.Select(x => new JSONClient(x));
            }
        }

		public void Init()
		{
            new Task(() =>
            {
                var a = IPAddress.Loopback;
                listener = new TcpListener(new IPEndPoint(a, this.Port));
                listener.Start();

                AcceptClient();

                Timer t = new Timer(
                    (object obj) =>
                    {
                        Clients.Values.Select(x => x as WebSocketClient).Where(x => x.IsNotNull()).Where(x=>x.Valid).ToList().ForEach(x => x.SendPacket("", 9));
                    },
                    null, 0, 1000);

            }).Start();
		}

        private void AcceptClient()
        {
            listener.BeginAcceptTcpClient(this.TcpClientJoined, null);
        }

        void TcpClientJoined(IAsyncResult res)
        {
            var socketClient = new SocketClient(listener.EndAcceptTcpClient(res), this);

            byte[] originalRequest = socketClient.Recieve(SocketFlags.Peek);

            if (originalRequest.Length != 0)
            {
                socketClient.handshake = new WebSocketHandshake(SocketClient.Encoder.GetString(originalRequest));

                SocketClient protcolClient = null;

                if (socketClient.handshake.Valid)
                {
                    //Work out correct client
                    socketClient.Recieve();
                }
                else
                {
                    SslStream sslStream = new SslStream(socketClient.tcpClient.GetStream(), true);
                    var a = new X509Certificate2(Environment.CurrentDirectory + "\\..\\..\\..\\Certs\\server.pfx", "test");
                    sslStream.AuthenticateAsServer(a, false, SslProtocols.Tls12, false);
                    sslStream.ReadTimeout = 5000;
                    sslStream.WriteTimeout = 5000;

                    socketClient.Stream = sslStream;

                    protcolClient = new WebSocketClient(socketClient, this) { handshake = socketClient.handshake, Stream = sslStream };

                    sslStream.Flush();

                    protcolClient.Write("hello");

                    List<byte> str = new List<byte>();
                    while (true)
                    {
                        try
                        {
                            originalRequest = protcolClient.Recieve();
                            foreach (var b in originalRequest)
                                str.Add(b);

                            if (str.Count >= 2)
                            {
                                if (SocketClient.Encoder.GetString(str.Skip(str.Count - 4).ToArray()) == "\r\n\r\n")
                                    break;
                            }
                        }
                        catch(IOException)
                        {
                            AcceptClient();
                            return;
                        }
                    }

                    socketClient.handshake = new WebSocketHandshake(SocketClient.Encoder.GetString(str.ToArray()));
                }

                switch (socketClient.Protocol)
                {
                    case "http":
                        protcolClient = new HttpSocketClient(socketClient, this) { handshake = socketClient.handshake};
                        break;
                    case "ws":
                        protcolClient = new WebSocketClient(socketClient, this) { handshake = socketClient.handshake};
                        break;
                }

                //Handshake
                if (protcolClient.IsNotNull())
                {
                    protcolClient.PerformHandshake();

                    var g = Guid.NewGuid();
                    Clients.TryAdd(g, protcolClient);

                    protcolClient.onSocketClosed = () =>
                    {
                        SocketClient c;
                        this.Clients.TryRemove(g, out c);
                    };

                    switch (protcolClient.Protocol)
                    {
                        case "http":
                            if (this.onHttpRequest != null)
                                this.onHttpRequest(protcolClient as HttpSocketClient);
                            protcolClient.Close();
                            break;
                        case "ws":
                            if (this.onClientJoined != null)
                                this.onClientJoined(protcolClient as WebSocketClient);
                            protcolClient.Start();
                            break;
                    }
                }
            }

            AcceptClient();
        }

		public void Log(string msg)
		{
			if (this.onLog != null)
				onLog(msg);
		}
    }
}
