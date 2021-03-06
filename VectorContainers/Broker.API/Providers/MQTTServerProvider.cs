﻿using System;
using System.Threading.Tasks;
using Broker.API.Extentions;
using Core.API.Network;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Server;
using System.Collections.Generic;
using Broker.API.Node;
using System.Linq;

namespace Broker.API.Providers
{
    public class MQTTServerProvider
    {
        private readonly IHttpClientService httpClientService;
        private readonly ILogger logger;
        private readonly IMqttServer server;
        private readonly List<INode> nodes;
        private readonly int port;

        public MQTTServerProvider(IHttpClientService httpClientService, ILogger<MQTTServerProvider> logger, int port)
        {
            this.httpClientService = httpClientService;
            this.logger = logger;
            this.port = port;

            nodes = new List<INode>();
            server = new MqttFactory().CreateMqttServer();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Task Run()
        {
            Task serverStarted = default;

            try
            {
                var options = new MqttServerOptionsBuilder()
                    .WithConnectionBacklog(100)
                    .WithApplicationMessageInterceptor(InterceptMessage)
                    .WithDefaultEndpointPort(port)
                    .WithMaxPendingMessagesPerClient(50)
                    .WithClientId($"Broker-{httpClientService.NodeIdentity}")
                    .WithPersistentSessions()
                    .Build();

                BootstrapClients();

                serverStarted = server.StartAsync(options);
            }
            catch (Exception ex)
            {
                logger.LogError($"<<< MQTTServerProvider.Run >>>: {ex.ToString()}");
            }

            return serverStarted;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        private void InterceptMessage(MqttApplicationMessageInterceptorContext context)
        {
            if (context.ApplicationMessage.IsReplicated())
            {
                context.ApplicationMessage.Topic = context.ApplicationMessage.Topic.Substring(Extentions.MqttApplicationMessageExtensions.ReplicationTopic.Length);
                Task.Factory.StartNew(() =>
                    Parallel.ForEach(nodes, (node) =>
                    {
                        try
                        {
                            node.ReplicateMessage(context.ApplicationMessage);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError($"<<< MQTTServerProvider.InterceptMessage >>>: {ex.ToString()}");
                        }
                    })
                );
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private void BootstrapClients()
        {
            logger.LogInformation("Bootstrapping replication clients...");

            nodes.AddRange((from member in httpClientService.Members
                            let url = new Uri(member.Value)
                            let node = new RemoteNode(member.Key, url.Host, url.Port)
                            select node).Select(node => node));

            Parallel.ForEach(nodes, (node) =>
            {
                try
                {
                    var isStarted = node.Start().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    logger.LogError($"<<< MQTTServerProvider.BootstrapClients >>>: {ex.ToString()}");
                }
            });
        }
    }
}
