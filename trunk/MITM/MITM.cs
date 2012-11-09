﻿#region License GNU GPL
// MITM.cs
// 
// Copyright (C) 2012 - BehaviorIsManaged
// 
// This program is free software; you can redistribute it and/or modify it 
// under the terms of the GNU General Public License as published by the Free Software Foundation;
// either version 2 of the License, or (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; 
// without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. 
// See the GNU General Public License for more details. 
// You should have received a copy of the GNU General Public License along with this program; 
// if not, write to the Free Software Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using BiM.Behaviors;
using BiM.Core.Config;
using BiM.Core.Extensions;
using BiM.Core.Messages;
using BiM.Core.Network;
using BiM.MITM.Network;
using BiM.Protocol.Messages;
using BiM.Protocol.Types;
using NLog;

namespace BiM.MITM
{
    public class MITM
    {
        [Configurable("ServerConnectionTimeout", "Timeout in seconds before closing the connection")]
        public static int ServerConnectionTimeout = 4;

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly MITMConfiguration m_configuration;
        private readonly Dictionary<string, Tuple<BotMITM, SelectedServerDataMessage>> m_tickets = new Dictionary<string, Tuple<BotMITM, SelectedServerDataMessage>>();

        public MITM(MITMConfiguration configuration)
        {
            m_configuration = configuration;
            AuthConnections = new ClientManager<ConnectionMITM>(
                DnsExtensions.GetIPEndPointFromHostName(m_configuration.FakeAuthHost, m_configuration.FakeAuthPort, AddressFamily.InterNetwork), CreateAuthClient);
            WorldConnections = new ClientManager<ConnectionMITM>(
                DnsExtensions.GetIPEndPointFromHostName(m_configuration.FakeWorldHost, m_configuration.FakeWorldPort, AddressFamily.InterNetwork), CreateWorldClient);

            AuthConnections.ClientConnected += OnAuthClientConnected;
            AuthConnections.ClientDisconnected += OnAuthClientDisconnected;
            WorldConnections.ClientConnected += OnWorldClientConnected;
            WorldConnections.ClientDisconnected += OnWorldClientDisconnected;

            MessageBuilder = new MessageReceiver();
            MessageBuilder.Initialize();

            NetworkMessageDispatcher.RegisterSharedContainer(this);
        }


        public MessageReceiver MessageBuilder
        {
            get;
            set;
        }

        public ClientManager<ConnectionMITM> AuthConnections
        {
            get;
            private set;
        }

        public ClientManager<ConnectionMITM> WorldConnections
        {
            get;
            private set;
        }

        public void Start()
        {
            AuthConnections.Start();
            WorldConnections.Start();

            logger.Info("MITM started");
        }

        public void Stop()
        {
            AuthConnections.Stop();
            WorldConnections.Stop();

            logger.Info("MITM stoped");
        }

        private ConnectionMITM CreateAuthClient(Socket socket)
        {
            if (socket == null) throw new ArgumentNullException("socket");
            var client = new ConnectionMITM(socket, MessageBuilder);
            client.MessageReceived += OnAuthClientMessageReceived;

            var dispatcher = new NetworkMessageDispatcher {Client = client, Server = client.Server};

            var bot = new BotMITM(client, dispatcher);
            client.Bot = bot;
            bot.ConnectionType = ClientConnectionType.Authentification;

            BotManager.Instance.RegisterBot(bot);

            return client;
        }

        private ConnectionMITM CreateWorldClient(Socket socket)
        {
            if (socket == null) throw new ArgumentNullException("socket");
            var client = new ConnectionMITM(socket, MessageBuilder);
            client.MessageReceived += OnWorldClientMessageReceived;

            return client;
        }

        private void OnAuthClientConnected(ConnectionMITM client)
        {
            client.Bot.Start();

            try
            {
                client.BindToServer(m_configuration.RealAuthHost, m_configuration.RealAuthPort); 
            }
            catch (Exception)
            {
                logger.Error("Cannot connect to {0}:{1}.", m_configuration.RealAuthHost, m_configuration.RealAuthPort);
                client.Bot.Stop();
                return;
            }

            // unblock the connection (fix #14)
            client.SendToServer(new BasicPingMessage());

            logger.Debug("Auth client connected");
        }

        private void OnAuthClientDisconnected(ConnectionMITM client)
        {
            client.Bot.AddMessage(() =>
                {
                    if (client.Bot.ExpectedDisconnection)
                        client.Bot.Stop();
                    else
                        client.Bot.Dispose();
                });
        }

        private void OnWorldClientConnected(ConnectionMITM client)
        {
            // todo : config
            client.Send(new ProtocolRequired(1467, 1467));
            client.Send(new HelloGameMessage());

            logger.Debug("World client connected");
        }

        private void OnWorldClientDisconnected(ConnectionMITM client)
        {
            client.Bot.AddMessage(client.Bot.Dispose);
        }

