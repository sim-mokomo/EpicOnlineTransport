using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Epic.OnlineServices.P2P;
using Epic.OnlineServices;
using Mirror;
using Epic.OnlineServices.Metrics;
using System.Collections;
using PlayEveryWare.EpicOnlineServices;

namespace EpicTransport {

    /// <summary>
    /// EOS Transport following the Mirror transport standard
    /// </summary>
    public class EosTransport : Transport {
        private const string EPIC_SCHEME = "epic";

        private Client client;
        private Server server;

        private Common activeNode;

        [SerializeField]
        public PacketReliability[] Channels = new PacketReliability[2] { PacketReliability.ReliableOrdered, PacketReliability.UnreliableUnordered };
        
        [Tooltip("Timeout for connecting in seconds.")]
        public int timeout = 25;

        [Tooltip("The max fragments used in fragmentation before throwing an error.")]
        public int maxFragments = 55;

        public float ignoreCachedMessagesAtStartUpInSeconds = 2.0f;
        private float ignoreCachedMessagesTimer = 0.0f;

        public RelayControl relayControl = RelayControl.AllowRelays;

        [Header("Info")]
        [Tooltip("This will display your Epic Account ID when you start or connect to a server.")]
        public ProductUserId productUserId;

        private int packetId = 0;

        private void Awake() {
            Debug.Assert(Channels != null && Channels.Length > 0, "No channel configured for EOS Transport.");
            Debug.Assert(Channels.Length < byte.MaxValue, "Too many channels configured for EOS Transport");

            if(Channels[0] != PacketReliability.ReliableOrdered) {
                Debug.LogWarning("EOS Transport Channel[0] is not ReliableOrdered, Mirror expects Channel 0 to be ReliableOrdered, only change this if you know what you are doing.");
            }
            if (Channels[1] != PacketReliability.UnreliableUnordered) {
                Debug.LogWarning("EOS Transport Channel[1] is not UnreliableUnordered, Mirror expects Channel 1 to be UnreliableUnordered, only change this if you know what you are doing.");
            }

            StartCoroutine("FetchEpicAccountId");
            StartCoroutine("ChangeRelayStatus");
        }

        public override void ClientEarlyUpdate() {
            EOSManager.Instance.Tick();

            if (activeNode != null) {
                ignoreCachedMessagesTimer += Time.deltaTime;

                if (ignoreCachedMessagesTimer <= ignoreCachedMessagesAtStartUpInSeconds) {
                    activeNode.ignoreAllMessages = true;
                } else {
                    activeNode.ignoreAllMessages = false;

                    if (client != null && !client.isConnecting) {
                        if (EOSManager.Instance.IsLoggedIn())
                        {
                            client.Connect(client.hostAddress);
                        } else {
                            Debug.LogError("EOS not initialized");
                            client.EosNotInitialized();
                        }
                        client.isConnecting = true;
                    }
                }
            }

            if (enabled) {
                activeNode?.ReceiveData();
            }
        }

        public override void ClientLateUpdate() {}

        public override void ServerEarlyUpdate() {
            EOSManager.Instance.Tick();

            if (activeNode != null) {
                ignoreCachedMessagesTimer += Time.deltaTime;

                if (ignoreCachedMessagesTimer <= ignoreCachedMessagesAtStartUpInSeconds) {
                    activeNode.ignoreAllMessages = true;
                } else {
                    activeNode.ignoreAllMessages = false;
                }
            }

            if (enabled) {
                activeNode?.ReceiveData();
            }
        }

        public override void ServerLateUpdate() {}

        public override bool ClientConnected() => ClientActive() && client.Connected;
        public override void ClientConnect(string address) {
            if (!EOSManager.Instance.IsLoggedIn()) {
                Debug.LogError("EOS not initialized. Client could not be started.");
                OnClientDisconnected.Invoke();
                return;
            }

            StartCoroutine("FetchEpicAccountId");

            if (ServerActive()) {
                Debug.LogError("Transport already running as server!");
                return;
            }

            if (!ClientActive() || client.Error) {
                Debug.Log($"Starting client, target address {address}.");

                client = Client.CreateClient(this, address);
                activeNode = client;

                var eosManager = EOSManager.Instance;
                if (eosManager.EnableCollectPlayerMetrics()) {
                    // Start Metrics colletion session
                    BeginPlayerSessionOptions sessionOptions = new BeginPlayerSessionOptions();
                    sessionOptions.AccountId = eosManager.GetLocalUserId();
                    sessionOptions.ControllerType = UserControllerType.Unknown;
                    sessionOptions.DisplayName = eosManager.GetDisplayUserName();
                    sessionOptions.GameSessionId = null;
                    sessionOptions.ServerIp = null;
                    
                    Result result = eosManager.GetEOSMetricsInterface().BeginPlayerSession(ref sessionOptions);

                    if(result == Result.Success) {
                        Debug.Log("Started Metric Session");
                    }
                }
            } else {
                Debug.LogError("Client already running!");
            }
        }

        public override void ClientConnect(Uri uri) {
            if (uri.Scheme != EPIC_SCHEME)
                throw new ArgumentException($"Invalid url {uri}, use {EPIC_SCHEME}://EpicAccountId instead", nameof(uri));

            ClientConnect(uri.Host);
        }

        public override void ClientSend(ArraySegment<byte> segment, int channelId = Mirror.Channels.Reliable) {
            Send(channelId, segment);
        }

        public override void ClientDisconnect() {
            if (ClientActive()) {
                Shutdown();
            }
        }
        public bool ClientActive() => client != null;


