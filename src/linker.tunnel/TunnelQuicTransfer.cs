using linker.libs;
using linker.libs.extends;
using linker.libs.timer;
using linker.tunnel.connection;
using linker.tunnel.transport;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace linker.tunnel
{
#pragma warning disable CA2252 // 此 API 需要选择加入预览功能
    public sealed class TunnelQuicTransfer
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<(QuicConnection connection, QuicStream stream)>> dic = new();
        private IPEndPoint quicListenEP = new IPEndPoint(IPAddress.Any, 0);
        public void Listen(X509Certificate certificate)
        {
            _ = QuicListen(certificate);
        }
        public async Task<ITunnelConnection> Transform(ITunnelConnection connection, TunnelTransportInfo info)
        {
            return connection;

            if (connection is TunnelConnectionUdp udp == false || udp.TransactionId == "tuntap")
            {
                return connection;
            }

            try
            {

                if (udp.Direction == TunnelDirection.Forward)
                {
                    Socket quicUdp = Local2RemoteQuic(udp.UdpClient, udp.IPEndPoint);

                    using CancellationTokenSource cts = new CancellationTokenSource(3000);
                    QuicConnection quicConnection = await QuicConnection.ConnectAsync(new QuicClientConnectionOptions
                    {
                        RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, (quicUdp.LocalEndPoint as IPEndPoint).Port),
                        LocalEndPoint = new IPEndPoint(IPAddress.Any, 0),
                        DefaultCloseErrorCode = 0x0a,
                        DefaultStreamErrorCode = 0x0b,
                        IdleTimeout = TimeSpan.FromMilliseconds(15000),
                        ClientAuthenticationOptions = new SslClientAuthenticationOptions
                        {
                            ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http3 },
                            EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
                            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                            {
                                return true;
                            }
                        }
                    }, cts.Token).ConfigureAwait(false);
                    QuicStream quicStream = await quicConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional).ConfigureAwait(false);

                    string key = $"{info.Remote.MachineId}->{info.TransactionId}->{info.FlowId}";
                    await quicStream.WriteAsync(Encoding.UTF8.GetBytes(key)).ConfigureAwait(false);
                }
                else
                {
                    _ = Remote2LocalQuic(udp.UdpClient, udp.IPEndPoint);
                    string key = $"{info.Local.MachineId}->{info.TransactionId}->{info.FlowId}";
                    try
                    {
                        using CancellationTokenSource cts = new CancellationTokenSource(3000);
                        TaskCompletionSource<(QuicConnection connection, QuicStream stream)> tcs = new(cts.Token);
                        dic.AddOrUpdate(key, tcs, (k, v) => tcs);
                        (QuicConnection quicConnection, QuicStream quicStream) = await tcs.Task.ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        dic.TryRemove(key, out _);
                    }
                }
            }
            catch (Exception)
            {
            }

            connection?.Dispose();
            return null;
        }

        private async Task Remote2LocalQuic(Socket remoteUdp, IPEndPoint remoteEp)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(8 * 1024);
            IPEndPoint tempEp = new IPEndPoint(IPAddress.Any, IPEndPoint.MinPort);
            Socket quicUdp = null;
            try
            {
                //等待对方来一条消息
                SocketReceiveFromResult result = await remoteUdp.ReceiveFromAsync(buffer, tempEp).ConfigureAwait(false);

                //发给QUIC监听，因为UDP，必须先发一条数据，然后才能接收，所以，先给QUIC发一条，才能拿去交换数据
                quicUdp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                quicUdp.WindowsUdpBug();
                await quicUdp.SendToAsync(buffer.AsMemory(0, result.ReceivedBytes), quicListenEP).ConfigureAwait(false);

                //然后就可以交换数据了
                await Task.WhenAny(CopyToAsync(remoteUdp, quicUdp, quicListenEP), CopyToAsync(quicUdp, remoteUdp, remoteEp)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                {
                    LoggerHelper.Instance.Error(ex);
                }
            }
            finally
            {
                quicUdp?.SafeClose();
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }


        private Socket Local2RemoteQuic(Socket remoteUdp, IPEndPoint remoteEP)
        {
            Socket localUdp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
            localUdp.Bind(new IPEndPoint(IPAddress.Any, 0));
            localUdp.WindowsUdpBug();

            _ = Local2RemoteQuic(remoteUdp, remoteEP, localUdp);

            return localUdp;
        }
        private async Task Local2RemoteQuic(Socket remoteUdp, IPEndPoint remoteEP, Socket localUdp)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(8 * 1024);
            IPEndPoint tempEp = new IPEndPoint(IPAddress.Any, IPEndPoint.MinPort);
            try
            {
                SocketReceiveFromResult result = await localUdp.ReceiveFromAsync(buffer, tempEp).ConfigureAwait(false);
                //quic的地址
                IPEndPoint quicEp = result.RemoteEndPoint as IPEndPoint;
                //发送给远端
                await remoteUdp.SendToAsync(buffer.AsMemory(0, result.ReceivedBytes), remoteEP).ConfigureAwait(false);

                await Task.WhenAny(CopyToAsync(localUdp, remoteUdp, remoteEP), CopyToAsync(remoteUdp, localUdp, quicEp)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                {
                    LoggerHelper.Instance.Error(ex);
                }
            }
            finally
            {
                localUdp?.SafeClose();
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        private async Task CopyToAsync(Socket local, Socket remote, IPEndPoint remoteEp)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(8 * 1024);
            IPEndPoint tempEp = new IPEndPoint(IPAddress.Any, IPEndPoint.MinPort);
            try
            {
                while (true)
                {
                    SocketReceiveFromResult result = await local.ReceiveFromAsync(buffer, tempEp).ConfigureAwait(false);
                    if (result.ReceivedBytes == 0)
                    {
                        continue;
                    }
                    await remote.SendToAsync(buffer.AsMemory(0, result.ReceivedBytes), remoteEp).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                {
                    LoggerHelper.Instance.Error(ex);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        private async Task QuicListen(X509Certificate certificate)
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                if (QuicListener.IsSupported == false)
                {
                    LoggerHelper.Instance.Warning($"msquic not supported, need win11+,or linux, or try to restart linker");
                    return;
                }
                if (certificate == null)
                {
                    LoggerHelper.Instance.Warning($"msquic need ssl");
                    return;
                }
                QuicListener listener = await QuicListener.ListenAsync(new QuicListenerOptions
                {
                    ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http3 },
                    ListenBacklog = int.MaxValue,
                    ListenEndPoint = new IPEndPoint(IPAddress.Any, 0),
                    ConnectionOptionsCallback = (connection, hello, token) =>
                    {
                        return ValueTask.FromResult(new QuicServerConnectionOptions
                        {
                            MaxInboundBidirectionalStreams = 65535,
                            MaxInboundUnidirectionalStreams = 65535,
                            DefaultCloseErrorCode = 0x0a,
                            DefaultStreamErrorCode = 0x0b,
                            IdleTimeout = TimeSpan.FromMilliseconds(15000),
                            ServerAuthenticationOptions = new SslServerAuthenticationOptions
                            {
                                ServerCertificate = certificate,
                                EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
                                ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http3 }
                            }
                        });
                    }
                }).ConfigureAwait(false);

                quicListenEP = new IPEndPoint(IPAddress.Loopback, listener.LocalEndPoint.Port);
                while (true)
                {
                    try
                    {
                        QuicConnection quicConnection = await listener.AcceptConnectionAsync().ConfigureAwait(false);
                        TimerHelper.Async(async () =>
                        {
                            while (true)
                            {
                                QuicStream quicStream = await quicConnection.AcceptInboundStreamAsync().ConfigureAwait(false);
                                try
                                {
                                    using CancellationTokenSource cts = new CancellationTokenSource(3000);
                                    using IMemoryOwner<byte> bufferOwner = MemoryPool<byte>.Shared.Rent(8 * 1024);
                                    int length = await quicStream.ReadAsync(bufferOwner.Memory, cts.Token).ConfigureAwait(false);
                                    string key = Encoding.UTF8.GetString(bufferOwner.Memory.Slice(0, length).Span);
                                    if (dic.TryRemove(key, out TaskCompletionSource<(QuicConnection connection, QuicStream stream)> tcs))
                                    {
                                        tcs.TrySetResult((quicConnection, quicStream));
                                    }
                                    else
                                    {
                                        quicStream.Close();
                                    }
                                }
                                catch (Exception)
                                {
                                    quicStream?.Close();
                                }
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                        {
                            LoggerHelper.Instance.Error(ex);
                        }
                        break;
                    }
                }
            }
        }
    }
}