        private void OnAuthClientMessageReceived(Client client, NetworkMessage message)
        {
            if (!( client is ConnectionMITM ))
                throw new ArgumentException("client is not of type ConnectionMITM");

            var mitm = client as ConnectionMITM;

            if (mitm.Bot == null)
                throw new NullReferenceException("mitm.Bot");

            mitm.Bot.Dispatcher.Enqueue(message, mitm.Bot);

            logger.Debug("{0} FROM {1}", message, message.From);
        }

        private void OnWorldClientMessageReceived(Client client, NetworkMessage message)
        {
            if (!( client is ConnectionMITM ))
                throw new ArgumentException("client is not of type ConnectionMITM");

            var mitm = client as ConnectionMITM;

            if (message is AuthenticationTicketMessage && mitm.Bot == null)
            {
                // special handling to connect and retrieve the bot instance
                HandleAuthenticationTicketMessage(mitm, message as AuthenticationTicketMessage);
            }
            else
            {
                if (mitm.Bot == null)
                    throw new NullReferenceException("mitm.Bot");

                if (mitm.Bot.Dispatcher.Stopped)
                    logger.Warn("Enqueue a message but the dispatcher is stopped !");

                mitm.Bot.Dispatcher.Enqueue(message, mitm.Bot);
            }

            logger.Debug("{0} FROM {1}", message, message.From);
        }

        private void HandleAuthenticationTicketMessage(ConnectionMITM client, AuthenticationTicketMessage message)
        {
            if (!m_tickets.ContainsKey(message.ticket))
                throw new Exception(string.Format("Ticket {0} not registered", message.ticket));

            var tuple = m_tickets[message.ticket];

            m_tickets.Remove(message.ticket);

            client.Bot = tuple.Item1;
            client.Bot.ChangeConnection(client);
            client.Bot.ConnectionType = ClientConnectionType.GameConnection;
            client.Bot.CancelAllMessages(); // avoid to handle message from the auth client.
            client.Bot.Start();

            ( (NetworkMessageDispatcher) client.Bot.Dispatcher ).Client = client;
            ( (NetworkMessageDispatcher) client.Bot.Dispatcher ).Server = client.Server;

            try
            {
                client.BindToServer(tuple.Item2.address, tuple.Item2.port);
            }
            catch (Exception)
            {
                logger.Error("Cannot connect to {0}:{1}.", tuple.Item2.address, tuple.Item2.port);
                client.Bot.Stop();
                return;
            }

            // unblock the connection (fix #14)
            client.SendToServer(new BasicPingMessage());

            client.TimeOutTimer = client.Bot.CallDelayed(ServerConnectionTimeout * 1000, () => OnServerConnectionTimedOut(client));
            logger.Debug("Bot retrieved with ticket {0}", message.ticket);
        }

        private void OnServerConnectionTimedOut(ConnectionMITM connection)
        {
            if (!connection.Server.IsConnected)
                logger.Error("Cannot etablish a connection to the server ({0}). Time out {1}ms", connection.Server.IP, ServerConnectionTimeout * 1000);
            else
            {
                logger.Warn("Send a BasicPingMessage to unblock the server connection");

                // unblock the connection (fix #14)
                connection.SendToServer(new BasicPingMessage());
            }
        }

        [MessageHandler(typeof(SelectedServerDataMessage), FromFilter = ListenerEntry.Server)]
        private void HandleSelectedServerDataMessage(Bot bot, SelectedServerDataMessage message)
        {
            m_tickets.Add(message.ticket, Tuple.Create((BotMITM)bot, new SelectedServerDataMessage(message.serverId, message.address, message.port, message.canCreateNewCharacter, message.ticket)));

            message.address = m_configuration.FakeWorldHost;
            message.port = (ushort) m_configuration.FakeWorldPort;

            ( (BotMITM)bot ).ExpectedDisconnection = true;

            logger.Debug("Client redirected to {0}:{1}", message.address, message.port);
        }

        [MessageHandler(typeof(AuthenticationTicketMessage), FromFilter = ListenerEntry.Client)]
        private static void HandleAuthenticationTicketMessage(Bot bot, AuthenticationTicketMessage message)
        {
            // theorically not received
            message.BlockNetworkSend();
        }

        [MessageHandler(typeof(ProtocolRequired), FromFilter = ListenerEntry.Server)]
        private void HandleProtocolRequired(Bot bot, ProtocolRequired message)
        {
            if (bot.ConnectionType == ClientConnectionType.GameConnection)
                message.BlockNetworkSend();
        }

        [MessageHandler(typeof(HelloGameMessage), FromFilter = ListenerEntry.Server)]
        private void HandleHelloGameMessage(Bot bot, HelloGameMessage message)
        {
            message.BlockNetworkSend();

            bot.SendToServer(new AuthenticationTicketMessage("fr", bot.ClientInformations.ConnectionTicket));

            var timer = ( (BotMITM)bot ).Connection.TimeOutTimer;
            if (timer != null)
                timer.Dispose();
        }
    }
}