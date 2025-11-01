﻿using linker.libs;
using linker.libs.extends;
using linker.libs.timer;
using linker.messenger.relay.messenger;
using linker.tunnel.connection;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace linker.messenger.relay.server
{
    /// <summary>
    /// 中继节点操作
    /// </summary>
    public class RelayServerNodeTransfer
    {
        /// <summary>
        /// 配置了就用配置的，每配置就用一个默认的
        /// </summary>
        public RelayServerNodeInfo Node => relayServerNodeStore.Node;

        private uint connectionNum = 0;
        private IConnection connection;
        public IConnection Connection => connection;

        private long bytes = 0;
        private long lastBytes = 0;
        private readonly RelaySpeedLimit limitTotal = new RelaySpeedLimit();
        private readonly ConcurrentDictionary<ulong, RelayTrafficCacheInfo> trafficDict = new();

        private readonly ISerializer serializer;
        private readonly IRelayServerNodeStore relayServerNodeStore;
        private readonly IMessengerResolver messengerResolver;
        private readonly IMessengerSender messengerSender;

        public RelayServerNodeTransfer(ISerializer serializer, IRelayServerNodeStore relayServerNodeStore,
            IRelayServerMasterStore relayServerMasterStore, IMessengerResolver messengerResolver, IMessengerSender messengerSender)
        {
            this.serializer = serializer;
            this.relayServerNodeStore = relayServerNodeStore;
            this.messengerResolver = messengerResolver;
            this.messengerSender = messengerSender;

            if (string.IsNullOrWhiteSpace(relayServerNodeStore.Node.MasterHost))
            {
                relayServerNodeStore.Node.Host = new IPEndPoint(IPAddress.Any, relayServerNodeStore.ServicePort).ToString();
                relayServerNodeStore.Node.MasterHost = new IPEndPoint(IPAddress.Loopback, relayServerNodeStore.ServicePort).ToString();
                relayServerNodeStore.Node.MasterSecretKey = relayServerMasterStore.Master.SecretKey;
                relayServerNodeStore.Node.Name = "default";
                relayServerNodeStore.Node.Public = false;
            }

            limitTotal.SetLimit((uint)Math.Ceiling((Node.MaxBandwidthTotal * 1024 * 1024) / 8.0));

            TrafficTask();
            ReportTask();
            SignInTask();

        }

        public async Task<RelayCacheInfo> TryGetRelayCache(string key)
        {
            try
            {
                MessageResponeInfo resp = await messengerSender.SendReply(new MessageRequestWrap
                {
                    Connection = connection,
                    MessengerId = (ushort)RelayMessengerIds.NodeGetCache186,
                    Payload = serializer.Serialize(new ValueTuple<string, string>(key, Node.Id)),
                    Timeout = 1000
                }).ConfigureAwait(false);
                if (resp.Code == MessageResponeCodes.OK && resp.Data.Length > 0)
                {
                    RelayCacheInfo result = serializer.Deserialize<RelayCacheInfo>(resp.Data.Span);
                    return result;
                }
            }
            catch (Exception ex)
            {
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                    LoggerHelper.Instance.Error($"{ex}");
            }
            return null;
        }

        public void Edit(RelayServerNodeUpdateInfo info)
        {
            if (info.Id == Node.Id)
            {
                relayServerNodeStore.UpdateInfo(new RelayServerNodeUpdateInfo188
                {
                    AllowTcp = info.AllowTcp,
                    AllowUdp = info.AllowUdp,
                    Id = info.Id,
                    Name = info.Name,
                    MaxConnection = info.MaxConnection,
                    MaxBandwidth = info.MaxBandwidth,
                    MaxBandwidthTotal = info.MaxBandwidthTotal,
                    MaxGbTotal = info.MaxGbTotal,
                    MaxGbTotalLastBytes = info.MaxGbTotalLastBytes,
                    Public = info.Public,
                    Url = info.Url,
                });
                relayServerNodeStore.Confirm();

                _ = Report();
            }
        }
        public void Edit(RelayServerNodeUpdateInfo188 info)
        {
            if (info.Id == Node.Id)
            {
                relayServerNodeStore.UpdateInfo(info);
                relayServerNodeStore.Confirm();
            }
        }
        public void Exit()
        {
            Helper.AppExit(1);
        }
        public void Update(string version)
        {
            Helper.AppUpdate(version);
        }

        public bool Validate(TunnelProtocolType tunnelProtocolType)
        {
            if (tunnelProtocolType == TunnelProtocolType.Udp && Node.AllowUdp == false) return false;
            if (tunnelProtocolType == TunnelProtocolType.Tcp && Node.AllowTcp == false) return false;

            return true;
        }
        /// <summary>
        /// 无效请求
        /// </summary>
        /// <returns></returns>
        public bool Validate(RelayCacheInfo relayCache)
        {
            return ValidateConnection(relayCache) && ValidateBytes(relayCache);
        }
        /// <summary>
        /// 连接数是否够
        /// </summary>
        /// <returns></returns>
        private bool ValidateConnection(RelayCacheInfo relayCache)
        {
            bool res = Node.MaxConnection == 0 || Node.MaxConnection * 2 > connectionNum;
            if (res == false && LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                LoggerHelper.Instance.Debug($"relay  ValidateConnection false,{connectionNum}/{Node.MaxConnection * 2}");

            return res;
        }
        /// <summary>
        /// 流量是否够
        /// </summary>
        /// <returns></returns>
        private bool ValidateBytes(RelayCacheInfo relayCache)
        {
            bool res = Node.MaxGbTotal == 0
                || (Node.MaxGbTotal > 0 && Node.MaxGbTotalLastBytes > 0);

            if (res == false && LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                LoggerHelper.Instance.Debug($"relay  ValidateBytes false,{Node.MaxGbTotalLastBytes}bytes/{Node.MaxGbTotal}gb");

            return res;
        }

        /// <summary>
        /// 增加连接数
        /// </summary>
        public void IncrementConnectionNum()
        {
            Interlocked.Increment(ref connectionNum);
        }
        /// <summary>
        /// 减少连接数
        /// </summary>
        public void DecrementConnectionNum()
        {
            Interlocked.Decrement(ref connectionNum);
        }

        /// <summary>
        /// 是否需要总限速
        /// </summary>
        /// <returns></returns>
        public bool NeedLimit(RelayTrafficCacheInfo relayCache)
        {
            return limitTotal.NeedLimit();
        }
        /// <summary>
        /// 总限速
        /// </summary>
        /// <param name="length"></param>
        /// <returns></returns>
        public bool TryLimit(ref int length)
        {
            return limitTotal.TryLimit(ref length);
        }
        /// <summary>
        /// 总限速
        /// </summary>
        /// <param name="length"></param>
        /// <returns></returns>
        public bool TryLimitPacket(int length)
        {
            return limitTotal.TryLimitPacket(length);
        }


        /// <summary>
        /// 开始计算流量
        /// </summary>
        /// <param name="relayCache"></param>
        public void AddTrafficCache(RelayTrafficCacheInfo relayCache)
        {
            SetLimit(relayCache);
            trafficDict.TryAdd(relayCache.Cache.FlowId, relayCache);
        }
        /// <summary>
        /// 取消计算流量
        /// </summary>
        /// <param name="relayCache"></param>
        public void RemoveTrafficCache(RelayTrafficCacheInfo relayCache)
        {
            trafficDict.TryRemove(relayCache.Cache.FlowId, out _);
        }
        /// <summary>
        /// 消耗流量
        /// </summary>
        /// <param name="length"></param>
        /// <returns></returns>
        public bool AddBytes(RelayTrafficCacheInfo cache, long length)
        {
            Interlocked.Add(ref bytes, length);

            if (Node.MaxGbTotal == 0) return true;

            Interlocked.Add(ref cache.Sendt, length);

            return Node.MaxGbTotalLastBytes > 0;
        }

        /// <summary>
        /// 设置限速
        /// </summary>
        /// <param name="relayCache"></param>
        private void SetLimit(RelayTrafficCacheInfo relayCache)
        {
            relayCache.CurrentCdkey = relayCache.Cache.Cdkey.Where(c => c.LastBytes > 0).OrderByDescending(c => c.Bandwidth).FirstOrDefault();
            //黑白名单
            if (relayCache.Cache.Bandwidth >= 0)
            {
                relayCache.Limit.SetLimit((uint)Math.Ceiling(relayCache.Cache.Bandwidth * 1024 * 1024 / 8.0));
                return;
            }

            //无限制
            if (relayCache.Cache.Super || Node.MaxBandwidth == 0)
            {
                relayCache.Limit.SetLimit(0);
                return;
            }

            //配置或cdkey，最大的
            double banwidth = double.Max(Node.MaxBandwidth, relayCache.CurrentCdkey?.Bandwidth ?? 1);
            relayCache.Limit.SetLimit((uint)Math.Ceiling(banwidth * 1024 * 1024 / 8.0));
        }

        /// <summary>
        /// 更新剩余流量
        /// </summary>
        /// <param name="dic"></param>
        public void UpdateLastBytes(Dictionary<int, long> dic)
        {
            if (dic.Count == 0) return;

            Dictionary<int, RelayCdkeyInfo> cdkeys = trafficDict.Values.SelectMany(c => c.Cache.Cdkey).ToDictionary(c => c.Id, c => c);
            //更新剩余流量
            foreach (KeyValuePair<int, long> item in dic)
            {
                if (cdkeys.TryGetValue(item.Key, out RelayCdkeyInfo info))
                {
                    info.LastBytes = item.Value;
                }
            }
        }
        private void ResetNodeBytes()
        {
            if (Node.MaxGbTotal == 0) return;

            foreach (var cache in trafficDict.Values.Where(c => c.CurrentCdkey == null))
            {
                long length = Interlocked.Exchange(ref cache.Sendt, 0);

                if (Node.MaxGbTotalLastBytes >= length)
                    relayServerNodeStore.SetMaxGbTotalLastBytes(Node.MaxGbTotalLastBytes - length);
                else relayServerNodeStore.SetMaxGbTotalLastBytes(0);
            }
            if (Node.MaxGbTotalMonth != DateTime.Now.Month)
            {
                relayServerNodeStore.SetMaxGbTotalMonth(DateTime.Now.Month);
                relayServerNodeStore.SetMaxGbTotalLastBytes((long)(Node.MaxGbTotal * 1024 * 1024 * 1024));
            }
            relayServerNodeStore.Confirm();
        }
        private async Task UploadBytes()
        {
            var cdkeys = trafficDict.Values.Where(c => c.CurrentCdkey != null && c.Sendt > 0 && c.CurrentCdkey.Id > 0).ToList();
            Dictionary<int, long> id2sent = cdkeys.GroupBy(c => c.CurrentCdkey.Id).ToDictionary(c => c.Key, d => d.Sum(d => { d.SendtCache = d.Sendt; return d.SendtCache; }));
            if (id2sent.Count == 0) return;

            bool result = await messengerSender.SendOnly(new MessageRequestWrap
            {
                Connection = connection,
                MessengerId = (ushort)RelayMessengerIds.TrafficReport,
                Payload = serializer.Serialize(new RelayTrafficUpdateInfo
                {
                    Dic = id2sent,
                    SecretKey = Node.MasterSecretKey
                }),
                Timeout = 4000
            }).ConfigureAwait(false);

            if (result)
            {
                //成功报告了流量，就重新计数
                foreach (var cache in cdkeys)
                {
                    Interlocked.Add(ref cache.Sendt, -cache.SendtCache);

                    if (Node.MaxGbTotalLastBytes >= cache.SendtCache)
                        relayServerNodeStore.SetMaxGbTotalLastBytes(Node.MaxGbTotalLastBytes - cache.SendtCache);
                    else relayServerNodeStore.SetMaxGbTotalLastBytes(0);

                    Interlocked.Exchange(ref cache.SendtCache, 0);
                    //当前cdkey流量用完了，就重新找找新的cdkey
                    if (cache.CurrentCdkey.LastBytes <= 0)
                    {
                        SetLimit(cache);
                    }
                }
                relayServerNodeStore.Confirm();
            }
        }
        private void TrafficTask()
        {
            TimerHelper.SetIntervalLong(async () =>
            {
                try
                {
                    ResetNodeBytes();
                    await UploadBytes().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LoggerHelper.Instance.Error(ex);
                }
            }, 3000);
        }

        private void ReportTask()
        {
            TimerHelper.SetIntervalLong(async () =>
            {
                await Report();
                await MasterHosts();
            }, 5000);
        }
        private async Task Report()
        {
            double diff = (bytes - lastBytes) * 8 / 1024.0 / 1024.0;
            lastBytes = bytes;

            try
            {
                IPEndPoint endPoint = await NetworkHelper.GetEndPointAsync(Node.Host, relayServerNodeStore.ServicePort).ConfigureAwait(false) ?? new IPEndPoint(IPAddress.Any, relayServerNodeStore.ServicePort);
                RelayServerNodeReportInfo188 relayNodeReportInfo = new RelayServerNodeReportInfo188
                {
                    Id = Node.Id,
                    Name = Node.Name,
                    Public = Node.Public,
                    MaxBandwidth = Node.MaxBandwidth,
                    BandwidthRatio = Math.Round(diff / 5, 2),
                    MaxBandwidthTotal = Node.MaxBandwidthTotal,
                    MaxGbTotal = Node.MaxGbTotal,
                    MaxGbTotalLastBytes = Node.MaxGbTotalLastBytes,
                    MaxConnection = Node.MaxConnection,
                    ConnectionRatio = connectionNum,
                    EndPoint = endPoint,
                    Url = Node.Url,
                    AllowProtocol = (Node.AllowTcp ? TunnelProtocolType.Tcp : TunnelProtocolType.None)
                     | (Node.AllowUdp ? TunnelProtocolType.Udp : TunnelProtocolType.None),
                    Sync2Server = Node.Sync2Server,
                    Version = VersionHelper.Version
                };
                var resp = await messengerSender.SendReply(new MessageRequestWrap
                {
                    Connection = connection,
                    MessengerId = (ushort)RelayMessengerIds.NodeReport188,
                    Payload = serializer.Serialize(relayNodeReportInfo)
                }).ConfigureAwait(false);
                if (Node.Sync2Server && resp.Code == MessageResponeCodes.OK && resp.Data.Length > 0)
                {
                    string version = serializer.Deserialize<string>(resp.Data.Span);
                    if (version != VersionHelper.Version)
                    {
                        Helper.AppUpdate(version);
                    }
                }
            }
            catch (Exception ex)
            {
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                {
                    LoggerHelper.Instance.Error($"relay report : {ex}");
                }
            }
        }
        private async Task MasterHosts()
        {
            try
            {
                if (signInHost != Node.MasterHost) return;
                var resp = await messengerSender.SendReply(new MessageRequestWrap
                {
                    Connection = Connection,
                    MessengerId = (ushort)RelayMessengerIds.Hosts,
                }).ConfigureAwait(false);
                if (resp.Code == MessageResponeCodes.OK && resp.Data.Length > 0)
                {
                    string[] hosts = serializer.Deserialize<string[]>(resp.Data.Span);
                    relayServerNodeStore.SetMasterHosts(hosts);
                }
            }
            catch (Exception)
            {
            }
        }


        private string signInHost = string.Empty;
        private void SignInTask()
        {
            TimerHelper.SetIntervalLong(async () =>
            {
                if (Connection == null || Connection.Connected == false)
                {
                    string[] hosts = [Node.MasterHost, .. Node.MasterHosts];
                    foreach (var host in hosts.Where(c => string.IsNullOrWhiteSpace(c) == false))
                    {
                        connection = await SignIn(host, Node.MasterSecretKey).ConfigureAwait(false);
                        if (connection != null && connection.Connected)
                        {
                            signInHost = host;
                            break;
                        }
                    }
                }
                else
                {
                    if (await TestHost(Node.MasterHost))
                    {
                        connection = await SignIn(Node.MasterHost, Node.MasterSecretKey).ConfigureAwait(false);
                        if (connection != null && connection.Connected)
                        {
                            signInHost = Node.MasterHost;
                        }
                    }
                }
            }, 3000);
        }
        private async Task<bool> TestHost(string host)
        {
            if (signInHost == Node.MasterHost) return false;
            try
            {
                IPEndPoint ip = await NetworkHelper.GetEndPointAsync(host, 1802).ConfigureAwait(false);
                using Socket socket = new Socket(ip.Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                socket.KeepAlive();
                await socket.ConnectAsync(ip).WaitAsync(TimeSpan.FromMilliseconds(5000)).ConfigureAwait(false);

                socket.SafeClose();
                return true;
            }
            catch (Exception)
            {
            }
            return false;
        }
        private async Task<IConnection> SignIn(string host, string secretKey)
        {
            byte[] bytes = ArrayPool<byte>.Shared.Rent(1024);
            try
            {
                byte[] secretKeyBytes = secretKey.Sha256().ToBytes();

                bytes[0] = (byte)secretKeyBytes.Length;
                secretKeyBytes.AsSpan().CopyTo(bytes.AsSpan(1));


                IPEndPoint remote = await NetworkHelper.GetEndPointAsync(host, 1802).ConfigureAwait(false);
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                {
                    LoggerHelper.Instance.Warning($"relay node sign in to {remote}");
                }

                Socket socket = new Socket(remote.Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                socket.KeepAlive();
                await socket.ConnectAsync(remote).WaitAsync(TimeSpan.FromMilliseconds(5000)).ConfigureAwait(false);
                return await messengerResolver.BeginReceiveClient(socket, true, (byte)ResolverType.RelayReport, bytes.AsMemory(0, secretKeyBytes.Length + 1)).ConfigureAwait(false);
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
                ArrayPool<byte>.Shared.Return(bytes);
            }
            return null;
        }
    }

    public sealed partial class RelayTrafficUpdateInfo
    {
        /// <summary>
        /// cdkey id  和 流量
        /// </summary>
        public Dictionary<int, long> Dic { get; set; }
        public string SecretKey { get; set; }
    }

}
