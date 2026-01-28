using LiteNetLib;
using LiteNetLib.Utils;
using CatCube.Shared;
using System.Net;

namespace CatCube.Network;

public class NetworkClient : IDisposable
{
    private EventBasedNetListener _listener;
    private NetManager _client;
    private NetPeer? _serverPeer;
    private NetDataWriter _writer;
    private int _localId = -1;
    
    // Callbacks
    public Action<int>? OnConnected;
    public Action<int, PlayerState>? OnPlayerStateReceived;
    public Action<int>? OnPlayerLeft;
    public Action? OnDisconnected;
    public int LocalId => _localId;

    public NetworkClient()
    {
        _listener = new EventBasedNetListener();
        _client = new NetManager(_listener);
        _writer = new NetDataWriter();
        
        // Corrected signature: 4 arguments
        _listener.NetworkReceiveEvent += (peer, reader, channel, deliveryMethod) =>
        {
            PacketType type = (PacketType)reader.GetByte();
            
            switch (type)
            {
                case PacketType.WorldState:
                    int count = reader.GetInt();
                    Console.WriteLine($"[Network] Received WorldState: {count} players");
                    for(int i=0; i<count; i++)
                    {
                        PlayerState state = new PlayerState();
                        state.Deserialize(reader);
                        OnPlayerStateReceived?.Invoke(state.Id, state);
                    }
                    break;
                    
                case PacketType.PlayerState:
                    PlayerState pState = new PlayerState();
                    pState.Deserialize(reader);
                    if (pState.Id != _localId)
                        Console.WriteLine($"[Network] Received PlayerState for {pState.Id} ({pState.Username})");
                    OnPlayerStateReceived?.Invoke(pState.Id, pState);
                    break;
                    
                case PacketType.PlayerLeft:
                    int leftId = reader.GetInt();
                    OnPlayerLeft?.Invoke(leftId);
                    break;
            }
        };
        
        _listener.PeerConnectedEvent += peer =>
        {
            Console.WriteLine("Connected to server!");
            _serverPeer = peer;
            _localId = peer.EndPoint.Port;
            OnConnected?.Invoke(_localId); // Use port as local ID
        };
        
        _listener.PeerDisconnectedEvent += (peer, info) =>
        {
            Console.WriteLine("Disconnected from server");
            _serverPeer = null;
            OnDisconnected?.Invoke();
        };
        
        _client.Start();
    }

    public void Connect(string ip, int port, string username, AvatarData avatar)
    {
        Console.WriteLine($"Connecting to {ip}:{port} as {username}...");
        
        NetDataWriter connectData = new NetDataWriter();
        // The Key must be the first thing if using AcceptIfKey or similar, 
        // but often it is better to just put it in the writer.
        // Actually, LiteNetLib Connect has an overload with key.
        
        connectData.Put(username);
        avatar.Serialize(connectData);
        
        _client.Connect(ip, port, NetworkConfig.Key, connectData);
    }

    public void SendState(PlayerState state)
    {
        if (_serverPeer == null) return;
        
        _writer.Reset();
        _writer.Put((byte)PacketType.PlayerState);
        state.Serialize(_writer);
        
        _serverPeer.Send(_writer, DeliveryMethod.Unreliable); // Position updates can be unreliable (fast)
    }

    public void PollEvents()
    {
        _client.PollEvents();
    }

    public void Dispose()
    {
        _client.Stop();
    }
}