        public override bool ServerActive() => server != null;
        public override void ServerStart()
        {
            var eosManager = EOSManager.Instance;
            if (!eosManager.IsLoggedIn()) {
                Debug.LogError("EOS not initialized. Server could not be started.");
                return;
            }

            StartCoroutine("FetchEpicAccountId");

            if (ClientActive()) {
                Debug.LogError("Transport already running as client!");
                return;
            }

            if (!ServerActive()) {
                Debug.Log("Starting server.");

                server = Server.CreateServer(this, NetworkManager.singleton.maxConnections);
                activeNode = server;

                if (eosManager.EnableCollectPlayerMetrics()) {
                    // Start Metrics colletion session
                    BeginPlayerSessionOptions sessionOptions = new BeginPlayerSessionOptions();
                    sessionOptions.AccountId = eosManager.GetLocalUserId();
                    sessionOptions.ControllerType = UserControllerType.Unknown;
                    sessionOptions.DisplayName = eosManager.GetDisplayUserName();
                    sessionOptions.GameSessionId = null;
                    sessionOptions.ServerIp = null;
                    Result result = eosManager.GetEOSMetricsInterface().BeginPlayerSession(ref sessionOptions);

                    if (result == Result.Success) {
                        Debug.Log("Started Metric Session");
                    }
                }
            } else {
                Debug.LogError("Server already started!");
            }
        }

        public override Uri ServerUri()
        {
            EOSManager.Instance.GetProductUserId().ToString(out var productUserIdString);
            UriBuilder epicBuilder = new UriBuilder { 
                Scheme = EPIC_SCHEME,
                Host = productUserIdString
            };

            return epicBuilder.Uri;
        }

        public override void ServerSend(int connectionId, ArraySegment<byte> segment, int channelId = Mirror.Channels.Reliable) {
            if (ServerActive()) {
                Send( channelId, segment, connectionId);
            }
        }
        public override void ServerDisconnect(int connectionId) => server.Disconnect(connectionId);
        public override string ServerGetClientAddress(int connectionId) => ServerActive() ? server.ServerGetClientAddress(connectionId) : string.Empty;
        public override void ServerStop() {
            if (ServerActive()) {
                Shutdown();
            }
        }

        private void Send(int channelId, ArraySegment<byte> segment, int connectionId = int.MinValue) {
            Packet[] packets = GetPacketArray(channelId, segment);

            for(int i  = 0; i < packets.Length; i++) {
                if (connectionId == int.MinValue) {
                    client.Send(packets[i].ToBytes(), channelId);
                } else {
                    server.SendAll(connectionId, packets[i].ToBytes(), channelId);
                }
            }

            packetId++;
        }

        private Packet[] GetPacketArray(int channelId, ArraySegment<byte> segment) {
            int packetCount = Mathf.CeilToInt((float) segment.Count / (float)GetMaxSinglePacketSize(channelId));
            Packet[] packets = new Packet[packetCount];

            for (int i = 0; i < segment.Count; i += GetMaxSinglePacketSize(channelId)) {
                int fragment = i / GetMaxSinglePacketSize(channelId);

                packets[fragment] = new Packet();
                packets[fragment].id = packetId;
                packets[fragment].fragment = fragment;
                packets[fragment].moreFragments = (segment.Count - i) > GetMaxSinglePacketSize(channelId);
                packets[fragment].data = new byte[segment.Count - i > GetMaxSinglePacketSize(channelId) ? GetMaxSinglePacketSize(channelId) : segment.Count - i];
                Array.Copy(segment.Array, i, packets[fragment].data, 0, packets[fragment].data.Length);
            }

            return packets;
        }

        public override void Shutdown()
        {
            var eosManager = EOSManager.Instance;
            if (eosManager.EnableCollectPlayerMetrics()) {
                // Stop Metrics collection session
                EndPlayerSessionOptions endSessionOptions = new EndPlayerSessionOptions();
                endSessionOptions.AccountId = EOSManager.Instance.GetLocalUserId();
                Result result = eosManager.GetEOSMetricsInterface().EndPlayerSession(ref endSessionOptions);

                if (result == Result.Success) {
                    Debug.LogError("Stopped Metric Session");
                }
            }

            server?.Shutdown();
            client?.Disconnect();

            server = null;
            client = null;
            activeNode = null;
            Debug.Log("Transport shut down.");
        }

        public int GetMaxSinglePacketSize(int channelId) => P2PInterface.MaxPacketSize - 10; // 1159 bytes, we need to remove 10 bytes for the packet header (id (4 bytes) + fragment (4 bytes) + more fragments (1 byte)) 

        public override int GetMaxPacketSize(int channelId) => P2PInterface.MaxPacketSize * maxFragments;

        public override bool Available() {
            try {
                return EOSManager.Instance.IsLoggedIn();
            } catch {
                return false;
            }
        }

        private IEnumerator FetchEpicAccountId()
        {
            var eosManager = EOSManager.Instance;
            while (!eosManager.IsLoggedIn()) {
                yield return null;
            }

            productUserId = eosManager.GetProductUserId();
        }

        private IEnumerator ChangeRelayStatus() {
            var eosManager = EOSManager.Instance;
            while (!eosManager.IsLoggedIn()) {
                yield return null;
            }

            SetRelayControlOptions setRelayControlOptions = new SetRelayControlOptions();
            setRelayControlOptions.RelayControl = relayControl;
            
            eosManager.GetEOSP2PInterface().SetRelayControl(ref setRelayControlOptions);
        }

        public void ResetIgnoreMessagesAtStartUpTimer() {
            ignoreCachedMessagesTimer = 0;
        }

        private void OnDestroy() {
            if (activeNode != null) {
                Shutdown();
            }
        }
    }
}
