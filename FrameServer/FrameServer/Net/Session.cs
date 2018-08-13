﻿using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;


namespace Network
{
    public class Session
    {
        public int id;
        public IPEndPoint tcpAdress, udpAdress;

        private NetworkService mService;
        private Socket mSocket;
        private Stopwatch mPingWatch;
        private Thread mActiveThread;

        public Socket socket { get { return mSocket; } }
        public NetworkService service { get { return mService; } }

        private KCP mKCP;
        private uint mNextUpdateTime = 0;
        private static readonly DateTime utc_time = new DateTime(1970, 1, 1);

        private static uint current
        {
            get
            {
                return (uint)(Convert.ToInt64(DateTime.UtcNow.Subtract(utc_time).TotalMilliseconds) & 0xffffffff);
            }
        }

        public bool Pinging
        {
            get
            {
                return mPingWatch != null;
            }
        }

        public Session(int clientId, Socket sock, NetworkService serv)
        {
            id = clientId;
            mService = serv;
            mSocket = sock;

            tcpAdress = (IPEndPoint)sock.RemoteEndPoint;
            mActiveThread = new Thread(ActiveThread);

            mActiveThread.Start();

            if (mService.kcp!=null)
            {
                mKCP = new KCP((uint)id, OnSendKcp);
                mKCP.NoDelay(1, 10, 2, 1);
            }
        }

        public void SendAcceptPoll()
        {
            mSocket.Send(BitConverter.GetBytes(id));
        }

        void ActiveThread()
        {
            while (IsConnected)
            {
                Thread.Sleep(1000);
            }

            Disconnect();
        }

        public bool IsConnected
        {
            get
            {
                try
                {
                    if (mSocket != null && mSocket.Connected)
                    {
                        return true;
                    }
                    return false;

                }
                catch (SocketException e)
                {
                    mService.CatchException(e);
                    return false;
                }
                catch (Exception e)
                {
                    mService.CatchException(e);
                    return false;
                }
            }
        }

        public void SendUdp(MessageBuffer message) 
        {
            if (mService != null)
            {
                if (mService.kcp!=null)
                {
                    SendKcp(message);
                }
                else if(mService.udp!=null)
                {
                    mService.udp.Send(new MessageInfo( message, this));
                }
            }
        }

       
        public void SendTcp(MessageBuffer message) 
        {
            if(mService!=null && mService.tcp !=null)
            {
                mService.tcp.Send(new MessageInfo(message, this));
            }
        }

       
        public void Disconnect()
        {
            if (mSocket == null) return;

            mSocket.Close();
            mSocket = null;
            mService.RemoveSession(this);

            mActiveThread.Abort();
            mActiveThread = null;

        }

        public void Ping()
        {
            if (Pinging)
            {
                mService.PingResult(this, mPingWatch.Elapsed.Milliseconds);
                mPingWatch = null;
            }
            else
            {
                mPingWatch = Stopwatch.StartNew();
                mService.udp.Send(new MessageInfo( new MessageBuffer(new byte[] { NetworkService.pingByte }), this));
            }
        }




        #region KCP
        private void SendKcp(MessageBuffer message)
        {
            if (mKCP != null)
            {
                mKCP.Send(message.buffer);
                mNextUpdateTime = 0;//可以马上更新
            }
        }
        public void Update()
        {
            if (mKCP == null)
            {
                return;
            }
            if (mService == null || mService.kcp == null || mService.kcp.IsActive == false)
            {
                return;
            }

            uint time = current;
            if (time >= mNextUpdateTime)
            {
                mKCP.Update(time);
                mNextUpdateTime = mKCP.Check(time);
            }
        }

        private void OnSendKcp(byte[] data, int length)
        {
            try
            {
                if (mService != null && mService.kcp != null & mService.kcp.IsActive)
                {
                    mService.kcp.Send(data, length, udpAdress);
                }
            }
            catch (Exception e)
            {
                Disconnect();
            }
        }

        public void OnReceiveKcp(byte[] data)
        {
            if (mKCP != null)
            {
                mKCP.Input(data);

                for(int size = mKCP.PeekSize(); size > 0;  size = mKCP.PeekSize())
                {
                    byte[] buffer = new byte[size];
                    if (mKCP.Recv(buffer) > 0)
                    {
                        MessageBuffer message = new MessageBuffer(buffer);
                        if (message.IsValid())
                        {
                            mService.OnReceive(new MessageInfo(message, this));
                        }
                    }
                }

            }
        }
        #endregion
    }
}
