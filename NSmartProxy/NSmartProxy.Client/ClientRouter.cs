﻿using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NSmartProxy.Client
{
    public class ClientRouter
    {
        public const string TARGET_SERVICE_ADDRESS = "192.168.0.168";
        public const int TARGET_SERVICE_ADDRESS_PORT = 81;
        public const string PROVIDER_ADDRESS = "192.168.0.168";
        public const int PROVIDER_ADDRESS_PORT = 9973;
        CancellationTokenSource CANCELTOKEN = new CancellationTokenSource();
        public async Task ConnectToProvider()
        {
            //连接，获取provider端连接
            TcpClient providerClient = new TcpClient();
            TcpClient targetServiceClient = new TcpClient();        //目标服务

            ///该端口是一个随机端口，通过url获取
            providerClient.Connect(PROVIDER_ADDRESS, PROVIDER_ADDRESS_PORT);
            Console.WriteLine("Provider connected.");
            NetworkStream providerStream = providerClient.GetStream();
            NetworkStream targetServceStream = null;
            var buf = new byte[4096];

            int firstResult = await providerStream.ReadAsync(buf, 0, buf.Length, CANCELTOKEN.Token);

            //服务端主动断开连接时，会收到0字节，此时关闭连接
            if (firstResult == 0) return;

            targetServiceClient.Connect(TARGET_SERVICE_ADDRESS, TARGET_SERVICE_ADDRESS_PORT);
            Console.WriteLine("TargetServer connected(1st time).");
            targetServceStream = targetServiceClient.GetStream();

            await targetServceStream.WriteAsync(buf, 0, firstResult);

            ////读取provider的tcp流
            ////ps：需要每隔一分钟发送一次心跳包，否则服务会自动断开
            //while (!CANCELTOKEN.IsCancellationRequested)
            //{
            //    int resultLength = await targetServceStream.ReadAsync(buf, 0, buf.Length, CANCELTOKEN.Token);

            //    //服务端主动断开连接时，会收到0字节，此时关闭连接
            //    if (resultLength == 0) break;

            //    await providerStream.WriteAsync(buf, 0, buf.Length);
            //}

            Console.WriteLine("Looping start.");
            //创建相互转发流
            var taskT2PLooping = StreamTransfer(CANCELTOKEN.Token, targetServiceClient, providerClient, targetServceStream, providerStream, "T2P");
            var taskP2TLooping = StreamTransfer(CANCELTOKEN.Token, providerClient, targetServiceClient, providerStream, targetServceStream, "P2T");


            //循环接受A并写入C
            var comletedTask = await Task.WhenAny(taskT2PLooping, taskP2TLooping);
            //comletedTask.
            Console.WriteLine(comletedTask.Result + "disconnected");

        }


        /// <summary>
        /// 流间传输
        /// </summary>
        /// <param name="ct"></param>
        /// <param name="fromStream"></param>
        /// <param name="toStream"></param>
        /// <param name="beforeTransfer"></param>
        /// <returns></returns>
        private async Task<string> StreamTransfer(CancellationToken ct, TcpClient sourceClient, TcpClient toClient, NetworkStream fromStream, NetworkStream toStream, string signal, Func<byte[], Task<bool>> beforeTransfer = null)
        {


            var buf = new byte[4096];
            //循环接收C并且写入A
            //while (!ct.IsCancellationRequested)
            while (1 == 1)
            {

                //15秒没有心跳数据，则关闭连接释放资源
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(3600));
                var amountReadTask = fromStream.ReadAsync(buf, 0, buf.Length, ct);

                //15秒到了或者读取到了内容则进行<\X/>下一个时间片
                var completedTask = await Task.WhenAny(timeoutTask, amountReadTask);

                // 非windowsform不需要 .ConfigureAwait(false);
                if (completedTask == timeoutTask)
                {
                    Console.WriteLine("client transefer timed out");

                    break;
                }

                //在接收到信息之后可以立即发送一些消息给客户端。
                //获取read之后返回结果（结果串长度）
                var amountRead = 0;
               
                try
                {
                    Console.WriteLine("read " + amountRead.ToString() + " ");
                    amountRead = amountReadTask.Result;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
                if (amountRead == 0)
                {
                    //end of stream.流关闭之后需要保留serviceclient到provider的连接。。重新连接
                    sourceClient.Close();
                    sourceClient.Connect(TARGET_SERVICE_ADDRESS, TARGET_SERVICE_ADDRESS_PORT);
                    fromStream = sourceClient.GetStream();
                    Console.WriteLine("reconnected");
                    continue;
                }

                bool continueWrite = true;
                if (beforeTransfer != null)
                {
                    continueWrite = await beforeTransfer(buf);
                }

                if (continueWrite)
                {
                    //转发
                    await toStream.WriteAsync(buf, 0, amountRead, ct);
                }
                Console.WriteLine("Data written");
            }
            Console.WriteLine("serviceclient END+++" + signal + " ++++SourceClientConnecting:" + sourceClient.Connected.ToString());
            return signal;
        }
    }

}
