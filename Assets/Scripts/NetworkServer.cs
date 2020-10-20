using UnityEngine;
using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Networking.Transport;
using NetworkMessages;
using System;
using System.Text;
using System.Collections.Generic;
using NetworkObjects;

public class NetworkServer : MonoBehaviour
{
    public NetworkDriver m_Driver;
    public ushort serverPort;
    private NativeList<NetworkConnection> m_Connections;
    private List<NetworkObjects.NetworkPlayer> players =
        new List<NetworkObjects.NetworkPlayer>();

    void Start ()
    {
        m_Driver = NetworkDriver.Create();
        var endpoint = NetworkEndPoint.AnyIpv4;
        endpoint.Port = serverPort;
        if (m_Driver.Bind(endpoint) != 0)
            Debug.Log("Failed to bind to port " + serverPort);
        else
            m_Driver.Listen();

        m_Connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);

        StartCoroutine(ServerUpdate());
    }

    System.Collections.IEnumerator ServerUpdate()
    {
        while (this.isActiveAndEnabled)
        {
            //Create message with list of all players
            ServerUpdateMsg update = new ServerUpdateMsg();
            update.players = players;
            for (int i = 0; i < m_Connections.Length; i++)
            {
                if (!m_Connections[i].IsCreated)
                    continue;
                //Send List of players to client
                SendToClient(JsonUtility.ToJson(update), m_Connections[i]);
            }
            Debug.Log("Update Sent to Clients");
            yield return new WaitForSeconds(1);
        }
    }

    void SendToClient(string message, NetworkConnection c){
        var writer = m_Driver.BeginSend(NetworkPipeline.Null, c);
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message),Allocator.Temp);
        writer.WriteBytes(bytes);
        m_Driver.EndSend(writer);
    }
    public void OnDestroy()
    {
        m_Driver.Dispose();
        m_Connections.Dispose();
    }

    void OnConnect(NetworkConnection c){
        m_Connections.Add(c);
        Debug.Log("Accepted a connection");

        //Send initial Message to client
        InitialMsg m = new InitialMsg();
        m.serverID = c.InternalId.ToString();
        SendToClient(JsonUtility.ToJson(m), c);

        //Build new player
        var player = new NetworkObjects.NetworkPlayer();
        player.id = c.InternalId.ToString();
        player.cubeColor = new Color(
            UnityEngine.Random.Range(0.0f, 1.0f),
            UnityEngine.Random.Range(0.0f, 1.0f),
            UnityEngine.Random.Range(0.0f, 1.0f));
        //Add to servers list
        //will be added to all clients on next update
        players.Add(player);
    }

    void OnData(DataStreamReader stream, int i){
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length,Allocator.Temp);
        stream.ReadBytes(bytes);
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);

        switch(header.cmd){
            case Commands.HANDSHAKE:
                HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg);
                Debug.Log("Handshake message received!: " + hsMsg.player.id);
                break;
            case Commands.PLAYER_UPDATE:
                PlayerUpdateMsg puMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                Debug.Log("Player update message received!");
                UpdatePlayer(puMsg.player);
                break;
            case Commands.SERVER_UPDATE:
                ServerUpdateMsg suMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
                Debug.Log("Server update message received!");
                break;
            case Commands.PLAYER_DISCONNECT:
                DisconnectMsg disMsg = JsonUtility.FromJson<DisconnectMsg>(recMsg);
                Debug.Log("Player disconnect received!");
                DisconnectPlayer(disMsg.serverID);
                break;
            default:
                Debug.Log("SERVER ERROR: Unrecognized message received!");
                break;
        }
    }

    private void UpdatePlayer(NetworkObjects.NetworkPlayer client)
    {
        //Find client in players list
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].id == client.id)
            {
                players[i] = client;
                Debug.Log("Player Updated Succesfully: " + client.id);
                break;
            }
        }
    }

    void DisconnectPlayer(string id)
    {
        //remove player with that id
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].id == id)
            {
                players.RemoveAt(i);
                Debug.Log("Player Removed Succesfully: " + id);
                break;
            }
        }

        //send disconnected id to connected clients
        for (int i = 0; i < m_Connections.Length; i++)
        {
            if (m_Connections[i].IsCreated)
            {
                DisconnectMsg disconnect = new DisconnectMsg();
                disconnect.serverID = id;
                SendToClient(JsonUtility.ToJson(disconnect), m_Connections[i]);
            }
        }

    }

    void OnDisconnect(int connection){
        //get the internal id
        string id = m_Connections[connection].InternalId.ToString();
        //delete the connection
        Debug.Log("Client disconnected from server");
        m_Connections[connection] = default(NetworkConnection);

        DisconnectPlayer(id);
    }

    void Update ()
    {
        m_Driver.ScheduleUpdate().Complete();

        // CleanUpConnections
        for (int i = 0; i < m_Connections.Length; i++)
        {
            if (!m_Connections[i].IsCreated)
            {
                m_Connections.RemoveAtSwapBack(i);
                --i;
            }
        }

        // AcceptNewConnections
        NetworkConnection c = m_Driver.Accept();
        while (c  != default(NetworkConnection))
        {            
            OnConnect(c);

            // Check if there is another new connection
            c = m_Driver.Accept();
        }

        // Read Incoming Messages
        DataStreamReader stream;
        for (int i = 0; i < m_Connections.Length; i++)
        {
            Assert.IsTrue(m_Connections[i].IsCreated);
            
            NetworkEvent.Type cmd;
            cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            while (cmd != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Data)
                {
                    OnData(stream, i);
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    OnDisconnect(i);
                }

                cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            }
        }
    }
}